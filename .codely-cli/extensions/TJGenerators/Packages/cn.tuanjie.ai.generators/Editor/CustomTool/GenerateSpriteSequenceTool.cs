using System;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Utils;
#endif

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Tracks active sprite sequence generation tasks
    /// </summary>
    public static class SpriteSequenceTaskTracker
    {
#if UNITY_EDITOR
        private static readonly Dictionary<string, SpriteSequenceTaskInfo> _activeTasks = new Dictionary<string, SpriteSequenceTaskInfo>();
        private static int _taskIdCounter = 0;

        private const string SessionKeyIds = "TJGen_SpriteSeq_Ids";
        private const string SessionKeyFmt = "TJGen_SpriteSeq_{0}";

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string generatorId;
            public string imagePath;
            public string animationType;
            public int    fps;
            public bool   loop;
            public string status;
            public int    progress;
            public string animationClipPath;
            public string folderPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string backendTaskId;
        }

        public class SpriteSequenceTaskInfo
        {
            public string TaskId { get; set; }
            public string GeneratorId { get; set; }
            public string ImagePath { get; set; }
            public string AnimationType { get; set; }
            public int Fps { get; set; }
            public bool Loop { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string AnimationClipPath { get; set; }
            public string FolderPath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string BackendTaskId { get; set; }
        }

        internal static void SaveToSession(SpriteSequenceTaskInfo info)
        {
            var p = new PersistedTask
            {
                taskId           = info.TaskId,
                generatorId      = info.GeneratorId,
                imagePath        = info.ImagePath ?? "",
                animationType    = info.AnimationType ?? "",
                fps              = info.Fps,
                loop             = info.Loop,
                status           = info.Status,
                progress         = info.Progress,
                animationClipPath = info.AnimationClipPath ?? "",
                folderPath       = info.FolderPath ?? "",
                errorMessage     = info.ErrorMessage ?? "",
                startTimeTicks   = info.StartTime.Ticks,
                endTimeTicks     = info.EndTime?.Ticks ?? 0,
                previewUrl       = info.PreviewUrl ?? "",
                backendTaskId    = info.BackendTaskId ?? ""
            };
            SessionState.SetString(string.Format(SessionKeyFmt, info.TaskId), JsonUtility.ToJson(p));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(info.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? info.TaskId : ids + "|" + info.TaskId);
        }

        private static SpriteSequenceTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json)) return null;
            PersistedTask p;
            try { p = JsonUtility.FromJson<PersistedTask>(json); }
            catch { return null; }

            var info = new SpriteSequenceTaskInfo
            {
                TaskId           = p.taskId,
                GeneratorId      = p.generatorId,
                ImagePath        = p.imagePath,
                AnimationType    = p.animationType,
                Fps              = p.fps,
                Loop             = p.loop,
                Status           = p.status,
                Progress         = p.progress,
                AnimationClipPath = p.animationClipPath,
                FolderPath       = p.folderPath,
                ErrorMessage     = p.errorMessage,
                PreviewUrl       = p.previewUrl,
                StartTime        = new DateTime(p.startTimeTicks),
                EndTime          = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
                BackendTaskId    = p.backendTaskId
            };

            // 这些类型无 domain reload 恢复 pipeline，一律标记为 interrupted
            if (info.Status == "generating" || info.Status == "initializing")
            {
                info.Status       = "interrupted";
                info.ErrorMessage = "Generation was interrupted (domain reload). Please re-generate.";
                info.EndTime      = DateTime.Now;
                SaveToSession(info);
            }

            _activeTasks[taskId] = info;
            return info;
        }

        public static string CreateTask(string generatorId, string imagePath, string animationType, int fps, bool loop, TJGeneratorsTaskHandle handle, string sessionId = "", string backendTaskId = "")
        {
            string taskId = $"sprite_sequence_{++_taskIdCounter}_{DateTime.Now.Ticks}";

            var task = new SpriteSequenceTaskInfo
            {
                TaskId = taskId,
                GeneratorId = generatorId,
                ImagePath = imagePath ?? "",
                AnimationType = animationType ?? "idle",
                Fps = fps,
                Loop = loop,
                Status = "generating",
                Progress = 0,
                StartTime = DateTime.Now,
                BackendTaskId = backendTaskId
            };

            _activeTasks[taskId] = task;
            SaveToSession(task);

            handle.OnProgress += (h) =>
            {
                // Only update status for non-terminal states.
                // "completed" must only be set by OnCompleted (which also sets AnimationClipPath/FolderPath/EndTime).
                // If we allowed OnProgress to set "completed", query_sprite_sequence_status would return
                // status:"completed" before AnimationClipPath and FolderPath are populated.
                if (h.Status != "completed" && h.Status != "failed")
                    task.Status = h.Status;
                task.Progress = h.Progress;
                if (!string.IsNullOrEmpty(h.PreviewUrl))
                    task.PreviewUrl = h.PreviewUrl;
                SaveToSession(task);
            };

            handle.OnCompleted += (h) =>
            {
                task.Status = "completed";
                task.Progress = 100;
                string clipPath = h.ModelPath;
                task.AnimationClipPath = clipPath;
                task.FolderPath = string.IsNullOrEmpty(clipPath) ? "" : Path.GetDirectoryName(clipPath)?.Replace('\\', '/');
                task.PreviewUrl = h.PreviewUrl;
                task.EndTime = DateTime.Now;
                SaveToSession(task);
                if (!string.IsNullOrEmpty(clipPath))
                    TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(clipPath));
                string folderPath = task.FolderPath;
                if (!string.IsNullOrEmpty(folderPath))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath }))
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(assetPath));
                        TJGeneratorsGenerationLabel.EnableSessionLabel(
                            TJGeneratorsAssetReference.FromPath(assetPath), sessionId);
                    }
                }
                if (!string.IsNullOrEmpty(clipPath))
                    TJGeneratorsGenerationLabel.EnableSessionLabel(
                        TJGeneratorsAssetReference.FromPath(clipPath), sessionId);
                int frameCount = string.IsNullOrEmpty(task.FolderPath)
                    ? 0
                    : AssetDatabase.FindAssets("t:Sprite", new[] { task.FolderPath }).Length;
                GenerationNotifier.NotifyCompleted("generate_sprite_sequence", taskId, backendTaskId,
                    new JObject
                    {
                        ["session_id"]          = sessionId,
                        ["generator_id"]        = task.GeneratorId ?? "",
                        ["folder_path"]         = task.FolderPath ?? "",
                        ["animation_clip_path"] = task.AnimationClipPath ?? "",
                        ["frame_count"]         = frameCount,
                        ["preview_url"]         = h.PreviewUrl ?? "",
                        ["progress"]            = 100,
                        ["start_time"]          = task.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["end_time"]            = task.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        ["duration_seconds"]    = task.EndTime.HasValue ? (int)(task.EndTime.Value - task.StartTime).TotalSeconds : 0
                    });
            };

            handle.OnFailed += (h) =>
            {
                task.Status = "failed";
                task.ErrorMessage = h.ErrorMessage;
                task.EndTime = DateTime.Now;
                SaveToSession(task);
                GenerationNotifier.NotifyFailed("generate_sprite_sequence", taskId, backendTaskId, h.ErrorMessage,
                    new JObject { ["session_id"] = sessionId, ["generator_id"] = task.GeneratorId ?? "" });
            };

            return taskId;
        }

        public static SpriteSequenceTaskInfo GetTask(string taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var task)) return task;
            return TryRestoreFromSession(taskId);
        }

        public static List<SpriteSequenceTaskInfo> GetAllTasks()
        {
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!string.IsNullOrEmpty(ids))
            {
                foreach (var id in ids.Split('|'))
                {
                    if (!string.IsNullOrEmpty(id) && !_activeTasks.ContainsKey(id))
                        TryRestoreFromSession(id);
                }
            }
            return new List<SpriteSequenceTaskInfo>(_activeTasks.Values);
        }

        public static void RemoveTask(string taskId)
        {
            _activeTasks.Remove(taskId);
            SessionState.EraseString(string.Format(SessionKeyFmt, taskId));
            string ids = SessionState.GetString(SessionKeyIds, "");
            var list = new List<string>(ids.Split('|'));
            list.Remove(taskId);
            SessionState.SetString(SessionKeyIds, string.Join("|", list));
        }

        public static void CleanupCompletedTasks()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _activeTasks)
            {
                if ((kvp.Value.Status == "completed" || kvp.Value.Status == "failed") &&
                    kvp.Value.EndTime.HasValue &&
                    (DateTime.Now - kvp.Value.EndTime.Value).TotalMinutes > 60)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                _activeTasks.Remove(id);
        }
#endif
    }

    /// <summary>
    /// CustomTool for generating 2D sprite sequence animations (frame-by-frame) using TJGenerators SpriteSequence pipeline.
    /// Requires a character reference image as input; outputs multiple Sprite frames + an AnimationClip asset
    /// saved to Assets/TJGenerators/History/Sequence_xxx/.
    /// </summary>
    public static class GenerateSpriteSequenceTool
    {
        [ExecuteCustomTool.CustomTool("generate_sprite_sequence",
            "Generate a 2D sprite sequence animation (multiple Sprite frames + AnimationClip) from a character reference image using AI. " +
            "Output is saved to Assets/TJGenerators/History/Sequence_xxx/ as individual Sprite PNGs and a .anim AnimationClip asset. " +
            "Parameters: " +
            "image_path (REQUIRED — absolute path or Assets-relative path of the character reference image), " +
            "generator_id (optional, default 'sprite_sequence_v1'), " +
            "animation_type (optional animation action; valid values: 'idle' (idle), 'frontRun' (run forward), 'backRun' (run backward); default 'idle'), " +
            "fps (optional frames per second for the AnimationClip, integer 1-60, default 12), " +
            "loop (optional bool, whether the AnimationClip loops, default true). " +
            "NOTE: image_path is mandatory — the API only accepts image input for sprite sequence generation. " +
            "IMPORTANT: Generation takes 1-3 minutes. Wait at least 5 seconds before the first query_sprite_sequence_status call, then poll every 10-15 seconds.")]
        public static object GenerateSpriteSequence(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateSpriteSequenceTool] Generating sprite sequence with parameters: {parameters}");

                string generatorId = parameters["generator_id"]?.ToString() ?? "sprite_sequence_v1";
                string imagePath = parameters["image_path"]?.ToString();
                string animationType = parameters["animation_type"]?.ToString() ?? "idle";
                string sessionId = parameters["session_id"]?.ToString() ?? "";
                int fps = 12;
                bool loop = true;

                if (parameters["fps"] != null)
                {
                    if (!int.TryParse(parameters["fps"].ToString(), out fps))
                    {
                        TJLog.LogWarning($"[GenerateSpriteSequenceTool] Invalid fps value '{parameters["fps"]}', using default: 12");
                        fps = 12;
                    }
                    fps = Mathf.Clamp(fps, 1, 60);
                }

                if (parameters["loop"] != null)
                {
                    if (!bool.TryParse(parameters["loop"].ToString(), out loop))
                    {
                        TJLog.LogWarning($"[GenerateSpriteSequenceTool] Invalid loop value '{parameters["loop"]}', using default: true");
                        loop = true;
                    }
                }

                if (string.IsNullOrEmpty(imagePath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'image_path' is required for sprite sequence generation. Provide the path to a character reference image." }
                    };
                }

                // Validate animation_type
                var validAnimTypes = new HashSet<string> { "idle", "frontRun", "backRun" };
                if (!validAnimTypes.Contains(animationType))
                {
                    TJLog.LogWarning($"[GenerateSpriteSequenceTool] Unknown animation_type '{animationType}', falling back to 'idle'. Valid values: idle, frontRun, backRun.");
                    animationType = "idle";
                }

                // Load sprite sequence generator config
                var config = ConfigManager.GetGeneratorConfig(ConfigType.SpriteSequence, generatorId);
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Cannot find sprite sequence generator config for '{generatorId}'. Valid value: 'sprite_sequence_v1'." }
                    };
                }

                // Create generator and set inputs
                var generator = new DynamicGenerator(config);
                generator.SetImagePath(imagePath);
                generator.SetParameter("animation_type", animationType);
                generator.SetParameter("fps", fps);
                generator.SetParameter("loop", loop);

                // 阶段1：同步提交任务到后端，立即获取 backendTaskId 或失败原因
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateSpriteSequenceTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateSpriteSequenceTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 阶段2：异步轮询（跳过提交）
                var context = new TJGeneratorsGenerationContext
                {
                    TargetAsset = null,
                    AutoCreateTargetPrefab = false
                };
                var handle = TJGeneratorsGenerationService.GenerateFromSubmittedTask(
                    generator, context, submitResult.BackendTaskId);

                // Create tracked task; subscribes to handle events internally for progress updates
                string taskId = SpriteSequenceTaskTracker.CreateTask(generatorId, imagePath, animationType, fps, loop, handle, sessionId, submitResult.BackendTaskId);

                TJLog.Log($"[GenerateSpriteSequenceTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}");

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Sprite sequence generation started. " +
                        "STEP 1 (do now): Note the task_id for later retrieval. " +
                        "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                        "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~90s) " +
                        "containing ALL generation results (folder_path, animation_clip_path, frame_count, timing, etc.). " +
                        "*** POLLING IS STRICTLY FORBIDDEN — do NOT call query_sprite_sequence_status repeatedly. " +
                        "Only call query_sprite_sequence_status ONCE as a one-time fallback if no notification arrives. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       generatorId },
                    { "image_path",         imagePath },
                    { "animation_type",     animationType },
                    { "fps",                fps },
                    { "loop",               loop },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) },
                    { "estimated_wait_seconds", 90 },
                    { "notification_mode",  "bg_task_done" }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateSpriteSequenceTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating sprite sequence: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("query_sprite_sequence_status",
            "Query the status of a sprite sequence generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, returns 'animation_clip_path' (.anim) and 'folder_path' containing all frame Sprite PNGs. " +
            "Status values: 'generating', 'completed', 'failed'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QuerySpriteSequenceStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters["task_id"]?.ToString();

                if (string.IsNullOrEmpty(taskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'task_id' parameter is required" }
                    };
                }

                var task = SpriteSequenceTaskTracker.GetTask(taskId);

                if (task == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Task '{taskId}' not found. It may have been completed and cleaned up." }
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "task_id", task.TaskId },
                    { "generator_id", task.GeneratorId },
                    { "status", task.Status },
                    { "progress", task.Progress },
                    { "image_path", task.ImagePath },
                    { "animation_type", task.AnimationType },
                    { "fps", task.Fps },
                    { "loop", task.Loop },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.AnimationClipPath)) result["animation_clip_path"] = task.AnimationClipPath;
                if (!string.IsNullOrEmpty(task.FolderPath)) result["folder_path"] = task.FolderPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.ErrorMessage)) result["error"] = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }


                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateSpriteSequenceTool] Query error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying task status: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }

        [ExecuteCustomTool.CustomTool("list_sprite_sequence_tasks", "List all active and recent sprite sequence generation tasks")]
        public static object ListSpriteSequenceTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks = SpriteSequenceTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id", task.TaskId },
                        { "generator_id", task.GeneratorId },
                        { "status", task.Status },
                        { "progress", task.Progress },
                        { "image_path", task.ImagePath },
                        { "animation_type", task.AnimationType },
                        { "fps", task.Fps },
                        { "loop", task.Loop },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.AnimationClipPath)) taskData["animation_clip_path"] = task.AnimationClipPath;
                    if (!string.IsNullOrEmpty(task.FolderPath)) taskData["folder_path"] = task.FolderPath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.ErrorMessage)) taskData["error"] = task.ErrorMessage;
                    if (task.EndTime.HasValue) taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    taskList.Add(taskData);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count", taskList.Count },
                    { "tasks", taskList }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateSpriteSequenceTool] List error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error listing tasks: {e.Message}" }
                };
            }
#else
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", "This tool only works in Unity Editor." }
            };
#endif
        }
    }
}
