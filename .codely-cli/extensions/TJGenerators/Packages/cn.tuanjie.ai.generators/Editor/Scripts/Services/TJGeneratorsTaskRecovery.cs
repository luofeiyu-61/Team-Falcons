#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TJGenerators.Utils;

namespace TJGenerators
{
    [Serializable]
    public class InterruptedTaskData
    {
        public string backendTaskId;      // 后端返回的任务ID
        public string localTaskId;        // 本地唯一ID（用于关联历史记录）
        public string prompt;
        public string imagePath;          // 图生模型时使用
        public string modelVersion;
        public bool isTextToModel;
        public long timestamp;
        public string sessionId;
        public string targetAssetGuid;
        public string status;

        public int faceLimit;
        public bool texture;
        public bool pbr;
    }

    public static class TJGeneratorsTaskRecovery
    {
        private const string InterruptedTasksFilePath = "Library/AI.TJGenerators/InterruptedTasks.json";

        public static readonly string SessionId = Guid.NewGuid().ToString();

        private static List<InterruptedTaskData> s_Tasks;
        private static readonly HashSet<string> s_Recovering = new HashSet<string>();

        static TJGeneratorsTaskRecovery() => Load();

        // ── Persistence ────────────────────────────────────────────────────

        private static void Load()
        {
            if (!File.Exists(InterruptedTasksFilePath))
            {
                s_Tasks = new List<InterruptedTaskData>();
                return;
            }

            try
            {
                var json = File.ReadAllText(InterruptedTasksFilePath);
                s_Tasks = JsonUtility.FromJson<TaskListWrapper>(json)?.tasks ?? new List<InterruptedTaskData>();

                int removed = s_Tasks.RemoveAll(t =>
                    t == null ||
                    string.IsNullOrEmpty(t.backendTaskId) ||
                    t.status == "failed" ||
                    t.status == "error" ||
                    t.status == "cancelled");

                if (removed > 0) Save();

                TJLog.Log($"[TJGeneratorsTaskRecovery] 加载了 {s_Tasks.Count} 个中断的任务");
            }
            catch (Exception ex)
            {
                TJLog.LogWarning($"[TJGeneratorsTaskRecovery] 加载中断任务失败: {ex.Message}");
                s_Tasks = new List<InterruptedTaskData>();
            }
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(InterruptedTasksFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(InterruptedTasksFilePath, JsonUtility.ToJson(new TaskListWrapper { tasks = s_Tasks }, true));
            }
            catch (Exception ex)
            {
                TJLog.LogError($"[TJGeneratorsTaskRecovery] 保存中断任务失败: {ex.Message}");
            }
        }

        [Serializable]
        private class TaskListWrapper
        {
            public List<InterruptedTaskData> tasks = new List<InterruptedTaskData>();
        }

        // ── CRUD ───────────────────────────────────────────────────────────

        public static void AddInterruptedTask(InterruptedTaskData data)
        {
            if (s_Tasks == null) s_Tasks = new List<InterruptedTaskData>();

            var idx = s_Tasks.FindIndex(t => t.backendTaskId == data.backendTaskId);
            if (idx >= 0) s_Tasks[idx] = data;
            else s_Tasks.Add(data);

            Save();
            TJLog.Log($"[TJGeneratorsTaskRecovery] 添加中断任务: {data.backendTaskId}");
        }

        /// <returns>true 表示成功移除；false 表示任务不存在（已被其他协程移除）</returns>
        public static bool RemoveInterruptedTask(string backendTaskId)
        {
            if (s_Tasks == null) return false;

            var removed = s_Tasks.RemoveAll(t => t.backendTaskId == backendTaskId);
            if (removed > 0)
            {
                Save();
                TJLog.Log($"[TJGeneratorsTaskRecovery] 移除中断任务: {backendTaskId}");
            }

            s_Recovering.Remove(backendTaskId);
            return removed > 0;
        }

        public static void UpdateTaskStatus(string backendTaskId, string status)
        {
            var task = s_Tasks?.Find(t => t.backendTaskId == backendTaskId);
            if (task == null) return;
            task.status = status;
            Save();
        }

        // ── Queries (public) ───────────────────────────────────────────────

        public static List<InterruptedTaskData> GetAllInterruptedTasks()
        {
            return s_Tasks?.ToList() ?? new List<InterruptedTaskData>();
        }

        public static bool IsRecovering(string backendTaskId) => s_Recovering.Contains(backendTaskId);

        public static void MarkAsRecovering(string backendTaskId) => s_Recovering.Add(backendTaskId);

        // ── Window recovery entry point ────────────────────────────────────

        /// <param name="host">宿主窗口，用于 null 检查</param>
        /// <param name="getAssetGuid">获取当前资产 GUID</param>
        /// <param name="setHistory">设置历史记录列表（加载后回调）</param>
        /// <param name="resumeTask">恢复单个任务的回调（查找 generator、设置状态、启动轮询）</param>
        /// <param name="onRepaint">刷新界面回调</param>
        public static void CheckAndRecoverInterruptedTasks(
            EditorWindow host,
            Func<string> getAssetGuid,
            Action<List<TJGeneratorsGenerationHistoryItem>> setHistory,
            Action<InterruptedTaskData> resumeTask,
            Action onRepaint)
        {
            EditorApplication.delayCall += () =>
            {
                if (host == null) return;

                var assetGuid = getAssetGuid();
                var history = TJGeneratorsHistoryManager.LoadHistoryForAsset(assetGuid);
                var generatingItems = history.FindAll(h => h.isGenerating);

                if (generatingItems.Count > 0)
                {
                    // history 中已有 isGenerating 占位符，说明是同 session 内 domain reload 导致的中断，直接恢复无需弹窗
                    var interruptedTasks = GetAllInterruptedTasksForAsset(assetGuid);
                    if (interruptedTasks.Count > 0)
                        RecoverAfterDomainReload(interruptedTasks, assetGuid, resumeTask, setHistory, onRepaint);
                    else
                        CleanupOrphanedPlaceholders(generatingItems, assetGuid, setHistory, onRepaint);
                }
                else
                {
                    // 无占位符说明任务在 history 写入前就中断，或来自上一个 session，需要用户确认
                    RecoverCrossSession(assetGuid, resumeTask);
                }
            };
        }

        // ── Private recovery helpers ───────────────────────────────────────

        private static void RecoverAfterDomainReload(
            List<InterruptedTaskData> tasks,
            string assetGuid,
            Action<InterruptedTaskData> resumeTask,
            Action<List<TJGeneratorsGenerationHistoryItem>> setHistory,
            Action onRepaint)
        {
            TJLog.Log($"[TJGeneratorsTaskRecovery] 检测到 {tasks.Count} 个中断的任务，重新恢复轮询...");
            foreach (var task in tasks)
            {
                // HTTP 提交阶段中断：后端可能未收到请求，backendTaskId 无效，无法轮询，直接清理
                if (task.status == "submitting")
                {
                    TJLog.Log($"[TJGeneratorsTaskRecovery] 任务 {task.localTaskId} 在提交阶段被中断，清理占位符");
                    RemoveInterruptedTask(task.backendTaskId);
                    if (!string.IsNullOrEmpty(task.localTaskId))
                        TJGeneratorsHistoryManager.RemovePlaceholder(task.localTaskId);
                    continue;
                }

                // MarkAsRecovering 由 StartGeneration 在 pipeline 启动时设置，避免对同一任务启动两条 pipeline
                if (IsRecovering(task.backendTaskId))
                {
                    TJLog.Log($"[TJGeneratorsTaskRecovery] 任务 {task.backendTaskId} 已在轮询中，跳过重复恢复。");
                    continue;
                }

                resumeTask(task);
            }

            setHistory(TJGeneratorsHistoryManager.LoadHistoryForAsset(assetGuid));
            onRepaint?.Invoke();
        }

        private static void CleanupOrphanedPlaceholders(
            List<TJGeneratorsGenerationHistoryItem> generatingItems,
            string assetGuid,
            Action<List<TJGeneratorsGenerationHistoryItem>> setHistory,
            Action onRepaint)
        {
            TJLog.Log($"[TJGeneratorsTaskRecovery] 发现 {generatingItems.Count} 个孤立的生成中占位符（无对应中断任务），清理...");
            foreach (var item in generatingItems)
            {
                TJLog.Log($"[TJGeneratorsTaskRecovery] 清理孤立占位符: taskId={item.taskId}");
                TJGeneratorsHistoryManager.RemovePlaceholder(item.taskId);
            }
            setHistory(TJGeneratorsHistoryManager.LoadHistoryForAsset(assetGuid));
            onRepaint?.Invoke();
        }

        private static void RecoverCrossSession(string assetGuid, Action<InterruptedTaskData> resumeTask)
        {
            var recoverableTasks = GetRecoverableTasksForAsset(assetGuid);
            if (recoverableTasks.Count == 0) return;

            // 不弹窗，自动恢复轮询。若需清理任务请使用历史记录面板。
            Debug.LogWarning(
                $"[TJGeneratorsTaskRecovery] 检测到 {recoverableTasks.Count} 个未完成的生成任务（可能在上次关闭编辑器时中断），自动恢复轮询。"
            );
            foreach (var task in recoverableTasks)
                resumeTask(task);
        }

        private static List<InterruptedTaskData> GetAllInterruptedTasksForAsset(string assetGuid)
        {
            return s_Tasks?
                .Where(t => t.targetAssetGuid == (assetGuid ?? ""))
                .ToList() ?? new List<InterruptedTaskData>();
        }

        private static List<InterruptedTaskData> GetRecoverableTasksForAsset(string assetGuid)
        {
            return s_Tasks?
                .Where(t => !s_Recovering.Contains(t.backendTaskId))
                .Where(t => t.targetAssetGuid == (assetGuid ?? ""))
                .ToList() ?? new List<InterruptedTaskData>();
        }
    }
}
#endif
