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
    // ========== Tripo 枚举 ==========
    
    public enum ModelVersion
    {
        v2_5_20250123 = 0,
        v2_0_20240919 = 1,
        v1_4_20240625 = 2,
        v1_3_20240522 = 3,
    }

    public enum TextureQuality
    {
        Standard = 0,
        Detailed = 1
    }

    public enum ModelStyle
    {
        Original = 0,
        Cartoon = 1,
        Clay = 2,
        Steampunk = 3,
        Venom = 4,
        Barbie = 5,
        Christmas = 6
    }

    public enum Orientation
    {
        Default = 0,
        AlignImage = 1
    }

    public enum TextureAlignment
    {
        OriginalImage = 0,
        Geometry = 1
    }

    // ========== 历史记录 ==========
    
    /// <summary>
    /// 历史生成记录项
    /// </summary>
    [Serializable]
    public class TJGeneratorsGenerationHistoryItem
    {
        public string modelPath;
        public string prompt;
        /// <summary>
        /// 仅用于历史列表/缩略图标题展示的玩家输入，不含 instructions 等系统文案。
        /// </summary>
        public string userPrompt;
        public string imagePath;
        public long timestamp;
        public string modelVersion;
        public bool isTextToModel;
        public bool isGenerating;
        public string taskId;
        public string assetGuid;
        /// <summary>
        /// 关联资产 GUID（材质生成时通常为贴图 PNG，与 assetGuid 的 Material 成对绑定）。
        /// </summary>
        public string linkedAssetGuid;
        public int progress;
        
        /// <summary>
        /// 预览图URL（来自API返回的rendered_image等字段）
        /// </summary>
        public string previewImageUrl;

        /// <summary>
        /// 多图生成时所有图片的 URL 数组（如 sprite 返回的 image_urls）
        /// </summary>
        public string[] imageUrls;

        /// <summary>
        /// 源OBJ文件的URL（用于混元智能减面）
        /// </summary>
        public string sourceObjUrl;

        /// <summary>
        /// Prompt模板ID（用于识别特定类型的生成任务，如地形高度图）
        /// </summary>
        public string promptTemplateId;

        public string GetUserFacingPrompt()
        {
            if (!string.IsNullOrWhiteSpace(userPrompt))
                return userPrompt.Trim();
            return TJGenerators.Utils.TJGeneratorsPromptDisplay.ExtractUserFacingPrompt(prompt);
        }

        public string GetDisplayName()
        {
            if (isGenerating)
            {
                if (progress >= 100)
                    return TJGeneratorsL10n.L("转换中...");
                return progress > 0 ? TJGeneratorsL10n.L("生成中 {0}%", progress) : TJGeneratorsL10n.L("生成中...");
            }
            string facing = GetUserFacingPrompt();
            if (!string.IsNullOrEmpty(facing))
                return TJGenerators.Utils.TJGeneratorsPromptDisplay.FormatHistoryTileLabel(facing);
            if (!string.IsNullOrEmpty(prompt))
                return TJGenerators.Utils.TJGeneratorsPromptDisplay.FormatHistoryTileLabel(
                    TJGenerators.Utils.TJGeneratorsPromptDisplay.ExtractUserFacingPrompt(prompt)
                );
            return Path.GetFileNameWithoutExtension(modelPath);
        }

        public string GetTimeString()
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            return dt.ToString("MM/dd HH:mm");
        }
    }

    /// <summary>
    /// 历史记录持久化管理
    /// </summary>
    public static class TJGeneratorsHistoryManager
    {
        private const string HistoryPrefsKey = "TJGeneratorsGenerationHistory";
        private const string LegacyHistoryPrefsKey = "ApexGenGenerationHistory"; // 旧键名，用于迁移
        private const int MaxHistoryCount = 100;
        private const int MaxHistoryPerAsset = 50;
        private static bool hasMigrated = false;

        [Serializable]
        private class HistoryWrapper
        {
            public List<TJGeneratorsGenerationHistoryItem> items = new List<TJGeneratorsGenerationHistoryItem>();
        }

        /// <summary>
        /// 从旧键名迁移历史记录到新键名
        /// </summary>
        private static void MigrateFromLegacyKey()
        {
            if (hasMigrated) return;

            var newJson = EditorPrefs.GetString(HistoryPrefsKey, "");
            var legacyJson = EditorPrefs.GetString(LegacyHistoryPrefsKey, "");

            // 如果新键名没有数据，但旧键名有数据，则迁移
            if (string.IsNullOrEmpty(newJson) && !string.IsNullOrEmpty(legacyJson))
            {
                TJLog.Log("[TJGeneratorsHistoryManager] 正在从旧版本迁移历史记录...");
                EditorPrefs.SetString(HistoryPrefsKey, legacyJson);
                EditorPrefs.DeleteKey(LegacyHistoryPrefsKey);
                TJLog.Log("[TJGeneratorsHistoryManager] 历史记录迁移完成");
            }
            // 如果两边都有数据，删除旧的
            else if (!string.IsNullOrEmpty(legacyJson))
            {
                EditorPrefs.DeleteKey(LegacyHistoryPrefsKey);
            }

            hasMigrated = true;
        }

        private static List<TJGeneratorsGenerationHistoryItem> LoadAllHistory()
        {
            MigrateFromLegacyKey(); // 确保迁移

            var json = EditorPrefs.GetString(HistoryPrefsKey, "");
            if (string.IsNullOrEmpty(json))
            {
                return new List<TJGeneratorsGenerationHistoryItem>();
            }

            try
            {
                var wrapper = JsonUtility.FromJson<HistoryWrapper>(json);
                wrapper.items.RemoveAll(item => !item.isGenerating && !string.IsNullOrEmpty(item.modelPath) && !DoesModelFileExist(item.modelPath));
                return wrapper.items;
            }
            catch
            {
                return new List<TJGeneratorsGenerationHistoryItem>();
            }
        }

        private static bool DoesModelFileExist(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
                return false;

            if (modelPath.StartsWith("Assets/") || modelPath.StartsWith("Assets\\"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(modelPath);
                return asset != null;
            }

            return File.Exists(modelPath);
        }

        public static List<TJGeneratorsGenerationHistoryItem> LoadHistory()
        {
            return LoadAllHistory();
        }

        public static List<TJGeneratorsGenerationHistoryItem> LoadHistoryForAsset(string assetGuid)
        {
            var allHistory = LoadAllHistory();
            var targetGuid = assetGuid ?? "";
            var result = allHistory.Where(h =>
                (h.assetGuid ?? "") == targetGuid
                || (h.linkedAssetGuid ?? "") == targetGuid).ToList();

            if (!string.IsNullOrEmpty(targetGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(targetGuid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    string normalized = assetPath.Replace('\\', '/');
                    foreach (var h in allHistory)
                    {
                        if (string.IsNullOrEmpty(h.modelPath))
                            continue;
                        if (!string.Equals(h.modelPath.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (result.Any(r => r.taskId == h.taskId))
                            continue;
                        result.Add(h);
                    }
                }
            }

            result = result.OrderByDescending(h => h.timestamp).ToList();
            if (result.Count > 0)
                TJLog.Log($"[TJGeneratorsHistoryManager] LoadHistoryForAsset: assetGuid={targetGuid}, total={allHistory.Count}, filtered={result.Count}");
            return result;
        }

        /// <summary>
        /// 加载 Material 资产的生成历史（assetGuid 绑定 .mat）。
        /// </summary>
        public static List<TJGeneratorsGenerationHistoryItem> LoadHistoryForMaterialAsset(string materialAssetPath)
        {
            string materialGuid = string.IsNullOrEmpty(materialAssetPath)
                ? ""
                : AssetDatabase.AssetPathToGUID(materialAssetPath);
            return LoadHistoryForAsset(materialGuid ?? "");
        }

        /// <summary>
        /// 材质生成完成时：Material 绑定 assetGuid，贴图 PNG 绑定 linkedAssetGuid。
        /// </summary>
        public static void TryLinkMaterialTextureHistoryItem(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.modelPath))
                return;

            string texturePath = item.modelPath.Replace('\\', '/');
            string textureGuid = AssetDatabase.AssetPathToGUID(texturePath);
            if (string.IsNullOrEmpty(textureGuid))
                return;

            string matPath = Path.ChangeExtension(texturePath, ".mat");
            string matGuid = AssetDatabase.AssetPathToGUID(matPath);

            item.linkedAssetGuid = textureGuid;

            if (!string.IsNullOrEmpty(matGuid))
            {
                if (string.IsNullOrEmpty(item.assetGuid)
                    || string.Equals(item.assetGuid, textureGuid, StringComparison.Ordinal))
                {
                    item.assetGuid = matGuid;
                }
            }
        }

        public static void SaveHistory(List<TJGeneratorsGenerationHistoryItem> history)
        {
            while (history.Count > MaxHistoryCount)
            {
                history.RemoveAt(history.Count - 1);
            }

            var wrapper = new HistoryWrapper { items = history };
            var json = JsonUtility.ToJson(wrapper);
            EditorPrefs.SetString(HistoryPrefsKey, json);
        }

        public static void AddToHistory(TJGeneratorsGenerationHistoryItem item)
        {
            var history = LoadAllHistory();
            if (!string.IsNullOrEmpty(item.modelPath))
            {
                history.RemoveAll(h => h.modelPath == item.modelPath);
            }
            history.Insert(0, item);
            SaveHistory(history);
        }

        public static string AddGeneratingPlaceholder(
            string prompt,
            string imagePath,
            string modelVersion,
            bool isTextToModel,
            string assetGuid,
            string userDisplayPrompt = null
        )
        {
            var history = LoadHistory();
            var targetGuid = assetGuid ?? "";

            TJLog.Log($"[TJGeneratorsHistoryManager] AddGeneratingPlaceholder: assetGuid={targetGuid}, modelVersion={modelVersion}, isTextToModel={isTextToModel}");

            var existingPlaceholder = history.Find(h =>
                h.isGenerating &&
                (h.assetGuid ?? "") == targetGuid &&
                h.isTextToModel == isTextToModel &&
                (isTextToModel ? h.prompt == prompt : h.imagePath == imagePath));

            if (existingPlaceholder != null)
            {
                TJLog.Log($"[TJGeneratorsHistoryManager] 检测到重复的生成请求，复用现有占位符: {existingPlaceholder.taskId}");
                return existingPlaceholder.taskId;
            }

            var taskId = Guid.NewGuid().ToString();
            string trimmedUserDisplay = string.IsNullOrWhiteSpace(userDisplayPrompt)
                ? null
                : userDisplayPrompt.Trim();

            var item = new TJGeneratorsGenerationHistoryItem
            {
                modelPath = "",
                prompt = prompt,
                userPrompt = trimmedUserDisplay,
                imagePath = imagePath,
                timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                modelVersion = modelVersion,
                isTextToModel = isTextToModel,
                isGenerating = true,
                taskId = taskId,
                assetGuid = targetGuid
            };

            history.Insert(0, item);
            SaveHistory(history);

            TJLog.Log($"[TJGeneratorsHistoryManager] 创建新占位符: taskId={taskId}, assetGuid={targetGuid}");

            return taskId;
        }

        public static void CompletePlaceholder(string taskId, string modelPath, string previewImageUrl = null, string[] imageUrls = null, string promptTemplateId = null, string sourceObjUrl = null)
        {
            TJLog.Log($"[TJGeneratorsHistoryManager] CompletePlaceholder called: taskId={taskId}, modelPath={modelPath}, previewImageUrl={previewImageUrl ?? "null"}, sourceObjUrl={sourceObjUrl ?? "null"}, imageUrls count={imageUrls?.Length ?? 0}, promptTemplateId={promptTemplateId ?? "null"}");
            var history = LoadHistory();
            TJLog.Log($"[TJGeneratorsHistoryManager] Loaded {history.Count} history items");
            var item = history.Find(h => h.taskId == taskId);
            if (item != null)
            {
                TJLog.Log($"[TJGeneratorsHistoryManager] Found item: taskId={item.taskId}, isGenerating={item.isGenerating}, assetGuid={item.assetGuid}");
                if (!item.isGenerating)
                {
                    TJLog.Log($"[TJGeneratorsHistoryManager] 任务 {taskId} 已完成，跳过重复完成");
                    return;
                }

                item.modelPath = modelPath;
                item.isGenerating = false;
                item.timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                item.previewImageUrl = previewImageUrl;
                item.sourceObjUrl = sourceObjUrl;
                item.imageUrls = imageUrls;
                item.promptTemplateId = promptTemplateId;
                TryLinkMaterialTextureHistoryItem(item);
                SaveHistory(history);
                TJLog.Log($"[TJGeneratorsHistoryManager] 任务 {taskId} 已完成，modelPath={modelPath}, previewImageUrl={previewImageUrl ?? "null"}, sourceObjUrl={sourceObjUrl ?? "null"}, promptTemplateId={promptTemplateId ?? "null"}");
            }
            else
            {
                TJLog.LogWarning($"[TJGeneratorsHistoryManager] 未找到taskId={taskId}的占位符");
            }
        }

        /// <summary>
        /// 多图完成：将占位符更新为第一张图，并插入其余张为独立历史条（一图一格）。
        /// </summary>
        public static void CompletePlaceholderMultiImage(string taskId, List<string> modelPaths, string[] imageUrls, string promptTemplateId = null)
        {
            if (modelPaths == null || modelPaths.Count == 0 || imageUrls == null || imageUrls.Length != modelPaths.Count)
            {
                TJLog.LogWarning("[TJGeneratorsHistoryManager] CompletePlaceholderMultiImage 参数无效");
                return;
            }

            var history = LoadHistory();
            int idx = history.FindIndex(h => h.taskId == taskId);
            if (idx < 0)
            {
                TJLog.LogWarning($"[TJGeneratorsHistoryManager] 未找到 taskId={taskId} 的占位符");
                return;
            }

            var placeholder = history[idx];
            if (!placeholder.isGenerating)
            {
                TJLog.Log("[TJGeneratorsHistoryManager] 任务已完成，跳过");
                return;
            }

            placeholder.modelPath = modelPaths[0];
            // Priority 2: result URL；Priority 3: 本地文件 URI（文件不存在时为 null）
            if (imageUrls != null && imageUrls.Length > 0 && !string.IsNullOrEmpty(imageUrls[0]))
            {
                placeholder.previewImageUrl = imageUrls[0];
            }
            else if (!string.IsNullOrEmpty(modelPaths[0]))
            {
                string fullPath0 = Path.GetFullPath(modelPaths[0]);
                placeholder.previewImageUrl = File.Exists(fullPath0)
                    ? "file://" + fullPath0.Replace('\\', '/')
                    : null;
            }
            placeholder.imageUrls = null;
            placeholder.isGenerating = false;
            placeholder.timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            placeholder.promptTemplateId = promptTemplateId;

            for (int i = 1; i < modelPaths.Count; i++)
            {
                string itemPreviewUrl;
                if (imageUrls != null && i < imageUrls.Length && !string.IsNullOrEmpty(imageUrls[i]))
                {
                    itemPreviewUrl = imageUrls[i];
                }
                else if (!string.IsNullOrEmpty(modelPaths[i]))
                {
                    string fullPath = Path.GetFullPath(modelPaths[i]);
                    itemPreviewUrl = File.Exists(fullPath)
                        ? "file://" + fullPath.Replace('\\', '/')
                        : null;
                }
                else
                {
                    itemPreviewUrl = null;
                }

                history.Insert(idx + i, new TJGeneratorsGenerationHistoryItem
                {
                    modelPath = modelPaths[i],
                    previewImageUrl = itemPreviewUrl,
                    prompt = placeholder.prompt,
                    userPrompt = placeholder.userPrompt,
                    imagePath = placeholder.imagePath,
                    timestamp = placeholder.timestamp,
                    modelVersion = placeholder.modelVersion,
                    isTextToModel = placeholder.isTextToModel,
                    isGenerating = false,
                    taskId = "",
                    assetGuid = placeholder.assetGuid,
                    promptTemplateId = promptTemplateId
                });
            }

            SaveHistory(history);
            TJLog.Log($"[TJGeneratorsHistoryManager] 多图完成: {modelPaths.Count} 条历史");
        }

        public static void RemovePlaceholder(string taskId)
        {
            var history = LoadHistory();
            history.RemoveAll(h => h.taskId == taskId);
            SaveHistory(history);
        }

        public static TJGeneratorsGenerationHistoryItem GetHistoryItem(string localTaskId)
        {
            if (string.IsNullOrEmpty(localTaskId)) return null;
            return LoadHistory().Find(h => h.taskId == localTaskId);
        }

        public static List<TJGeneratorsGenerationHistoryItem> GetHistory(string assetGuid)
        {
            var history = LoadHistory();
            return string.IsNullOrEmpty(assetGuid) ? history : history.FindAll(h => h.assetGuid == assetGuid);
        }

        /// <summary>
        /// 获取所有可减面的OBJ文件（有sourceObjUrl的OBJ文件）
        /// </summary>
        public static List<TJGeneratorsGenerationHistoryItem> GetConvertibleObjFiles()
        {
            var history = LoadAllHistory();
            return history.Where(h =>
                !h.isGenerating &&
                !string.IsNullOrEmpty(h.modelPath) &&
                !string.IsNullOrEmpty(h.sourceObjUrl) &&
                h.modelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) &&
                DoesModelFileExist(h.modelPath)
            ).ToList();
        }

        public static void UpdatePlaceholderProgress(string taskId, int progress)
        {
            var history = LoadHistory();
            var item = history.Find(h => h.taskId == taskId && h.isGenerating);
            if (item != null)
            {
                item.progress = progress;
                SaveHistory(history);
            }
        }

        public static void RemoveFromHistory(string modelPath)
        {
            var history = LoadHistory();
            history.RemoveAll(h => h.modelPath == modelPath);
            SaveHistory(history);
        }

        public static void ClearHistoryForAsset(string assetGuid)
        {
            var history = LoadAllHistory();
            var targetGuid = assetGuid ?? "";
            history.RemoveAll(h => (h.assetGuid ?? "") == targetGuid);
            SaveHistory(history);
        }

        /// <summary>
        /// 删除 EditorPrefs 中的全部生成历史（含旧版键名），无法撤销。
        /// </summary>
        public static void ClearAllHistory()
        {
            EditorPrefs.DeleteKey(HistoryPrefsKey);
            EditorPrefs.DeleteKey(LegacyHistoryPrefsKey);
            hasMigrated = false;
            TJLog.Log("[TJGeneratorsHistoryManager] 已清空所有生成历史记录");
        }

        /// <summary>
        /// 目标资源从 .wav 占位变为 .mp4 / .mp3 等时 GUID 会变，将已有关联的历史条目的 assetGuid 迁移到新 GUID，避免面板历史丢失。
        /// </summary>
        public static void RewriteAssetGuid(string oldGuid, string newGuid)
        {
            if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid)
                || string.Equals(oldGuid, newGuid, StringComparison.Ordinal))
                return;

            var history = LoadAllHistory();
            bool changed = false;
            foreach (var h in history)
            {
                if (string.Equals(h.assetGuid ?? "", oldGuid, StringComparison.Ordinal))
                {
                    h.assetGuid = newGuid;
                    changed = true;
                }
            }

            if (changed)
            {
                SaveHistory(history);
                TJLog.Log($"[TJGeneratorsHistoryManager] 已将历史记录 assetGuid 自 {oldGuid} 迁移至 {newGuid}");
            }
        }
    }

    // ========== 用户信息 API 响应 ==========

    [Serializable]
    public class UserInfoResponse
    {
        public string avatar;
        public UserCredits credits;
        public string email;
        public string genesisUserId;
        public string id;
        public bool isAdmin;
        public bool isCP;
        public string loginType;
        public UserOrg org;
        public string phone;
        public string role;
        public string type;
        public string username;
    }

    [Serializable]
    public class UserCredits
    {
        public int currentCredits;
        public string email;
        public string lastCreditDate;
        public int todayEarned;
        public int todaySpent;
        public int totalEarned;
        public int totalSpent;
        public string userId;
        public string username;
    }

    [Serializable]
    public class UserOrg
    {
        public string orgDisplayName;
        public string orgId;
        public string orgName;
    }
}
#endif
