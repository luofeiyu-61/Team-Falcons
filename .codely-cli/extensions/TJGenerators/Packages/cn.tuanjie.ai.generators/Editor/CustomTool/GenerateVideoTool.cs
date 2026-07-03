using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Config;
using TJGenerators.Pipeline;
using TJGenerators.Utils;
using Unity.EditorCoroutines.Editor;
#endif

namespace UnityTcp.Editor.Tools
{
    /// <summary>
    /// Tracks active video generation tasks.
    /// </summary>
    public static class VideoTaskTracker
    {
#if UNITY_EDITOR
        private static readonly Dictionary<string, VideoTaskInfo> _activeTasks = new Dictionary<string, VideoTaskInfo>();
        private static int _taskIdCounter = 0;

        private const string SessionKeyIds = "TJGen_Video_Ids";
        private const string SessionKeyFmt = "TJGen_Video_{0}";

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string generatorId;
            public string prompt;
            public string imagePath;
            public string status;
            public int    progress;
            public string videoPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string lastFrameUrl;
            public string placeholderPath;
            public string backendTaskId;
        }

        public class VideoTaskInfo
        {
            public string TaskId { get; set; }
            public string GeneratorId { get; set; }
            public string Prompt { get; set; }
            public string ImagePath { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string VideoPath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public string LastFrameUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string PlaceholderPath { get; set; }
            public string BackendTaskId { get; set; }
        }

        internal static void SaveToSession(VideoTaskInfo info)
        {
            var p = new PersistedTask
            {
                taskId          = info.TaskId,
                generatorId     = info.GeneratorId,
                prompt          = info.Prompt ?? "",
                imagePath       = info.ImagePath ?? "",
                status          = info.Status,
                progress        = info.Progress,
                videoPath       = info.VideoPath ?? "",
                errorMessage    = info.ErrorMessage ?? "",
                startTimeTicks  = info.StartTime.Ticks,
                endTimeTicks    = info.EndTime?.Ticks ?? 0,
                previewUrl      = info.PreviewUrl ?? "",
                lastFrameUrl    = info.LastFrameUrl ?? "",
                placeholderPath = info.PlaceholderPath ?? "",
                backendTaskId   = info.BackendTaskId ?? ""
            };
            SessionState.SetString(string.Format(SessionKeyFmt, info.TaskId), JsonUtility.ToJson(p));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(info.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? info.TaskId : ids + "|" + info.TaskId);
        }

        private static VideoTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json)) return null;
            PersistedTask p;
            try { p = JsonUtility.FromJson<PersistedTask>(json); }
            catch { return null; }

            var info = new VideoTaskInfo
            {
                TaskId          = p.taskId,
                GeneratorId     = p.generatorId,
                Prompt          = p.prompt,
                ImagePath       = p.imagePath,
                Status          = p.status,
                Progress        = p.progress,
                VideoPath       = p.videoPath,
                ErrorMessage    = p.errorMessage,
                PreviewUrl      = p.previewUrl,
                LastFrameUrl    = p.lastFrameUrl,
                StartTime       = new DateTime(p.startTimeTicks),
                EndTime         = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
                PlaceholderPath = p.placeholderPath,
                BackendTaskId   = p.backendTaskId
            };

            // pipeline 无法在 domain reload 后恢复，一律标记为中断
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

        public static string CreateTask(string generatorId, string prompt, string imagePath, string placeholderPath, string backendTaskId = null)
        {
            string taskId = $"video_{++_taskIdCounter}_{DateTime.Now.Ticks}";

            var task = new VideoTaskInfo
            {
                TaskId          = taskId,
                GeneratorId     = generatorId,
                Prompt          = prompt ?? "",
                ImagePath       = imagePath ?? "",
                Status          = "generating",
                StartTime       = DateTime.Now,
                PlaceholderPath = placeholderPath,
                BackendTaskId   = backendTaskId
            };
            _activeTasks[taskId] = task;
            SaveToSession(task);

            return taskId;
        }

        public static void MarkTaskCompleted(string taskId, string videoPath, string previewUrl = null, string lastFrameUrl = null)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status      = "completed";
                task.Progress    = 100;
                task.VideoPath   = videoPath;
                task.PreviewUrl  = previewUrl;
                task.LastFrameUrl = lastFrameUrl;
                task.EndTime     = DateTime.Now;
                SaveToSession(task);
            }
        }

        public static void MarkTaskFailed(string taskId, string errorMessage)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status       = "failed";
                task.ErrorMessage = errorMessage;
                task.EndTime      = DateTime.Now;
                SaveToSession(task);
            }
        }

        public static VideoTaskInfo GetTask(string taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var task)) return task;
            return TryRestoreFromSession(taskId);
        }

        public static List<VideoTaskInfo> GetAllTasks()
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
            return new List<VideoTaskInfo>(_activeTasks.Values);
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
    /// CustomTool for generating video assets using TJGenerators Video pipeline.
    /// Supports text-to-video and image-to-video generation.
    /// Output is an MP4 (VideoClip) saved to Assets/TJGenerators/History/.
    /// </summary>
    public static class GenerateVideoTool
    {
        [ExecuteCustomTool.CustomTool("generate_video",
            "Generate a video asset from a text prompt or reference image using AI. " +
            "Output is an MP4 (VideoClip) saved to Assets/TJGenerators/History/. " +
            "Key parameters: generator_id (default 'huoshan_seedance'), " +
            "prompt (text description), image_path (optional reference image — omit for text-to-video), " +
            "mode (optional: 'text_to_video' or 'reference_image', auto-detected from image_path), " +
            "resolution (optional: '720p' or '1080p', default '720p'), " +
            "ratio (optional: '16:9', '9:16', or '1:1', default '16:9'), " +
            "duration (optional: 3-15 seconds, default 12), " +
            "return_last_frame (optional: bool, default true), " +
            "output_path (optional save path). " +
            "IMPORTANT: Generation takes 30-120 seconds. Wait at least 5 seconds before the first " +
            "query_video_status call, then poll every 5-10 seconds. " +
            "A placeholder_path is returned immediately — you can reference it right away.")]
        public static object GenerateVideo(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateVideoTool] Generating video with parameters: {parameters}");

                string generatorId = parameters["generator_id"]?.ToString() ?? "huoshan_seedance";
                string prompt      = parameters["prompt"]?.ToString();
                string imagePath   = parameters["image_path"]?.ToString();
                string outputPath  = parameters["output_path"]?.ToString();
                string sessionId   = parameters["session_id"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(prompt) && string.IsNullOrEmpty(imagePath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Either 'prompt' or 'image_path' must be provided" }
                    };
                }

                // Load video generator config
                var config = ConfigManager.GetGeneratorConfig(ConfigType.Video, generatorId);
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Cannot find video generator config for '{generatorId}'. Valid value: 'huoshan_seedance'." }
                    };
                }

                // Create generator and set inputs
                var generator = new DynamicGenerator(config);

                if (!string.IsNullOrEmpty(prompt))
                    generator.SetTextPrompt(prompt);

                if (!string.IsNullOrEmpty(imagePath))
                    generator.SetImagePath(imagePath);

                // Apply optional parameters
                ApplyVideoParameters(generator, generatorId, parameters);

                // 阶段 1：同步提交任务到后端
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateVideoTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateVideoTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 提交成功后再创建 placeholder（避免鉴权失败时留下无用文件）
                string placeholderPath = CreatePlaceholderVideo(outputPath);

                // 注册任务
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = VideoTaskTracker.CreateTask(generatorId, prompt, imagePath, placeholderPath, capturedBackendTaskId);

                // 创建 pipeline host
                var host = new VideoPipelineHost(
                    placeholderPath,
                    sessionId,
                    (savedPath, previewUrl, lastFrameUrl) =>
                    {
                        VideoTaskTracker.MarkTaskCompleted(taskId, savedPath, previewUrl, lastFrameUrl);
                        var t = VideoTaskTracker.GetTask(taskId);
                        GenerationNotifier.NotifyCompleted("generate_video", taskId, capturedBackendTaskId,
                            new JObject
                            {
                                ["session_id"]       = sessionId,
                                ["generator_id"]     = generatorId,
                                ["prompt"]           = prompt ?? "",
                                ["video_path"]       = savedPath ?? "",
                                ["preview_url"]      = previewUrl ?? "",
                                ["last_frame_url"]   = lastFrameUrl ?? "",
                                ["progress"]         = 100,
                                ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                            });
                    },
                    errorMsg =>
                    {
                        VideoTaskTracker.MarkTaskFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_video", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["generator_id"] = generatorId, ["prompt"] = prompt ?? "" });
                    }
                );

                // 阶段 2：异步轮询（跳过提交）
                var pipeline = new GenerationPipeline(host, ConfigType.Video);
                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateVideoTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}, placeholder: {placeholderPath}");

                string mode = string.IsNullOrEmpty(imagePath) ? "text_to_video" : "reference_image";

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Video generation started. " +
                        "STEP 1 (do now): Note the placeholder_path for later use. " +
                        "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                        "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~60s) " +
                        "containing ALL generation results (video_path, preview_url, last_frame_url, timing, etc.). " +
                        "*** POLLING IS STRICTLY FORBIDDEN — do NOT call query_video_status repeatedly. " +
                        "Only call query_video_status ONCE as a last-resort fallback if no notification arrives. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       generatorId },
                    { "mode",               mode },
                    { "prompt",             prompt ?? "" },
                    { "placeholder_path",   placeholderPath },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) },
                    { "estimated_wait_seconds", 60 },
                    { "notification_mode",  "bg_task_done" }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateVideoTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating video: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("query_video_status",
            "Query the status of a video generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, returns 'video_path' with the VideoClip asset path in the project. " +
            "Status values: 'generating', 'completed', 'failed', 'interrupted'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryVideoStatus(JObject parameters)
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

                var task = VideoTaskTracker.GetTask(taskId);

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
                    { "success",      true },
                    { "task_id",      task.TaskId },
                    { "generator_id", task.GeneratorId },
                    { "status",       task.Status },
                    { "progress",     task.Progress },
                    { "prompt",       task.Prompt },
                    { "start_time",   task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.ImagePath)) result["input_image_path"] = task.ImagePath;
                if (!string.IsNullOrEmpty(task.VideoPath)) result["video_path"]        = task.VideoPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.LastFrameUrl)) result["last_frame_url"] = task.LastFrameUrl;
                if (!string.IsNullOrEmpty(task.ErrorMessage)) result["error"]           = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"]  = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "generating")
                {
                    if (!string.IsNullOrEmpty(task.PlaceholderPath))
                        result["placeholder_path"] = task.PlaceholderPath;
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[QueryVideoStatus] Query error: {e}");
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

        [ExecuteCustomTool.CustomTool("list_video_tasks", "List all active and recent video generation tasks")]
        public static object ListVideoTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks    = VideoTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id",      task.TaskId },
                        { "generator_id", task.GeneratorId },
                        { "status",       task.Status },
                        { "progress",     task.Progress },
                        { "prompt",       task.Prompt },
                        { "start_time",   task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.ImagePath))    taskData["input_image_path"] = task.ImagePath;
                    if (!string.IsNullOrEmpty(task.VideoPath))   taskData["video_path"]        = task.VideoPath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.LastFrameUrl)) taskData["last_frame_url"]  = task.LastFrameUrl;
                    if (!string.IsNullOrEmpty(task.ErrorMessage)) taskData["error"]             = task.ErrorMessage;
                    if (task.EndTime.HasValue) taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    taskList.Add(taskData);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count",   taskList.Count },
                    { "tasks",   taskList }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[ListVideoTasks] List error: {e}");
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

#if UNITY_EDITOR
        private static void EnsureAssetDatabaseFolder(string folderPath)
        {
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = folderPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string CreatePlaceholderVideo(string outputPath)
        {
            string placeholderPath;
            if (!string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    EnsureAssetDatabaseFolder(dir);
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(outputPath, ".mp4"));
            }
            else
            {
                string folder = PathUtils.GetProjectBrowserInsertionFolderAssetPath();
                string uniqueName = "Video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{uniqueName}");
            }

            return TJGeneratorsVideoUtils.CreateBlankVideoClip(placeholderPath);
        }

        private static void ApplyVideoParameters(DynamicGenerator generator, string generatorId, JObject parameters)
        {
            // Huoshan SeeDream Video parameters
            if (generatorId == "huoshan_seedance")
            {
                if (parameters["mode"] != null)
                    generator.SetParameter("mode", parameters["mode"].ToString());

                if (parameters["resolution"] != null)
                    generator.SetParameter("resolution", parameters["resolution"].ToString());

                if (parameters["ratio"] != null)
                    generator.SetParameter("ratio", parameters["ratio"].ToString());

                if (parameters["duration"] != null)
                    generator.SetParameter("duration", parameters["duration"].ToObject<int>());

                if (parameters["return_last_frame"] != null)
                    generator.SetParameter("return_last_frame", parameters["return_last_frame"].ToObject<bool>());
            }
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// IGenerationPipelineHost implementation for headless video generation via custom tools.
    /// Handles video saving and task lifecycle callbacks.
    /// </summary>
    internal class VideoPipelineHost : IGenerationPipelineHost
    {
        private readonly string _placeholderPath;
        private readonly TJGeneratorsAssetReference _placeholderRef;
        private readonly string _sessionId;
        private readonly Action<string, string, string> _onCompleted;
        private readonly Action<string> _onFailed;

        public VideoPipelineHost(string placeholderPath, string sessionId, Action<string, string, string> onCompleted, Action<string> onFailed)
        {
            _placeholderPath = placeholderPath;
            _placeholderRef  = TJGeneratorsAssetReference.FromPath(placeholderPath);
            _sessionId       = sessionId ?? "";
            _onCompleted     = onCompleted;
            _onFailed        = onFailed;
        }

        public TJGeneratorsAssetReference GetTargetAsset() => _placeholderRef;

        public void StartEditorCoroutine(IEnumerator coroutine)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(coroutine);
        }

        public void RefreshHistory() { }
        public void ShowPreviewModel(string assetPath) { }
        public void RefreshUserInfo() { }
        public void Repaint() { }
        public void StartGeneration(ModelGeneratorBase generator) { }

        public void ShowDialog(string title, string message)
        {
            ErrorDialogUtils.ShowErrorDialog(title, message, (errorMessage) => _onFailed?.Invoke(errorMessage), "GenerateVideoTool");
        }

        public string GetTextureSavePath(ModelGeneratorBase generator) => null;
        public void OnTextureSaved(string savePath, ModelGeneratorBase generator) { }

        public string GetAudioSavePath(ModelGeneratorBase generator) => null;
        public void OnAudioSaved(string savePath, ModelGeneratorBase generator) { }

        public string GetVideoSavePath(ModelGeneratorBase generator)
        {
            // 返回 placeholder 路径，pipeline 直接覆盖文件内容，保持 GUID 不变
            return _placeholderPath;
        }

        public void OnVideoSaved(string savePath, ModelGeneratorBase generator)
        {
            TJLog.Log($"[GenerateVideoTool] Video saved: {savePath}");

            // 标记为 AI 生成资产
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));
            TJGeneratorsGenerationLabel.EnableSessionLabel(TJGeneratorsAssetReference.FromPath(savePath), _sessionId);

            // 提取 previewUrl 和 lastFrameUrl 从 generator
            string previewUrl = generator.CurrentPreviewUrl;
            string lastFrameUrl = null;

            // 尝试从响应中获取 last_frame_url
            if (generator is DynamicGenerator dynamicGen)
            {
                var lastFrameField = dynamicGen.GetParameter("last_frame_url");
                if (lastFrameField != null)
                    lastFrameUrl = lastFrameField.ToString();
            }

            _onCompleted?.Invoke(savePath, previewUrl, lastFrameUrl);
        }
    }
#endif
}