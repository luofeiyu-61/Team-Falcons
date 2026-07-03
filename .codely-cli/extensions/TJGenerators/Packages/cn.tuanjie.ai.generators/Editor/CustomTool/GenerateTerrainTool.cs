using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
    /// Tracks active terrain heightmap generation tasks.
    /// </summary>
    public static class TerrainTaskTracker
    {
#if UNITY_EDITOR
        private static readonly Dictionary<string, TerrainTaskInfo> _activeTasks = new Dictionary<string, TerrainTaskInfo>();
        private static int _taskIdCounter = 0;

        private const string SessionKeyIds = "TJGen_Terrain_Ids";
        private const string SessionKeyFmt = "TJGen_Terrain_{0}";

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string prompt;
            public string imagePath;
            public string status;
            public int    progress;
            public string heightmapPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string placeholderPath;
            public string terrainDataPath;
            public string terrainGoName;
            public string backendTaskId;
        }

        public class TerrainTaskInfo
        {
            public string TaskId { get; set; }
            public string Prompt { get; set; }
            public string ImagePath { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string HeightmapPath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string PlaceholderPath { get; set; }
            public string TerrainDataPath { get; set; }
            public string TerrainGoName { get; set; }
            public string BackendTaskId { get; set; }
        }

        internal static void SaveToSession(TerrainTaskInfo info)
        {
            var p = new PersistedTask
            {
                taskId          = info.TaskId,
                prompt          = info.Prompt ?? "",
                imagePath       = info.ImagePath ?? "",
                status          = info.Status,
                progress        = info.Progress,
                heightmapPath   = info.HeightmapPath ?? "",
                errorMessage    = info.ErrorMessage ?? "",
                startTimeTicks  = info.StartTime.Ticks,
                endTimeTicks    = info.EndTime?.Ticks ?? 0,
                previewUrl      = info.PreviewUrl ?? "",
                placeholderPath = info.PlaceholderPath ?? "",
                terrainDataPath = info.TerrainDataPath ?? "",
                terrainGoName   = info.TerrainGoName ?? "",
                backendTaskId   = info.BackendTaskId ?? ""
            };
            SessionState.SetString(string.Format(SessionKeyFmt, info.TaskId), JsonUtility.ToJson(p));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(info.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? info.TaskId : ids + "|" + info.TaskId);
        }

        private static TerrainTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json)) return null;
            PersistedTask p;
            try { p = JsonUtility.FromJson<PersistedTask>(json); }
            catch { return null; }

            var info = new TerrainTaskInfo
            {
                TaskId          = p.taskId,
                Prompt          = p.prompt,
                ImagePath       = p.imagePath,
                Status          = p.status,
                Progress        = p.progress,
                HeightmapPath   = p.heightmapPath,
                ErrorMessage    = p.errorMessage,
                PreviewUrl      = p.previewUrl,
                StartTime       = new DateTime(p.startTimeTicks),
                EndTime         = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
                PlaceholderPath = p.placeholderPath,
                TerrainDataPath = p.terrainDataPath,
                TerrainGoName   = p.terrainGoName,
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

        public static string CreateTask(string prompt, string imagePath = null, string placeholderPath = null, string backendTaskId = null)
        {
            string taskId = $"terrain_{++_taskIdCounter}_{DateTime.Now.Ticks}";

            var task = new TerrainTaskInfo
            {
                TaskId          = taskId,
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

        public static void MarkTaskCompleted(string taskId, string heightmapPath, string previewUrl = null)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status       = "completed";
                task.Progress     = 100;
                task.HeightmapPath = heightmapPath;
                task.PreviewUrl   = previewUrl;
                task.EndTime      = DateTime.Now;
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

        public static void MarkTaskApplied(string taskId, string terrainDataPath, string terrainGoName)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status         = "applied";
                task.TerrainDataPath = terrainDataPath;
                task.TerrainGoName  = terrainGoName;
                task.EndTime        = task.EndTime ?? DateTime.Now;
                SaveToSession(task);
            }
        }

        public static TerrainTaskInfo GetTask(string taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var task)) return task;
            return TryRestoreFromSession(taskId);
        }

        public static List<TerrainTaskInfo> GetAllTasks()
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
            return new List<TerrainTaskInfo>(_activeTasks.Values);
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
#endif
    }

    /// <summary>
    /// 线程安全的主线程任务分发器。
    /// Task.Run 中不能直接调用 EditorApplication.delayCall += （委托不是线程安全的），
    /// 改用并发队列 + EditorApplication.update 在主线程消费。
    /// </summary>
    internal static class EditorMainThreadDispatcher
    {
#if UNITY_EDITOR
        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> _queue
            = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        private static bool _registered;

        /// <summary>在主线程的下一个 editor update 中执行 action。线程安全。</summary>
        public static void Dispatch(Action action)
        {
            _queue.Enqueue(action);
            EnsureRegistered();
        }

        private static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { TJLog.LogError($"[EditorMainThreadDispatcher] {e}"); }
            }
        }
#endif
    }

    /// <summary>
    /// 跟踪 apply_terrain_heightmap 的异步后处理任务（后台线程执行耗时滤波）。
    /// </summary>
    public static class TerrainApplyTaskTracker
    {
#if UNITY_EDITOR
        public class ApplyTaskInfo
        {
            public string ApplyTaskId    { get; set; }
            public string Status         { get; set; }   // "processing" | "completed" | "failed"
            public string HeightmapPath  { get; set; }
            public string TerrainDataPath { get; set; }
            public string TerrainGoName  { get; set; }
            public string ErrorMessage   { get; set; }
            public DateTime StartTime    { get; set; }
            public DateTime? EndTime     { get; set; }
        }

        private static readonly Dictionary<string, ApplyTaskInfo> _tasks = new Dictionary<string, ApplyTaskInfo>();
        private static int _counter;

        public static string CreateTask(string heightmapPath)
        {
            string id = $"terrain_apply_{++_counter}_{DateTime.Now.Ticks}";
            _tasks[id] = new ApplyTaskInfo
            {
                ApplyTaskId = id,
                Status      = "processing",
                HeightmapPath = heightmapPath,
                StartTime   = DateTime.Now
            };
            return id;
        }

        public static void MarkCompleted(string id, string terrainDataPath, string terrainGoName)
        {
            if (_tasks.TryGetValue(id, out var t))
            {
                t.Status          = "completed";
                t.TerrainDataPath = terrainDataPath;
                t.TerrainGoName   = terrainGoName;
                t.EndTime         = DateTime.Now;
            }
        }

        public static void MarkFailed(string id, string error)
        {
            if (_tasks.TryGetValue(id, out var t))
            {
                t.Status       = "failed";
                t.ErrorMessage = error;
                t.EndTime      = DateTime.Now;
            }
        }

        public static ApplyTaskInfo GetTask(string id)
        {
            _tasks.TryGetValue(id, out var t);
            return t;
        }
#endif
    }

    /// <summary>
    /// CustomTools for generating Unity Terrain heightmaps via the Frontier AI model.
    /// Workflow: generate_terrain (async) → query_terrain_status → apply_terrain_heightmap (async) → query_terrain_apply_status.
    /// </summary>
    public static class GenerateTerrainTool
    {
        [ExecuteCustomTool.CustomTool("generate_terrain",
            "Generate a Unity Terrain heightmap using the Frontier AI model (unity_terrain_heightmap template). " +
            "Frontier is the only supported model — there is no generator_id parameter. " +
            "Returns immediately with task_id and placeholder_path. " +
            "After generation completes (~30-90s), call apply_terrain_heightmap with the heightmap_path " +
            "to create a Unity Terrain in the scene. " +
            "Parameters: prompt (terrain description, e.g. 'rolling hills with river valley'), " +
            "image_path (optional reference image), " +
            "resolution ('1K'/'2K'/'4K', default '2K'; terrain heightmaps are always square), " +
            "output_path (optional save path). " +
            "IMPORTANT: Generation takes 30-90 seconds. Wait at least 5 seconds before the first " +
            "query_terrain_status call, then poll every 10-15 seconds.")]
        public static object GenerateTerrain(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateTerrainTool] Generating terrain with parameters: {parameters}");

                string prompt     = parameters["prompt"]?.ToString();
                string imagePath  = parameters["image_path"]?.ToString();
                string resolution = parameters["resolution"]?.ToString() ?? "2K";
                string outputPath = parameters["output_path"]?.ToString();
                string sessionId  = parameters["session_id"]?.ToString() ?? "";
                // aspect_ratio is always "1:1" for terrain heightmaps (square)
                const string aspectRatio = "1:1";

                if (string.IsNullOrEmpty(prompt) && string.IsNullOrEmpty(imagePath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Either 'prompt' or 'image_path' must be provided" }
                    };
                }

                int maxLen = TJGeneratorsPromptLimits.GetMaxLength("frontier-effect");
                if (!string.IsNullOrEmpty(prompt) && prompt.Length > maxLen)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error_code", "INVALID_PARAMS" },
                        { "message", $"Prompt length ({prompt.Length}) exceeds the {maxLen} character limit." }
                    };
                }

                // 加载 frontier-effect 配置（地形高度图固定使用此生成器）
                var config = ConfigManager.GetGeneratorConfig(ConfigType.Image, "frontier-effect");
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Cannot find image generator config for 'frontier-effect'. " +
                                     "Please run AI/开发/清除配置缓存并重新加载 (requires TJGENERATORS_DEBUG)." }
                    };
                }

                var generator = new DynamicGenerator(config);

                if (!string.IsNullOrEmpty(prompt))
                    generator.SetTextPrompt(prompt);

                if (!string.IsNullOrEmpty(imagePath))
                    generator.SetImagePath(imagePath);

                // 注入地形高度图提示词模板
                var templateConfig = config.promptTemplateSelector?.options
                    ?.Find(t => string.Equals(t.id, "unity_terrain_heightmap", StringComparison.OrdinalIgnoreCase));
                if (templateConfig != null)
                    generator.SetPromptTemplateSelection(templateConfig);

                // 设置 Frontier 参数
                generator.SetParameter("resolution", resolution);
                generator.SetParameter("aspectRatio", aspectRatio);
                generator.SetParameter("outputFormat", "png");

                // 同步提交任务
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateTerrainTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateTerrainTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 创建占位符 PNG
                string placeholderPath = CreatePlaceholderTexture(outputPath);

                // 注册任务
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = TerrainTaskTracker.CreateTask(prompt, imagePath, placeholderPath, capturedBackendTaskId);

                // 启动异步轮询
                var host = new TerrainPipelineHost(
                    placeholderPath,
                    sessionId,
                    (heightmapPath, previewUrl) =>
                    {
                        TerrainTaskTracker.MarkTaskCompleted(taskId, heightmapPath, previewUrl);
                        var t = TerrainTaskTracker.GetTask(taskId);
                        GenerationNotifier.NotifyCompleted("generate_terrain", taskId, capturedBackendTaskId,
                            new JObject
                            {
                                ["session_id"]       = sessionId,
                                ["prompt"]           = prompt ?? "",
                                ["heightmap_path"]   = heightmapPath ?? "",
                                ["preview_url"]      = previewUrl ?? "",
                                ["progress"]         = 100,
                                ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                            });
                    },
                    errorMsg =>
                    {
                        TerrainTaskTracker.MarkTaskFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_terrain", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["prompt"] = prompt ?? "" });
                    }
                );
                var pipeline = new GenerationPipeline(host, ConfigType.Image);
                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateTerrainTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}, placeholder={placeholderPath}");

                string mode = string.IsNullOrEmpty(imagePath) ? "text-to-heightmap" : "image-to-heightmap";

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Terrain heightmap generation started. " +
                        "STEP 1 (do now): Note the placeholder_path. " +
                        "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                        "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~60s) " +
                        "containing ALL results (heightmap_path, preview_url, timing, etc.). " +
                        "When completed, call apply_terrain_heightmap with the heightmap_path. " +
                        "*** POLLING IS STRICTLY FORBIDDEN — only call query_terrain_status ONCE as a last-resort fallback. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
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
                TJLog.LogError($"[GenerateTerrainTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating terrain: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("apply_terrain_heightmap",
            "Start async post-processing of a heightmap PNG and place a Unity Terrain in the scene. " +
            "IMPORTANT: This is ASYNC — returns apply_task_id immediately (< 1s). " +
            "Post-processing (bilateral filter + thermal erosion) runs in background (~10-60s). " +
            "Poll query_terrain_apply_status with apply_task_id to check completion. " +
            "Parameters: heightmap_path (required), " +
            "task_id (optional but strongly recommended — prevents duplicate terrain creation; pass the task_id from generate_terrain), " +
            "session_id (optional, string — associates generated assets with a session for tracking), " +
            "use_default_options (bool, default true — uses recommended median+bilateral+erosion+percentile preset), " +
            "terrain_go_name (string, default 'TJGenerators Terrain'). " +
            "Advanced post-processing (only when use_default_options=false): " +
            "median3x3 (bool), gaussian_blur (bool), gaussian_sigma (float), " +
            "bilateral_filter (bool), bilateral_sigma_space (float), bilateral_sigma_color (float), " +
            "thermal_erosion (bool), thermal_erosion_iterations (int), thermal_erosion_talus (float), " +
            "percentile_normalize (bool), percentile_low (float), percentile_high (float), " +
            "height_gamma (float, <1 more mountains, >1 more plains), " +
            "remap_output_min (float), remap_output_max (float).")]
        public static object ApplyTerrainHeightmap(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string heightmapPath = parameters["heightmap_path"]?.ToString();
                if (string.IsNullOrEmpty(heightmapPath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'heightmap_path' parameter is required." }
                    };
                }

                string originalPath = heightmapPath.Replace('\\', '/');
                string absOriginal  = PathUtils.ToAbsoluteAssetPath(originalPath);
                if (!File.Exists(absOriginal))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Heightmap file not found: {heightmapPath}. Make sure query_terrain_status returns status='completed' before calling apply." }
                    };
                }

                string taskId        = parameters["task_id"]?.ToString() ?? "";
                string sessionId     = parameters["session_id"]?.ToString() ?? "";
                string terrainGoName = parameters["terrain_go_name"]?.ToString() ?? "TJGenerators Terrain";

                // 重入防护：generate_terrain task 已 applied → 直接返回结果
                if (!string.IsNullOrEmpty(taskId))
                {
                    var existingTask = TerrainTaskTracker.GetTask(taskId);
                    if (existingTask != null && existingTask.Status == "applied")
                    {
                        TJLog.LogWarning($"[GenerateTerrainTool] task {taskId} already applied. Skipping.");
                        return new Dictionary<string, object>
                        {
                            { "success",         true },
                            { "already_applied", true },
                            { "terrain_data_path", existingTask.TerrainDataPath ?? "" },
                            { "terrain_go_name",   existingTask.TerrainGoName ?? "" },
                            { "message",         $"Terrain already applied for task {taskId}. Do NOT call apply_terrain_heightmap again. WORKFLOW COMPLETE." }
                        };
                    }
                }

                // 解析后处理选项
                bool useDefaults = parameters["use_default_options"] == null ||
                                   parameters["use_default_options"].ToObject<bool>();
                var opts = useDefaults ? TerrainHeightmapPostProcessOptions.Default :
                    new TerrainHeightmapPostProcessOptions
                    {
                        median3x3                = GetBool(parameters, "median3x3", true),
                        gaussianBlur             = GetBool(parameters, "gaussian_blur", true),
                        gaussianSigma            = GetFloat(parameters, "gaussian_sigma", 1.2f),
                        bilateralFilter          = GetBool(parameters, "bilateral_filter", true),
                        bilateralSigmaSpace      = GetFloat(parameters, "bilateral_sigma_space", 3.0f),
                        bilateralSigmaColor      = GetFloat(parameters, "bilateral_sigma_color", 0.2f),
                        thermalErosion           = GetBool(parameters, "thermal_erosion", true),
                        thermalErosionIterations = GetInt(parameters, "thermal_erosion_iterations", 25),
                        thermalErosionTalus      = GetFloat(parameters, "thermal_erosion_talus", 0.02f),
                        percentileNormalization  = GetBool(parameters, "percentile_normalize", true),
                        percentileLow            = GetFloat(parameters, "percentile_low", 0.05f),
                        percentileHigh           = GetFloat(parameters, "percentile_high", 0.95f),
                        heightGamma              = GetFloat(parameters, "height_gamma", 1.0f),
                        remapOutputMin           = GetFloat(parameters, "remap_output_min", 0.02f),
                        remapOutputMax           = GetFloat(parameters, "remap_output_max", 0.98f),
                    };

                // ── 主线程步骤1：生成 processed 路径并复制原图 ────────────────
                string processedAssetPath = TerrainCreationUtils.GenerateProcessedHeightmapAssetPath(originalPath);
                if (string.IsNullOrEmpty(processedAssetPath))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Cannot generate post-processing file path" }
                    };
                }
                string absProcessed = PathUtils.ToAbsoluteAssetPath(processedAssetPath);
                string processedDir = Path.GetDirectoryName(absProcessed);
                if (!string.IsNullOrEmpty(processedDir) && !Directory.Exists(processedDir))
                    Directory.CreateDirectory(processedDir);
                try { File.Copy(absOriginal, absProcessed, overwrite: true); }
                catch (Exception e)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Failed to copy heightmap: " + e.Message }
                    };
                }

                // ── 主线程步骤2：解码 PNG → float[]（快速，< 100 ms） ─────────
                if (!TerrainHeightmapPostProcessor.TryExtractPixelData(
                        absProcessed, out float[] luminance, out int imgW, out int imgH, out string extractErr))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Failed to decode heightmap: " + extractErr }
                    };
                }

                // ── 注册 apply task ──────────────────────────────────────────
                string applyTaskId = TerrainApplyTaskTracker.CreateTask(heightmapPath);
                TJLog.Log($"[GenerateTerrainTool] apply_terrain_heightmap started async: applyTaskId={applyTaskId}, processed={processedAssetPath}");

                // ── 后台线程：执行耗时滤波（10-60s） ─────────────────────────
                // 捕获所有需要的值（不能跨线程引用 Unity 对象）
                string capturedApplyTaskId     = applyTaskId;
                string capturedProcessedAsset  = processedAssetPath;
                string capturedAbsProcessed    = absProcessed;
                string capturedTerrainGoName   = terrainGoName;
                string capturedSessionId       = sessionId;
                string capturedTaskId          = taskId;
                string capturedHeightmapPath   = heightmapPath;

                // 2K 图双边滤波约 60-180s，设 300s 超时自动标记失败
                const int timeoutSeconds = 300;

                Task.Run(() =>
                {
                    try
                    {
                        // 超时检查：开始时记录时间，完成后检查是否超时
                        var filterStart = DateTime.Now;
                        TerrainHeightmapPostProcessor.ApplyFilters(luminance, imgW, imgH, opts);
                        double filterSeconds = (DateTime.Now - filterStart).TotalSeconds;
                        TJLog.Log($"[GenerateTerrainTool] ApplyFilters done in {filterSeconds:F1}s, dispatching to main thread");

                        // ── 通过线程安全队列回到主线程 ──────────────────────────
                        EditorMainThreadDispatcher.Dispatch(() =>
                        {
                            try
                            {
                                if (!TerrainHeightmapPostProcessor.TryEncodeAndSave(
                                        capturedAbsProcessed, luminance, imgW, imgH, out string saveErr))
                                {
                                    TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, "PNG encoding failed: " + saveErr);
                                    GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", "PNG encoding failed: " + saveErr,
                                        new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                                    TJLog.LogError($"[GenerateTerrainTool] encode failed: {saveErr}");
                                    return;
                                }

                                AssetDatabase.ImportAsset(capturedProcessedAsset, ImportAssetOptions.ForceUpdate);

                                string terrainDataPath; string goName;
                                try
                                {
                                    (terrainDataPath, goName) = TerrainCreationUtils.CreateTerrainFromHeightmap(
                                        capturedProcessedAsset, capturedTerrainGoName);
                                }
                                catch (Exception ex)
                                {
                                    TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, "Terrain creation failed: " + ex.Message);
                                    GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", "Terrain creation failed: " + ex.Message,
                                        new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                                    TJLog.LogError($"[GenerateTerrainTool] CreateTerrain failed: {ex}");
                                    return;
                                }

                                // label
                                var terrainRef   = TJGeneratorsAssetReference.FromPath(terrainDataPath);
                                var processedRef = TJGeneratorsAssetReference.FromPath(capturedProcessedAsset);
                                TJGeneratorsGenerationLabel.EnableLabel(terrainRef);
                                TJGeneratorsGenerationLabel.EnableSessionLabel(terrainRef, capturedSessionId);
                                TJGeneratorsGenerationLabel.EnableLabel(processedRef);
                                TJGeneratorsGenerationLabel.EnableSessionLabel(processedRef, capturedSessionId);

                                // 标记两个 tracker
                                TerrainApplyTaskTracker.MarkCompleted(capturedApplyTaskId, terrainDataPath, goName);
                                GenerationNotifier.NotifyCompleted("apply_terrain_heightmap", capturedApplyTaskId, "",
                                    new JObject
                                    {
                                        ["session_id"]        = capturedSessionId,
                                        ["heightmap_path"]    = capturedHeightmapPath,
                                        ["terrain_data_path"] = terrainDataPath ?? "",
                                        ["terrain_go_name"]   = goName ?? "",
                                        ["progress"]          = 100
                                    });
                                if (!string.IsNullOrEmpty(capturedTaskId))
                                    TerrainTaskTracker.MarkTaskApplied(capturedTaskId, terrainDataPath, goName);

                                TJLog.Log($"[GenerateTerrainTool] Terrain created async: {goName}, TerrainData={terrainDataPath}");
                            }
                            catch (Exception ex)
                            {
                                TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, ex.Message);
                                GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", ex.Message,
                                    new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                                TJLog.LogError($"[GenerateTerrainTool] main-thread finalization failed: {ex}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        string bgErrMsg = "Background filtering failed: " + ex.Message;
                        EditorMainThreadDispatcher.Dispatch(() =>
                        {
                            TerrainApplyTaskTracker.MarkFailed(capturedApplyTaskId, bgErrMsg);
                            GenerationNotifier.NotifyFailed("apply_terrain_heightmap", capturedApplyTaskId, "", bgErrMsg,
                                new JObject { ["session_id"] = capturedSessionId, ["heightmap_path"] = capturedHeightmapPath });
                        });
                        TJLog.LogError($"[GenerateTerrainTool] background filter failed: {ex}");
                    }
                });

                return new Dictionary<string, object>
                {
                    { "success",          true },
                    { "apply_task_id",    applyTaskId },
                    { "status",           "processing" },
                    { "message",
                        $"Post-processing started in background. " +
                        "A <bg_task_done> notification will arrive automatically when terrain is ready (~60-180s). " +
                        "Do NOT retry apply_terrain_heightmap. " +
                        "*** POLLING IS STRICTLY FORBIDDEN — only call query_terrain_apply_status ONCE as a last-resort fallback. ***" },
                    { "estimated_seconds",        90 },
                    { "notification_mode",        "bg_task_done" },
                    { "max_wait_before_retry_seconds", timeoutSeconds }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] apply error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error starting terrain apply: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("query_terrain_apply_status",
            "Query the status of an apply_terrain_heightmap background task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "Pass the apply_task_id returned by apply_terrain_heightmap. " +
            "Status: 'processing' (background filtering running), " +
            "'completed' (terrain is in the scene — workflow done, do not call apply again), " +
            "'failed' (see error field). " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryTerrainApplyStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string applyTaskId = parameters["apply_task_id"]?.ToString();
                if (string.IsNullOrEmpty(applyTaskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'apply_task_id' is required. It is returned by apply_terrain_heightmap." }
                    };
                }

                var task = TerrainApplyTaskTracker.GetTask(applyTaskId);
                if (task == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"apply task '{applyTaskId}' not found. It may be from a previous Editor session." }
                    };
                }

                var result = new Dictionary<string, object>
                {
                    { "success",        true },
                    { "apply_task_id",  task.ApplyTaskId },
                    { "status",         task.Status },
                    { "heightmap_path", task.HeightmapPath ?? "" },
                    { "start_time",     task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (task.EndTime.HasValue)
                {
                    result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                int elapsedSeconds = (int)(DateTime.Now - task.StartTime).TotalSeconds;
                result["elapsed_seconds"] = elapsedSeconds;

                switch (task.Status)
                {
                    case "processing":
                        result["message"] = $"Background post-processing is running ({elapsedSeconds}s elapsed). 2K images take 60-180s — this is normal. Do NOT retry apply_terrain_heightmap.";
                        break;
                    case "completed":
                        result["terrain_data_path"] = task.TerrainDataPath ?? "";
                        result["terrain_go_name"]   = task.TerrainGoName ?? "";
                        result["message"]           = "Terrain is in the scene. WORKFLOW COMPLETE. Do NOT call apply_terrain_heightmap again.";
                        result["next_step"]         = "DONE. Terrain GameObject is selected in scene.";
                        break;
                    case "failed":
                        result["error"]   = task.ErrorMessage ?? "";
                        result["message"] = "Post-processing failed. You may retry with apply_terrain_heightmap.";
                        break;
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] query apply status error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying apply status: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("query_terrain_status",
            "Query the status of a terrain heightmap generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, heightmap_path contains the PNG ready for apply_terrain_heightmap. " +
            "Status values: 'generating', 'completed', 'failed', 'interrupted'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryTerrainStatus(JObject parameters)
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

                var task = TerrainTaskTracker.GetTask(taskId);

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
                    { "success",    true },
                    { "task_id",    task.TaskId },
                    { "status",     task.Status },
                    { "progress",   task.Progress },
                    { "prompt",     task.Prompt },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.ImagePath))     result["input_image_path"] = task.ImagePath;
                if (!string.IsNullOrEmpty(task.HeightmapPath)) result["heightmap_path"]   = task.HeightmapPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.ErrorMessage))  result["error"]            = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"]        = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "generating")
                {
                    if (!string.IsNullOrEmpty(task.PlaceholderPath))
                        result["placeholder_path"] = task.PlaceholderPath;
                }
                else if (task.Status == "completed")
                {
                    result["next_step"] = "Call apply_terrain_heightmap ONCE with this task_id and heightmap_path. Do NOT call it again after that.";
                }
                else if (task.Status == "applied")
                {
                    result["terrain_data_path"] = task.TerrainDataPath ?? "";
                    result["terrain_go_name"]   = task.TerrainGoName ?? "";
                    result["next_step"]         = "DONE. Terrain is already in the scene. Do NOT call apply_terrain_heightmap again. Workflow complete.";
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateTerrainTool] Query error: {e}");
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

        [ExecuteCustomTool.CustomTool("list_terrain_tasks", "List all active and recent terrain heightmap generation tasks.")]
        public static object ListTerrainTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks    = TerrainTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id",    task.TaskId },
                        { "status",     task.Status },
                        { "progress",   task.Progress },
                        { "prompt",     task.Prompt },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.ImagePath))     taskData["input_image_path"] = task.ImagePath;
                    if (!string.IsNullOrEmpty(task.HeightmapPath)) taskData["heightmap_path"]   = task.HeightmapPath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.ErrorMessage))  taskData["error"]            = task.ErrorMessage;
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
                TJLog.LogError($"[GenerateTerrainTool] List error: {e}");
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

        private static string CreatePlaceholderTexture(string outputPath)
        {
            string placeholderPath;
            if (!string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    EnsureAssetDatabaseFolder(dir);
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(outputPath, ".png"));
            }
            else
            {
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                    AssetDatabase.CreateFolder("Assets", "TJGenerators");
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                    AssetDatabase.CreateFolder("Assets/TJGenerators", "History");
                string uniqueName = "Terrain_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                placeholderPath = AssetDatabase.GenerateUniqueAssetPath("Assets/TJGenerators/History/" + uniqueName);
            }

            // 创建 1x1 灰色占位 PNG
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 1f));
            tex.Apply();
            byte[] pngBytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string absolutePath = PathUtils.ToAbsoluteAssetPath(placeholderPath);
            File.WriteAllBytes(absolutePath, pngBytes);
            PathUtils.ImportAssetAfterDiskWrite(placeholderPath);

            return placeholderPath;
        }

        private static bool GetBool(JObject p, string key, bool defaultValue)
            => p[key] != null ? p[key].ToObject<bool>() : defaultValue;

        private static float GetFloat(JObject p, string key, float defaultValue)
            => p[key] != null ? p[key].ToObject<float>() : defaultValue;

        private static int GetInt(JObject p, string key, int defaultValue)
            => p[key] != null ? p[key].ToObject<int>() : defaultValue;
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// IGenerationPipelineHost implementation for headless terrain heightmap generation via custom tools.
    /// Keeps TextureImporterType.Default (not Sprite) — heightmaps are raw grayscale data.
    /// </summary>
    internal class TerrainPipelineHost : IGenerationPipelineHost
    {
        private readonly string _placeholderPath;
        private readonly TJGeneratorsAssetReference _placeholderRef;
        private readonly string _sessionId;
        private readonly Action<string, string> _onCompleted;  // (heightmapPath, previewUrl)
        private readonly Action<string> _onFailed;

        public TerrainPipelineHost(string placeholderPath, string sessionId, Action<string, string> onCompleted, Action<string> onFailed)
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
            ErrorDialogUtils.ShowErrorDialog(title, message, (errorMessage) => _onFailed?.Invoke(errorMessage), "GenerateTerrainTool");
        }

        public string GetTextureSavePath(ModelGeneratorBase generator)
        {
            // 返回 placeholder 路径，pipeline 直接覆盖文件内容，保持 GUID 不变
            return _placeholderPath;
        }

        public void OnTextureSaved(string savePath, ModelGeneratorBase generator)
        {
            TJLog.Log($"[GenerateTerrainTool] Heightmap saved: {savePath}");

            // 保持 TextureImporterType.Default（高度图不是 Sprite）
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));
            TJGeneratorsGenerationLabel.EnableSessionLabel(TJGeneratorsAssetReference.FromPath(savePath), _sessionId);
            _onCompleted?.Invoke(savePath, generator.CurrentPreviewUrl);
        }

        public string GetAudioSavePath(ModelGeneratorBase generator) => null;
        public void OnAudioSaved(string savePath, ModelGeneratorBase generator) { }
        public string GetVideoSavePath(ModelGeneratorBase generator) => null;
        public void OnVideoSaved(string savePath, ModelGeneratorBase generator) { }
    }
#endif
}
