using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Tracks active image generation tasks.
    /// </summary>
    public static class ImageTaskTracker
    {
#if UNITY_EDITOR
        private static readonly Dictionary<string, ImageTaskInfo> _activeTasks = new Dictionary<string, ImageTaskInfo>();
        private static int _taskIdCounter = 0;

        private const string SessionKeyIds = "TJGen_Image_Ids";
        private const string SessionKeyFmt = "TJGen_Image_{0}";

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string generatorId;
            public string prompt;
            public string imagePath;
            public string status;
            public int    progress;
            public string resultPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;
            public string previewUrl;
            public string placeholderPath;
            public string backendTaskId;
        }

        public class ImageTaskInfo
        {
            public string TaskId { get; set; }
            public string GeneratorId { get; set; }
            public string Prompt { get; set; }
            public string ImagePath { get; set; }
            public string Status { get; set; }
            public int Progress { get; set; }
            public string ResultPath { get; set; }
            public string ErrorMessage { get; set; }
            public string PreviewUrl { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public string PlaceholderPath { get; set; }
            public string BackendTaskId { get; set; }
        }

        internal static void SaveToSession(ImageTaskInfo info)
        {
            var p = new PersistedTask
            {
                taskId          = info.TaskId,
                generatorId     = info.GeneratorId,
                prompt          = info.Prompt ?? "",
                imagePath       = info.ImagePath ?? "",
                status          = info.Status,
                progress        = info.Progress,
                resultPath      = info.ResultPath ?? "",
                errorMessage    = info.ErrorMessage ?? "",
                startTimeTicks  = info.StartTime.Ticks,
                endTimeTicks    = info.EndTime?.Ticks ?? 0,
                previewUrl      = info.PreviewUrl ?? "",
                placeholderPath = info.PlaceholderPath ?? "",
                backendTaskId   = info.BackendTaskId ?? ""
            };
            SessionState.SetString(string.Format(SessionKeyFmt, info.TaskId), JsonUtility.ToJson(p));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(info.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? info.TaskId : ids + "|" + info.TaskId);
        }

        private static ImageTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json)) return null;
            PersistedTask p;
            try { p = JsonUtility.FromJson<PersistedTask>(json); }
            catch { return null; }

            var info = new ImageTaskInfo
            {
                TaskId          = p.taskId,
                GeneratorId     = p.generatorId,
                Prompt          = p.prompt,
                ImagePath       = p.imagePath,
                Status          = p.status,
                Progress        = p.progress,
                ResultPath      = p.resultPath,
                ErrorMessage    = p.errorMessage,
                PreviewUrl      = p.previewUrl,
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

        public static string CreateTask(string generatorId, string prompt, string imagePath = null, string placeholderPath = null, string backendTaskId = null)
        {
            string taskId = $"image_{++_taskIdCounter}_{DateTime.Now.Ticks}";

            var task = new ImageTaskInfo
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

        public static void MarkTaskCompleted(string taskId, string resultPath, string previewUrl = null)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
            {
                task.Status     = "completed";
                task.Progress   = 100;
                task.ResultPath = resultPath;
                task.PreviewUrl = previewUrl;
                task.EndTime    = DateTime.Now;
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

        public static ImageTaskInfo GetTask(string taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var task)) return task;
            return TryRestoreFromSession(taskId);
        }

        public static List<ImageTaskInfo> GetAllTasks()
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
            return new List<ImageTaskInfo>(_activeTasks.Values);
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
    /// Tracks auto 2D sprite-sequence workflow tasks.
    /// </summary>
    public static class AutoSpriteSequenceTaskTracker
    {
#if UNITY_EDITOR
        private const string SessionKeyIds = "TJGen_AutoSpriteSeq_Ids";
        private const string SessionKeyFmt = "TJGen_AutoSpriteSeq_{0}";

        [Serializable]
        private class PersistedAutoTask
        {
            public string taskId;
            public string imageTaskId;
            public string status;
            public string prompt;
            public string error;
            public string imagePath;
            public string spritesFolder;
            public string animationPath;
            public int sliceColumns;
            public int sliceRows;
            public float chromaTolerance;
            public float chromaFeather;
            public bool loop;
            public float fps;
            public long startTimeTicks;
            public long endTimeTicks;
            public bool postProcessDone;
        }

        public class AutoTaskInfo
        {
            public string TaskId { get; set; }
            public string ImageTaskId { get; set; }
            public string Status { get; set; } // submitted, generating, recovering, postprocessing, completed, failed, interrupted
            public string Prompt { get; set; }
            public string Error { get; set; }
            public string ImagePath { get; set; }
            public string SpritesFolder { get; set; }
            public string AnimationPath { get; set; }
            public int SliceColumns { get; set; }
            public int SliceRows { get; set; }
            public float ChromaTolerance { get; set; }
            public float ChromaFeather { get; set; }
            public bool Loop { get; set; }
            public float Fps { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime? EndTime { get; set; }
            public bool PostProcessDone { get; set; }
        }

        private static readonly Dictionary<string, AutoTaskInfo> _tasks = new Dictionary<string, AutoTaskInfo>();
        private static int _counter = 0;

        public static string CreateTask(string imageTaskId, string prompt)
        {
            string id = $"auto_sprite_seq_{++_counter}_{DateTime.Now.Ticks}";
            _tasks[id] = new AutoTaskInfo
            {
                TaskId = id,
                ImageTaskId = imageTaskId,
                Status = "submitted",
                Prompt = prompt ?? "",
                StartTime = DateTime.Now
            };
            SaveToSession(_tasks[id]);
            return id;
        }

        public static AutoTaskInfo GetTask(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var t))
                return t;
            return TryRestoreFromSession(taskId);
        }

        public static List<AutoTaskInfo> GetAllTasks()
        {
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!string.IsNullOrEmpty(ids))
            {
                foreach (var id in ids.Split('|'))
                {
                    if (!string.IsNullOrEmpty(id) && !_tasks.ContainsKey(id))
                        TryRestoreFromSession(id);
                }
            }
            return new List<AutoTaskInfo>(_tasks.Values);
        }

        public static void Save(AutoTaskInfo task)
        {
            if (task == null || string.IsNullOrEmpty(task.TaskId))
                return;
            _tasks[task.TaskId] = task;
            SaveToSession(task);
        }

        public static void RemoveTask(string taskId)
        {
            _tasks.Remove(taskId);
            SessionState.EraseString(string.Format(SessionKeyFmt, taskId));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (string.IsNullOrEmpty(ids))
                return;
            var list = new List<string>(ids.Split('|'));
            list.Remove(taskId);
            SessionState.SetString(SessionKeyIds, string.Join("|", list));
        }

        public static void CleanupCompletedTasks()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _tasks)
            {
                var t = kvp.Value;
                if ((t.Status == "completed" || t.Status == "failed" || t.Status == "interrupted")
                    && t.EndTime.HasValue
                    && (DateTime.Now - t.EndTime.Value).TotalMinutes > 60)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                RemoveTask(id);
        }

        private static void SaveToSession(AutoTaskInfo task)
        {
            var p = new PersistedAutoTask
            {
                taskId = task.TaskId,
                imageTaskId = task.ImageTaskId ?? "",
                status = task.Status ?? "",
                prompt = task.Prompt ?? "",
                error = task.Error ?? "",
                imagePath = task.ImagePath ?? "",
                spritesFolder = task.SpritesFolder ?? "",
                animationPath = task.AnimationPath ?? "",
                sliceColumns = task.SliceColumns,
                sliceRows = task.SliceRows,
                chromaTolerance = task.ChromaTolerance,
                chromaFeather = task.ChromaFeather,
                loop = task.Loop,
                fps = task.Fps,
                startTimeTicks = task.StartTime.Ticks,
                endTimeTicks = task.EndTime?.Ticks ?? 0,
                postProcessDone = task.PostProcessDone
            };
            SessionState.SetString(string.Format(SessionKeyFmt, task.TaskId), JsonUtility.ToJson(p));
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(task.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? task.TaskId : ids + "|" + task.TaskId);
        }

        private static AutoTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json))
                return null;

            PersistedAutoTask p;
            try { p = JsonUtility.FromJson<PersistedAutoTask>(json); }
            catch { return null; }

            var t = new AutoTaskInfo
            {
                TaskId = p.taskId,
                ImageTaskId = p.imageTaskId,
                Status = p.status,
                Prompt = p.prompt,
                Error = p.error,
                ImagePath = p.imagePath,
                SpritesFolder = p.spritesFolder,
                AnimationPath = p.animationPath,
                SliceColumns = p.sliceColumns,
                SliceRows = p.sliceRows,
                ChromaTolerance = p.chromaTolerance,
                ChromaFeather = p.chromaFeather,
                Loop = p.loop,
                Fps = p.fps,
                StartTime = p.startTimeTicks > 0 ? new DateTime(p.startTimeTicks) : DateTime.Now,
                EndTime = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null,
                PostProcessDone = p.postProcessDone
            };

            // domain reload 恢复语义：进行中任务标记为 recovering，允许 query 再次驱动流程
            if ((t.Status == "submitted" || t.Status == "generating" || t.Status == "postprocessing") && !t.PostProcessDone)
            {
                t.Status = "recovering";
                t.Error = "";
                t.EndTime = null;
            }

            _tasks[taskId] = t;
            return t;
        }
#endif
    }

    /// <summary>
    /// CustomTool for generating image assets using TJGenerators Image pipeline.
    /// Supports text-to-image and image-to-image generation.
    /// Supported models: frontier-game-design (default), huoshan_seedream_image, frontier-effect.
    /// Output is a PNG (TextureImporterType.Default) saved to Assets/TJGenerators/History/.
    /// </summary>
    public static class GenerateImageTool
    {
        [ExecuteCustomTool.CustomTool("generate_image",
            "Generate an image asset from a text prompt or reference image using AI. " +
            "Output is a PNG (Texture2D, Default type) saved to Assets/TJGenerators/History/. " +
            "Key parameters: generator_id (default 'frontier-game-design'; or 'huoshan_seedream_image', 'frontier-effect'), " +
            "prompt (text description), image_path (optional reference image — omit for text-to-image), " +
            "size (output resolution, e.g. '2048x2048', huoshan_seedream_image only), " +
            "is_segmentation (bool, auto-remove background, default false, huoshan_seedream_image only), " +
            "resolution (frontier-effect only, '0.5K'/'1K'/'2K'/'4K', default '1K'), " +
            "aspect_ratio (frontier-effect only, 'auto'/'16:9'/'9:16'/'1:1'/'4:3'/'3:4'/'3:2'/'2:3'/'5:4'/'4:5'/'21:9', default 'auto'), " +
            "output_format (frontier-effect only, 'png'/'jpeg', default 'png'), " +
            "imageSize (frontier-game-design only, 'square_hd'/'square'/'portrait_4_3'/'portrait_16_9'/'landscape_4_3'/'landscape_16_9', default 'square_hd'), " +
            "outputFormat (frontier-game-design only, 'png'/'jpeg', default 'png'), " +
            "prompt_template (frontier-game-design only, 'game_icon'/'concept_art', optional prompt prefix), " +
            "output_path (optional save path). " +
            "IMPORTANT: Generation takes 30-90 seconds. Wait at least 5 seconds before the first " +
            "query_image_status call, then poll every 10-15 seconds. " +
            "A placeholder_path is returned immediately — you can reference it right away.")]
        public static object GenerateImage(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateImageTool] Generating image with parameters: {parameters}");

                string generatorId = parameters["generator_id"]?.ToString() ?? "frontier-game-design";
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

                int maxLen = GetImagePromptMaxLength(generatorId);
                if (maxLen > 0 && !string.IsNullOrEmpty(prompt) && prompt.Length > maxLen)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error_code", "INVALID_PARAMS" },
                        { "message", $"Prompt length ({prompt.Length}) exceeds the {maxLen} character limit for '{generatorId}'." }
                    };
                }

                // 加载图片生成器配置
                var config = ConfigManager.GetGeneratorConfig(ConfigType.Image, generatorId);
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Cannot find image generator config for '{generatorId}'. Valid values: 'frontier-game-design', 'huoshan_seedream_image', 'frontier-effect'." }
                    };
                }

                // 创建生成器并设置输入
                var generator = new DynamicGenerator(config);

                if (!string.IsNullOrEmpty(prompt))
                    generator.SetTextPrompt(prompt);

                string userDisplayForHistory = parameters["prompt"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(userDisplayForHistory))
                    generator.SetHistoryDisplayPrompt(userDisplayForHistory);

                if (!string.IsNullOrEmpty(imagePath))
                    generator.SetImagePath(imagePath);

                // 应用 prompt_template（frontier-game-design 专用）
                string promptTemplateId = parameters["prompt_template"]?.ToString();
                if (!string.IsNullOrEmpty(promptTemplateId) && config?.promptTemplateSelector?.options != null)
                {
                    var template = config.promptTemplateSelector.options.Find(t => t.id == promptTemplateId);
                    if (template != null)
                        generator.SetPromptTemplateSelection(template);
                    else
                        TJLog.LogWarning($"[GenerateImageTool] prompt_template '{promptTemplateId}' not found in config, ignoring.");
                }

                // 应用可选参数
                ApplyImageParameters(generator, parameters);

                // 阶段1：同步提交任务到后端
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateImageTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateImageTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 提交成功后再创建 placeholder（避免鉴权失败时留下无用文件）
                string placeholderPath = CreatePlaceholderTexture(outputPath);

                // 注册任务
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = ImageTaskTracker.CreateTask(generatorId, prompt, imagePath, placeholderPath, capturedBackendTaskId);

                // 创建 pipeline host
                var host = new ImagePipelineHost(
                    placeholderPath,
                    sessionId,
                    (savedPath, previewUrl) =>
                    {
                        ImageTaskTracker.MarkTaskCompleted(taskId, savedPath, previewUrl);
                        var t = ImageTaskTracker.GetTask(taskId);
                        GenerationNotifier.NotifyCompleted("generate_image", taskId, capturedBackendTaskId,
                            new JObject
                            {
                                ["session_id"]       = sessionId,
                                ["generator_id"]     = generatorId,
                                ["prompt"]           = prompt ?? "",
                                ["image_path"]       = savedPath,
                                ["preview_url"]      = previewUrl ?? "",
                                ["progress"]         = 100,
                                ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                            });
                    },
                    errorMsg =>
                    {
                        ImageTaskTracker.MarkTaskFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_image", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["generator_id"] = generatorId, ["prompt"] = prompt ?? "" });
                    }
                );

                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);

                // 阶段2：异步轮询（跳过提交）
                var pipeline = new GenerationPipeline(host, ConfigType.Image);
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateImageTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}, placeholder: {placeholderPath}");

                string mode = string.IsNullOrEmpty(imagePath) ? "text-to-image" : "image-to-image";

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Image generation started. " +
                        "STEP 1 (do now): Apply placeholder_path to the scene if needed. " +
                        "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                        "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~60s) " +
                        "containing ALL generation results (image_path, preview_url, timing, etc.). " +
                        "*** POLLING IS STRICTLY FORBIDDEN — do NOT call query_image_status repeatedly. " +
                        "Only call query_image_status ONCE as a last-resort fallback if no notification arrives. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       generatorId },
                    { "mode",               mode },
                    { "prompt",             prompt ?? "" },
                    { "placeholder_path",   placeholderPath },
                    { "estimated_wait_seconds", 60 },
                    { "notification_mode",  "bg_task_done" },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating image: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("generate_frontier_sequence",
            "Generate a sequence-style image using Frontier (frontier-game-design) with profile-based spritesheet instructions. " +
            "User reference images (image_path) lock character identity; profile knowledge_refs layout images lock grid alignment. " +
            "Parameters: prompt, image_path (optional), imageSize, outputFormat, output_path. " +
            "Optional: profile_id ('general_sprite_sequence_4x4' default), instructions (override), knowledge_refs (array).")]
        public static object GenerateFrontierSequence(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateImageTool] Generating frontier sequence with parameters: {parameters}");

                var wrapped = parameters != null ? (JObject)parameters.DeepClone() : new JObject();
                wrapped["generator_id"] = "frontier-game-design";
                if (wrapped["imageSize"] == null)
                    wrapped["imageSize"] = "square_hd";
                if (wrapped["outputFormat"] == null)
                    wrapped["outputFormat"] = "png";

                string profileId = null;
                var profileResult = ResolveFrontierSequenceProfileAndEnvelope(wrapped, out profileId);
                if (profileResult.Error != null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", profileResult.Error }
                    };
                }

                string layoutFileErr = ValidateFrontierKnowledgeLayoutFilesExist(profileResult.KnowledgeRefs);
                if (layoutFileErr != null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", layoutFileErr }
                    };
                }

                var envelope = BuildFrontierSequenceEnvelopeJObject(profileResult, wrapped, includeUserReferenceRefs: true);

                // 通过 DynamicGenerator 扩展字段透传到后端
                wrapped["frontier_sequence_envelope_raw"] = envelope.ToString();
                string rawUserPrompt = wrapped["prompt"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(rawUserPrompt))
                    wrapped["user_display_prompt"] = rawUserPrompt;
                wrapped["prompt"] = BuildPromptWithInstructionsFallback(
                    wrapped["prompt"]?.ToString(),
                    profileResult.Instructions
                );

                var result = GenerateImageInternal(wrapped, enableFrontierSequenceEnvelope: true);
                if (result is Dictionary<string, object> dict && dict.TryGetValue("success", out var ok) && ok is bool b && b)
                {
                    dict["template_envelope"] = envelope.ToObject<object>();
                    dict["template_notes"] = "Use profile_id general_sprite_sequence_4x4 (default). knowledge_refs from profile or request override.";
                    if (!string.IsNullOrEmpty(profileId))
                        dict["profile_id"] = profileId;
                    dict["slice_columns"] = profileResult.SliceColumns;
                    dict["slice_rows"] = profileResult.SliceRows;
                    if (profileResult.LocalKnowledgeCount > 0)
                        dict["local_knowledge_encoded_count"] = profileResult.LocalKnowledgeCount;
                }
                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in generate_frontier_sequence: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating frontier sequence: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("generate_2d_sprite_sequence_auto",
            "Generate 2D sprite-sequence assets asynchronously (Frontier image generation + auto cutout + fixed-grid slicing + AnimationClip). " +
            "Status flow: submitted -> generating -> postprocessing -> completed/failed. " +
            "Params: prompt(required), image_path(optional), profile_id(optional), " +
            "chroma_tolerance/chroma_feather(optional), fps(optional), loop(optional). " +
            "IMPORTANT: This call returns immediately; use query_2d_sprite_sequence_auto_status to poll.")]
        public static object Generate2DSpriteSequenceAuto(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var wrapped = parameters != null ? (JObject)parameters.DeepClone() : new JObject();
                wrapped["generator_id"] = "frontier-game-design";
                if (wrapped["imageSize"] == null)
                    wrapped["imageSize"] = "square_hd";
                if (wrapped["outputFormat"] == null)
                    wrapped["outputFormat"] = "png";

                string profileId = null;
                var profileResult = ResolveFrontierSequenceProfileAndEnvelope(wrapped, out profileId);
                if (!string.IsNullOrEmpty(profileResult.Error))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", profileResult.Error }
                    };
                }

                string layoutFileErr2 = ValidateFrontierKnowledgeLayoutFilesExist(profileResult.KnowledgeRefs);
                if (layoutFileErr2 != null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", layoutFileErr2 }
                    };
                }

                var envelope = BuildFrontierSequenceEnvelopeJObject(profileResult, wrapped, includeUserReferenceRefs: true);
                wrapped["frontier_sequence_envelope_raw"] = envelope.ToString();
                string rawUserPromptAuto = wrapped["prompt"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(rawUserPromptAuto))
                    wrapped["user_display_prompt"] = rawUserPromptAuto;
                wrapped["prompt"] = BuildPromptWithInstructionsFallback(
                    wrapped["prompt"]?.ToString(),
                    profileResult.Instructions
                );

                var imageResult = GenerateImageInternal(wrapped, enableFrontierSequenceEnvelope: true);
                if (!(imageResult is Dictionary<string, object> imageDict)
                    || !imageDict.TryGetValue("success", out var okObj)
                    || !(okObj is bool ok)
                    || !ok)
                {
                    return imageResult;
                }

                string imageTaskId = imageDict.TryGetValue("task_id", out var imageTaskObj) ? imageTaskObj?.ToString() : null;
                if (string.IsNullOrEmpty(imageTaskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Image task created but task_id is missing." }
                    };
                }

                int cols = profileResult.SliceColumns > 0 ? profileResult.SliceColumns : 4;
                int rows = profileResult.SliceRows > 0 ? profileResult.SliceRows : 4;
                float tolerance = parameters?["chroma_tolerance"] != null ? Mathf.Clamp(parameters["chroma_tolerance"].ToObject<float>(), 0.05f, 0.35f) : 0.16f;
                float feather = parameters?["chroma_feather"] != null ? Mathf.Clamp(parameters["chroma_feather"].ToObject<float>(), 0f, 0.3f) : 0.04f;
                float fps = parameters?["fps"] != null ? Mathf.Clamp(parameters["fps"].ToObject<float>(), 1f, 60f) : 12f;
                bool loop = parameters?["loop"] != null && parameters["loop"].ToObject<bool>();
                if (parameters?["loop"] == null) loop = true;

                string autoTaskId = AutoSpriteSequenceTaskTracker.CreateTask(imageTaskId, wrapped["prompt"]?.ToString());
                var autoTask = AutoSpriteSequenceTaskTracker.GetTask(autoTaskId);
                autoTask.SliceColumns = cols;
                autoTask.SliceRows = rows;
                autoTask.ChromaTolerance = tolerance;
                autoTask.ChromaFeather = feather;
                autoTask.Fps = fps;
                autoTask.Loop = loop;
                autoTask.Status = "generating";
                AutoSpriteSequenceTaskTracker.Save(autoTask);

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "submission_success", true },
                    { "task_id", autoTaskId },
                    { "image_task_id", imageTaskId },
                    { "status", "submitted" },
                    { "message", "Auto sprite-sequence task submitted. Image generation is running in background, then post-processing will run automatically. Poll query_2d_sprite_sequence_auto_status." },
                    { "slice_columns", cols },
                    { "slice_rows", rows },
                    { "chroma_tolerance", tolerance },
                    { "chroma_feather", feather },
                    { "fps", fps },
                    { "loop", loop },
                    { "fixed_grid", $"{cols}x{rows}" },
                    { "total_frames", cols * rows },
                    { "estimated_wait_seconds", 90 },
                    { "notification_mode",  "bg_task_done" },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(imageDict.TryGetValue("backend_task_id", out var btid) ? btid?.ToString() : null) }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in generate_2d_sprite_sequence_auto: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error submitting auto sprite-sequence task: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("query_2d_sprite_sequence_auto_status",
            "Query auto 2D sprite-sequence task status. " +
            "Status values: submitted, generating, postprocessing, completed, failed, interrupted. " +
            "When image generation completes, this tool automatically performs cutout + slicing + animation creation.")]
        public static object Query2DSpriteSequenceAutoStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters?["task_id"]?.ToString();
                if (string.IsNullOrEmpty(taskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'task_id' is required." }
                    };
                }

                var autoTask = AutoSpriteSequenceTaskTracker.GetTask(taskId);
                if (autoTask == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Auto task '{taskId}' not found." }
                    };
                }

                // Keep a live link to underlying image task for preview purposes
                var imageTaskForPreview = ImageTaskTracker.GetTask(autoTask.ImageTaskId);

                if (!autoTask.PostProcessDone && autoTask.Status != "failed")
                {
                    var imageTask = imageTaskForPreview;
                    if (imageTask == null)
                    {
                        autoTask.Status = "interrupted";
                        autoTask.Error = $"Image task '{autoTask.ImageTaskId}' not found.";
                        autoTask.EndTime = DateTime.Now;
                        AutoSpriteSequenceTaskTracker.Save(autoTask);
                    }
                    else if (imageTask.Status == "failed" || imageTask.Status == "interrupted")
                    {
                        autoTask.Status = imageTask.Status == "interrupted" ? "interrupted" : "failed";
                        autoTask.Error = imageTask.ErrorMessage ?? $"Image task status: {imageTask.Status}";
                        autoTask.EndTime = DateTime.Now;
                        AutoSpriteSequenceTaskTracker.Save(autoTask);
                    }
                    else if (imageTask.Status == "completed")
                    {
                        autoTask.Status = "postprocessing";
                        AutoSpriteSequenceTaskTracker.Save(autoTask);
                        RunAutoPostProcess(autoTask, imageTask.ResultPath);
                    }
                    else
                    {
                        autoTask.Status = "generating";
                        AutoSpriteSequenceTaskTracker.Save(autoTask);
                    }
                }

                var result = new Dictionary<string, object>
                {
                    { "success", true },
                    { "task_id", autoTask.TaskId },
                    { "image_task_id", autoTask.ImageTaskId },
                    { "status", autoTask.Status },
                    { "slice_columns", autoTask.SliceColumns },
                    { "slice_rows", autoTask.SliceRows },
                    { "total_frames", autoTask.SliceColumns * autoTask.SliceRows },
                    { "fps", autoTask.Fps },
                    { "loop", autoTask.Loop },
                    { "prompt", autoTask.Prompt ?? "" },
                    { "start_time", autoTask.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };
                if (!string.IsNullOrEmpty(autoTask.ImagePath)) result["image_path"] = autoTask.ImagePath;
                if (!string.IsNullOrEmpty(autoTask.SpritesFolder))
                {
                    result["sprites_folder"] = autoTask.SpritesFolder;
                    // legacy-compat alias (sprite sequence backend uses folder_path)
                    result["folder_path"] = autoTask.SpritesFolder;
                }
                if (!string.IsNullOrEmpty(autoTask.AnimationPath))
                {
                    result["animation_path"] = autoTask.AnimationPath;
                    // legacy-compat alias (sprite sequence backend uses animation_clip_path)
                    result["animation_clip_path"] = autoTask.AnimationPath;
                }
                // Preview: prefer backend-provided preview_url; fallback to fixed URL then local file://.
                if (imageTaskForPreview != null && !string.IsNullOrEmpty(imageTaskForPreview.PreviewUrl))
                {
                    result["preview_url"] = imageTaskForPreview.PreviewUrl;
                }
                else if (imageTaskForPreview != null && !string.IsNullOrEmpty(imageTaskForPreview.BackendTaskId))
                {
                    result["preview_url"] = PreviewUrlHelper.BuildFixedPreviewUrl(imageTaskForPreview.BackendTaskId);
                }
                else if (autoTask.Status == "completed" && !string.IsNullOrEmpty(autoTask.ImagePath))
                {
                    string fileUrl = TryBuildFileUrlFromUnityAssetPath(autoTask.ImagePath);
                    if (!string.IsNullOrEmpty(fileUrl))
                        result["preview_url"] = fileUrl;
                }
                if (!string.IsNullOrEmpty(autoTask.Error)) result["error"] = autoTask.Error;
                if (autoTask.EndTime.HasValue) result["end_time"] = autoTask.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                if (autoTask.Status == "completed")
                    result["result_summary"] = $"Completed. Sprites: {autoTask.SpritesFolder ?? "N/A"}, Animation: {autoTask.AnimationPath ?? "N/A"}.";
                if (autoTask.Status == "interrupted")
                    result["hint"] = "The underlying image task was interrupted. Re-run generate_2d_sprite_sequence_auto.";

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in query_2d_sprite_sequence_auto_status: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error querying auto sprite-sequence task: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("generate_2d_sprite_sequence_router",
            "Route 2D sequence generation request to legacy sprite-sequence tool or auto frontier tool. " +
            "Routing policy: if image_path exists AND animation_type in [idle, frontRun, backRun], route to legacy generate_sprite_sequence; otherwise route to generate_2d_sprite_sequence_auto.")]
        public static object Generate2DSpriteSequenceRouter(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var p = parameters != null ? (JObject)parameters.DeepClone() : new JObject();
                bool hasImage = !string.IsNullOrEmpty(p["image_path"]?.ToString());
                string animType = p["animation_type"]?.ToString();
                bool legacyAnim = !string.IsNullOrEmpty(animType) &&
                                  (string.Equals(animType, "idle", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(animType, "frontRun", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(animType, "backRun", StringComparison.OrdinalIgnoreCase));

                bool useLegacy = hasImage && legacyAnim;
                object result = useLegacy
                    ? GenerateSpriteSequenceTool.GenerateSpriteSequence(p)
                    : Generate2DSpriteSequenceAuto(p);

                if (result is Dictionary<string, object> dict)
                {
                    dict["route"] = useLegacy ? "legacy_sprite_sequence" : "auto_frontier_sequence";
                    dict["router_policy"] = "image_path + {idle|frontRun|backRun} -> legacy, otherwise -> auto";
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in generate_2d_sprite_sequence_router: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Router submit error: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("query_2d_sprite_sequence_router_status",
            "Query status for a task created by generate_2d_sprite_sequence_router. " +
            "If task_id starts with 'sprite_sequence_', route to query_sprite_sequence_status; if starts with 'auto_sprite_seq_', route to query_2d_sprite_sequence_auto_status.")]
        public static object Query2DSpriteSequenceRouterStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters?["task_id"]?.ToString();
                if (string.IsNullOrEmpty(taskId))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'task_id' is required." }
                    };
                }

                object result;
                if (taskId.StartsWith("sprite_sequence_", StringComparison.OrdinalIgnoreCase))
                {
                    result = GenerateSpriteSequenceTool.QuerySpriteSequenceStatus(parameters);
                    if (result is Dictionary<string, object> d1) d1["route"] = "legacy_sprite_sequence";
                }
                else if (taskId.StartsWith("auto_sprite_seq_", StringComparison.OrdinalIgnoreCase))
                {
                    result = Query2DSpriteSequenceAutoStatus(parameters);
                    if (result is Dictionary<string, object> d2) d2["route"] = "auto_frontier_sequence";
                }
                else
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", $"Unknown task_id format: {taskId}" }
                    };
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in query_2d_sprite_sequence_router_status: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Router query error: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("list_2d_sprite_sequence_router_tasks",
            "List both legacy and auto 2D sequence tasks for router workflow.")]
        public static object List2DSpriteSequenceRouterTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var legacyObj = GenerateSpriteSequenceTool.ListSpriteSequenceTasks(new JObject());
                var autoObj = List2DSpriteSequenceAutoTasks(new JObject());

                var merged = new List<Dictionary<string, object>>();
                int legacyCount = 0;
                int autoCount = 0;

                if (legacyObj is Dictionary<string, object> legacyDict && legacyDict.TryGetValue("tasks", out var legacyTasksObj)
                    && legacyTasksObj is List<Dictionary<string, object>> legacyTasks)
                {
                    legacyCount = legacyTasks.Count;
                    for (int i = 0; i < legacyTasks.Count; i++)
                    {
                        legacyTasks[i]["route"] = "legacy_sprite_sequence";
                        merged.Add(legacyTasks[i]);
                    }
                }

                if (autoObj is Dictionary<string, object> autoDict && autoDict.TryGetValue("tasks", out var autoTasksObj)
                    && autoTasksObj is List<Dictionary<string, object>> autoTasks)
                {
                    autoCount = autoTasks.Count;
                    for (int i = 0; i < autoTasks.Count; i++)
                    {
                        autoTasks[i]["route"] = "auto_frontier_sequence";
                        merged.Add(autoTasks[i]);
                    }
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count", merged.Count },
                    { "legacy_count", legacyCount },
                    { "auto_count", autoCount },
                    { "tasks", merged }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in list_2d_sprite_sequence_router_tasks: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Router list error: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("list_2d_sprite_sequence_auto_tasks",
            "List all active and recent auto 2D sprite-sequence tasks in current Unity Editor session.")]
        public static object List2DSpriteSequenceAutoTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                AutoSpriteSequenceTaskTracker.CleanupCompletedTasks();
                var tasks = AutoSpriteSequenceTaskTracker.GetAllTasks();
                var list = new List<Dictionary<string, object>>();
                foreach (var t in tasks)
                {
                    var imageTask = !string.IsNullOrEmpty(t.ImageTaskId) ? ImageTaskTracker.GetTask(t.ImageTaskId) : null;
                    var d = new Dictionary<string, object>
                    {
                        { "task_id", t.TaskId },
                        { "image_task_id", t.ImageTaskId },
                        { "status", t.Status },
                        { "prompt", t.Prompt ?? "" },
                        { "slice_columns", t.SliceColumns },
                        { "slice_rows", t.SliceRows },
                        { "fps", t.Fps },
                        { "loop", t.Loop },
                        { "start_time", t.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };
                    if (!string.IsNullOrEmpty(t.ImagePath)) d["image_path"] = t.ImagePath;
                    if (!string.IsNullOrEmpty(t.SpritesFolder)) d["sprites_folder"] = t.SpritesFolder;
                    if (!string.IsNullOrEmpty(t.AnimationPath)) d["animation_path"] = t.AnimationPath;
                    if (imageTask != null && !string.IsNullOrEmpty(imageTask.PreviewUrl)) d["preview_url"] = imageTask.PreviewUrl;
                    else if (imageTask != null && !string.IsNullOrEmpty(imageTask.BackendTaskId)) d["preview_url"] = PreviewUrlHelper.BuildFixedPreviewUrl(imageTask.BackendTaskId);
                    else if (t.Status == "completed" && !string.IsNullOrEmpty(t.ImagePath))
                    {
                        string fileUrl = TryBuildFileUrlFromUnityAssetPath(t.ImagePath);
                        if (!string.IsNullOrEmpty(fileUrl)) d["preview_url"] = fileUrl;
                    }
                    if (!string.IsNullOrEmpty(t.Error)) d["error"] = t.Error;
                    if (t.EndTime.HasValue) d["end_time"] = t.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    list.Add(d);
                }

                return new Dictionary<string, object>
                {
                    { "success", true },
                    { "count", list.Count },
                    { "tasks", list },
                    { "note", "Tasks are session-local. If Unity fully restarts, auto task list may be cleared." }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateImageTool] Error in list_2d_sprite_sequence_auto_tasks: {e}");
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

        [ExecuteCustomTool.CustomTool("query_image_status",
            "Query the status of an image generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, returns 'image_path' with the Texture2D asset path in the project. " +
            "Status values: 'generating', 'completed', 'failed', 'interrupted'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryImageStatus(JObject parameters)
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

                var task = ImageTaskTracker.GetTask(taskId);

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

                if (!string.IsNullOrEmpty(task.ImagePath))  result["input_image_path"] = task.ImagePath;
                if (!string.IsNullOrEmpty(task.ResultPath)) result["image_path"]        = task.ResultPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
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
                TJLog.LogError($"[GenerateImageTool] Query error: {e}");
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

#if UNITY_EDITOR
        private static string TryBuildFileUrlFromUnityAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;
            try
            {
                // assetPath is typically like "Assets/xxx.png"
                string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
                if (!File.Exists(abs))
                    return null;
                return new Uri(abs).AbsoluteUri; // file:///...
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将用户参考图与 envelope 内 knowledge_refs 指向的本地布局图合并为绝对路径列表（先用户、后 knowledge），供 DynamicGenerator 写入 images 数组。
        /// </summary>
        private static bool TryCollectFrontierMergedImagePaths(
            JObject parameters,
            out List<string> mergedAbsolutePaths,
            out int userReferenceImageCount
        )
        {
            mergedAbsolutePaths = new List<string>();
            userReferenceImageCount = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string singleUser = parameters["image_path"]?.ToString();
            if (!string.IsNullOrEmpty(singleUser))
            {
                string abs = ResolveToAbsolutePath(singleUser);
                if (!string.IsNullOrEmpty(abs) && File.Exists(abs) && seen.Add(abs))
                {
                    mergedAbsolutePaths.Add(abs);
                    userReferenceImageCount++;
                }
            }

            if (parameters["image_paths"] is JArray userArr)
            {
                foreach (var token in userArr)
                {
                    string path = token?.ToString();
                    if (string.IsNullOrEmpty(path))
                        continue;
                    string abs = ResolveToAbsolutePath(path);
                    if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
                        continue;
                    if (!seen.Add(abs))
                        continue;
                    mergedAbsolutePaths.Add(abs);
                    userReferenceImageCount++;
                }
            }

            // 追加 knowledge 本地图（若配置存在且在磁盘可读）
            JArray krefs = null;
            string envRaw = parameters["frontier_sequence_envelope_raw"]?.ToString();
            if (!string.IsNullOrEmpty(envRaw))
            {
                try
                {
                    var env = JObject.Parse(envRaw);
                    krefs = env["knowledge_refs"] as JArray;
                }
                catch
                {
                    // ignored
                }
            }

            if (krefs != null)
            {
                foreach (var token in krefs)
                {
                    if (!(token is JObject item))
                        continue;
                    string lp = item["local_path"]?.ToString();
                    if (string.IsNullOrEmpty(lp))
                        lp = item["image_path"]?.ToString();
                    if (string.IsNullOrEmpty(lp))
                        lp = item["path"]?.ToString();
                    if (string.IsNullOrEmpty(lp))
                        continue;
                    string abs = ResolveToAbsolutePath(lp);
                    if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
                        continue;
                    if (!seen.Add(abs))
                        continue;
                    mergedAbsolutePaths.Add(abs);
                }
            }

            return mergedAbsolutePaths.Count > 0;
        }

        private static object GenerateImageInternal(JObject parameters, bool enableFrontierSequenceEnvelope)
        {
            string generatorId = parameters["generator_id"]?.ToString() ?? "frontier-game-design";
            string prompt      = parameters["prompt"]?.ToString();
            string userDisplayPrompt = parameters["user_display_prompt"]?.ToString()?.Trim();
            if (string.IsNullOrEmpty(userDisplayPrompt))
                userDisplayPrompt = parameters["prompt"]?.ToString()?.Trim();
            string imagePath   = parameters["image_path"]?.ToString();
            string outputPath  = parameters["output_path"]?.ToString();
            string sessionId   = parameters["session_id"]?.ToString() ?? "";

            List<string> frontierMergedPaths = null;
            int frontierUserImageCount = 0;
            bool hasFrontierMergedImages = false;
            if (enableFrontierSequenceEnvelope)
                hasFrontierMergedImages = TryCollectFrontierMergedImagePaths(
                    parameters,
                    out frontierMergedPaths,
                    out frontierUserImageCount);

            bool hasAnyInput = !string.IsNullOrEmpty(prompt)
                || !string.IsNullOrEmpty(imagePath)
                || (hasFrontierMergedImages && frontierMergedPaths != null && frontierMergedPaths.Count > 0);

            if (!hasAnyInput)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", "Either 'prompt' or 'image_path' must be provided" }
                };
            }

            if (hasFrontierMergedImages && frontierMergedPaths != null && frontierMergedPaths.Count > 0)
                prompt = FrontierSequenceImageOrderHint.AppendToPrompt(prompt ?? "", frontierMergedPaths.Count, frontierUserImageCount);

            int maxLen = GetImagePromptMaxLength(generatorId);
            if (maxLen > 0 && !string.IsNullOrEmpty(prompt) && prompt.Length > maxLen)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error_code", "INVALID_PARAMS" },
                    { "message", $"Prompt length ({prompt.Length}) exceeds the {maxLen} character limit for '{generatorId}'." }
                };
            }

            var config = ConfigManager.GetGeneratorConfig(ConfigType.Image, generatorId);
            if (config == null)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Cannot find image generator config for '{generatorId}'." }
                };
            }

            var generator = new DynamicGenerator(config);
            if (!string.IsNullOrEmpty(prompt))
                generator.SetTextPrompt(prompt);
            if (!string.IsNullOrWhiteSpace(userDisplayPrompt))
                generator.SetHistoryDisplayPrompt(userDisplayPrompt);

            if (hasFrontierMergedImages && frontierMergedPaths != null && frontierMergedPaths.Count > 0)
                generator.SetImagePaths(frontierMergedPaths);
            else if (!string.IsNullOrEmpty(imagePath))
                generator.SetImagePath(imagePath);

            ApplyImageParameters(generator, parameters);

            if (enableFrontierSequenceEnvelope)
            {
                string envelopeRaw = parameters["frontier_sequence_envelope_raw"]?.ToString();
                if (!string.IsNullOrEmpty(envelopeRaw))
                    generator.SetExtraRawJsonField("frontier_sequence_envelope", envelopeRaw);
            }

            var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
            if (!submitResult.Success)
            {
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "error_code", submitResult.ErrorCode },
                    { "message", submitResult.Message }
                };
            }

            string placeholderPath = CreatePlaceholderTexture(outputPath);
            string trackerImagePath = imagePath;
            if (hasFrontierMergedImages && frontierMergedPaths != null && frontierMergedPaths.Count > 0)
                trackerImagePath = frontierMergedPaths[0];
            string capturedInternalBackendTaskId = submitResult.BackendTaskId;
            string taskId = ImageTaskTracker.CreateTask(generatorId, prompt, trackerImagePath, placeholderPath, capturedInternalBackendTaskId);

            var host = new ImagePipelineHost(
                placeholderPath,
                sessionId,
                (savedPath, previewUrl) =>
                {
                    ImageTaskTracker.MarkTaskCompleted(taskId, savedPath, previewUrl);
                    var t = ImageTaskTracker.GetTask(taskId);
                    GenerationNotifier.NotifyCompleted("generate_image", taskId, capturedInternalBackendTaskId,
                        new JObject
                        {
                            ["session_id"]       = sessionId,
                            ["generator_id"]     = generatorId,
                            ["prompt"]           = prompt ?? "",
                            ["image_path"]       = savedPath,
                            ["preview_url"]      = previewUrl ?? "",
                            ["progress"]         = 100,
                            ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                        });
                },
                errorMsg =>
                {
                    ImageTaskTracker.MarkTaskFailed(taskId, errorMsg);
                    GenerationNotifier.NotifyFailed("generate_image", taskId, capturedInternalBackendTaskId, errorMsg,
                        new JObject { ["session_id"] = sessionId, ["generator_id"] = generatorId, ["prompt"] = prompt ?? "" });
                }
            );

            string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);

            var pipeline = new GenerationPipeline(host, ConfigType.Image);
            EditorCoroutineUtility.StartCoroutineOwnerless(
                pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

            bool anyRefImage = !string.IsNullOrEmpty(imagePath)
                || (hasFrontierMergedImages && frontierMergedPaths != null && frontierMergedPaths.Count > 0);
            string mode = anyRefImage ? "image-to-image" : "text-to-image";
            return new Dictionary<string, object>
            {
                { "success",            true },
                { "submission_success", true },
                { "message",
                    "Image generation started. " +
                    "STEP 1 (do now): Apply placeholder_path to the scene if needed. " +
                    "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                    "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~60s) " +
                    "containing ALL generation results (image_path, preview_url, timing, etc.). " +
                    "*** POLLING IS STRICTLY FORBIDDEN — do NOT call query_image_status repeatedly. " +
                    "Only call query_image_status ONCE as a last-resort fallback if no notification arrives. ***" },
                { "task_id",            taskId },
                { "backend_task_id",    submitResult.BackendTaskId },
                { "status",             "submitted" },
                { "generator_id",       generatorId },
                { "mode",               mode },
                { "prompt",             prompt ?? "" },
                { "placeholder_path",   placeholderPath },
                { "estimated_wait_seconds", 60 },
                { "notification_mode",  "bg_task_done" },
                { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) }
            };
        }

        private static string BuildPromptWithInstructionsFallback(string prompt, string instructions)
        {
            const string channelConstraint =
                "Channel constraint: User-uploaded reference images are for character identity and appearance; knowledge reference images are only for grid/slice layout, and must not be used for style or character appearance.";
            if (string.IsNullOrWhiteSpace(instructions))
                return prompt ?? "";
            if (string.IsNullOrWhiteSpace(prompt))
                return instructions + "\n\n" + channelConstraint;
            return instructions + "\n\n" + channelConstraint + "\n\n[User Addition]\n" + prompt.Trim();
        }

        private static JObject BuildFrontierSequenceEnvelopeJObject(
            FrontierSequenceProfileResolveResult profileResult,
            JObject wrapped,
            bool includeUserReferenceRefs
        )
        {
            var envelope = new JObject
            {
                ["instructions"] = profileResult.Instructions,
                ["knowledge_refs"] = profileResult.KnowledgeRefs,
                ["grid"] = $"{profileResult.SliceColumns}x{profileResult.SliceRows}",
                ["reference_channel_policy"] = new JObject
                {
                    ["user_reference_channel"] = "imageUrls",
                    ["knowledge_reference_channel"] = "frontier_sequence_envelope.knowledge_refs",
                    ["identity_priority"] = "user_reference_first",
                    ["knowledge_usage"] = "layout_alignment_only"
                }
            };
            if (includeUserReferenceRefs)
                envelope["user_reference_refs"] = BuildUserReferenceRefsFromParameters(wrapped);
            return envelope;
        }

        /// <summary>
        /// profile 中声明了 knowledge 本地路径时，必须在磁盘可读，否则合并后 images 可能缺少布局参考。
        /// </summary>
        private static string ValidateFrontierKnowledgeLayoutFilesExist(JArray knowledgeRefs)
        {
            if (knowledgeRefs == null || knowledgeRefs.Count == 0)
                return null;

            var missing = new List<string>();
            foreach (var token in knowledgeRefs)
            {
                if (!(token is JObject item))
                    continue;
                string lp = item["local_path"]?.ToString();
                if (string.IsNullOrEmpty(lp))
                    lp = item["image_path"]?.ToString();
                if (string.IsNullOrEmpty(lp))
                    lp = item["path"]?.ToString();
                if (string.IsNullOrEmpty(lp))
                    continue;
                string abs = ResolveToAbsolutePath(lp);
                if (string.IsNullOrEmpty(abs) || !File.Exists(abs))
                    missing.Add(lp);
            }

            if (missing.Count == 0)
                return null;

            return "Cannot read layout reference image from local machine. The generation request will lack layout alignment reference (may cause character position drift per cell).\nPlease check if the following paths exist in the package (e.g. Editor/Config/KnowledgeRefs/walk.png):\n"
                + string.Join("\n", missing);
        }

        private static void RunAutoPostProcess(AutoSpriteSequenceTaskTracker.AutoTaskInfo autoTask, string imageAssetPath)
        {
            if (autoTask == null)
                return;
            if (string.IsNullOrEmpty(imageAssetPath))
            {
                autoTask.Status = "failed";
                autoTask.Error = "Image generation completed but image path is empty.";
                autoTask.EndTime = DateTime.Now;
                AutoSpriteSequenceTaskTracker.Save(autoTask);
                return;
            }

            Texture2D src = null;
            Texture2D cutout = null;
            try
            {
                src = SpriteSequencePostProcessService.LoadReadableTextureFromAssetPath(imageAssetPath);
                if (src == null)
                    throw new InvalidOperationException("Failed to read generated image.");

                cutout = SpriteSequencePostProcessService.BuildGreenScreenCutoutTexture(
                    src,
                    autoTask.ChromaTolerance,
                    autoTask.ChromaFeather
                );

                var sliceResult = SpriteSequencePostProcessService.SliceTextureToSpritesAndAnimation(
                    cutout,
                    imageAssetPath,
                    autoTask.SliceColumns,
                    autoTask.SliceRows,
                    autoTask.Fps,
                    autoTask.Loop
                );

                autoTask.ImagePath = imageAssetPath;
                autoTask.SpritesFolder = sliceResult.OutputDirectory;
                autoTask.AnimationPath = sliceResult.AnimationClipPath;
                autoTask.Status = "completed";
                autoTask.PostProcessDone = true;
                autoTask.EndTime = DateTime.Now;
                AutoSpriteSequenceTaskTracker.Save(autoTask);
            }
            catch (Exception e)
            {
                autoTask.Status = "failed";
                autoTask.Error = $"Post-process failed: {e.Message}";
                autoTask.EndTime = DateTime.Now;
                AutoSpriteSequenceTaskTracker.Save(autoTask);
            }
            finally
            {
                if (src != null) UnityEngine.Object.DestroyImmediate(src);
                if (cutout != null) UnityEngine.Object.DestroyImmediate(cutout);
            }
        }

        private static JArray BuildUserReferenceRefsFromParameters(JObject wrapped)
        {
            var refs = new JArray();
            if (wrapped == null)
                return refs;

            var rawList = new List<string>();
            string single = wrapped["image_path"]?.ToString();
            if (!string.IsNullOrEmpty(single))
                rawList.Add(single);

            if (wrapped["image_paths"] is JArray arr)
            {
                foreach (var token in arr)
                {
                    string p = token?.ToString();
                    if (!string.IsNullOrEmpty(p))
                        rawList.Add(p);
                }
            }

            for (int i = 0; i < rawList.Count; i++)
            {
                string p = rawList[i];
                refs.Add(new JObject
                {
                    ["index"] = i,
                    ["source"] = "user_upload",
                    ["role"] = "identity_primary",
                    ["path"] = p,
                    ["name"] = Path.GetFileName(p)
                });
            }

            return refs;
        }

        private struct FrontierSequenceProfileResolveResult
        {
            public string Instructions;
            public JArray KnowledgeRefs;
            public string Error;
            public int LocalKnowledgeCount;
            public int SliceColumns;
            public int SliceRows;
        }

        private static FrontierSequenceProfileResolveResult ResolveFrontierSequenceProfileAndEnvelope(JObject wrapped, out string appliedProfileId)
        {
            appliedProfileId = null;
            JObject profile = null;

            string requestedProfileId = wrapped["profile_id"]?.ToString();
            string overrideInstructions = wrapped["instructions"]?.ToString();
            if (!string.IsNullOrWhiteSpace(overrideInstructions))
                overrideInstructions = overrideInstructions.Trim();
            else
                overrideInstructions = null;

            var configRoot = LoadFrontierSequenceProfilesConfig();
            string instructions;

            if (overrideInstructions != null)
            {
                instructions = overrideInstructions;
                if (configRoot != null)
                {
                    string effectiveProfileId = string.IsNullOrEmpty(requestedProfileId)
                        ? configRoot["defaultProfileId"]?.ToString()
                        : requestedProfileId;
                    if (!string.IsNullOrEmpty(effectiveProfileId))
                    {
                        profile = GetProfileById(configRoot, effectiveProfileId);
                        if (profile == null && !string.IsNullOrEmpty(requestedProfileId))
                        {
                            return new FrontierSequenceProfileResolveResult
                            {
                                Error =
                                    $"Missing sequence instructions config: profile \"{requestedProfileId}\" not found in FrontierSequenceProfiles.json."
                            };
                        }

                        if (profile != null)
                            appliedProfileId = effectiveProfileId;
                    }
                }
            }
            else
            {
                if (configRoot == null)
                {
                    return new FrontierSequenceProfileResolveResult
                    {
                        Error =
                            "Missing sequence instructions config: Cannot find or read FrontierSequenceProfiles.json. Ensure the package contains Editor/Config/FrontierSequenceProfiles.json."
                    };
                }

                string effectiveProfileId = string.IsNullOrEmpty(requestedProfileId)
                    ? configRoot["defaultProfileId"]?.ToString()
                    : requestedProfileId;
                if (string.IsNullOrEmpty(effectiveProfileId))
                {
                    return new FrontierSequenceProfileResolveResult
                    {
                        Error = "Missing sequence instructions config: defaultProfileId not configured and profile_id not specified in request."
                    };
                }

                profile = GetProfileById(configRoot, effectiveProfileId);
                if (profile == null)
                {
                    return new FrontierSequenceProfileResolveResult
                    {
                        Error =
                            $"Missing sequence instructions config: profile \"{effectiveProfileId}\" not found in FrontierSequenceProfiles.json."
                    };
                }

                appliedProfileId = effectiveProfileId;
                string pinstr = profile["instructions"]?.ToString();
                if (string.IsNullOrWhiteSpace(pinstr))
                {
                    return new FrontierSequenceProfileResolveResult
                    {
                        Error =
                            $"Missing sequence instructions config: profile \"{effectiveProfileId}\" has empty instructions."
                    };
                }

                instructions = pinstr;
            }

            int sliceColumns = 4;
            int sliceRows = 4;
            if (profile != null)
            {
                if (profile["sliceColumns"] != null)
                    sliceColumns = Mathf.Max(1, profile["sliceColumns"].Value<int>());
                if (profile["sliceRows"] != null)
                    sliceRows = Mathf.Max(1, profile["sliceRows"].Value<int>());
            }

            JArray knowledgeRefs = wrapped["knowledge_refs"] as JArray;
            if (knowledgeRefs == null && profile?["knowledge_refs"] is JArray profileRefs)
                knowledgeRefs = (JArray)profileRefs.DeepClone();
            if (knowledgeRefs == null)
                knowledgeRefs = new JArray();

            int localEncodedCount = 0;
            NormalizeLocalKnowledgeRefsInPlace(knowledgeRefs, ref localEncodedCount);

            return new FrontierSequenceProfileResolveResult
            {
                Instructions = instructions,
                KnowledgeRefs = knowledgeRefs,
                LocalKnowledgeCount = localEncodedCount,
                SliceColumns = sliceColumns,
                SliceRows = sliceRows
            };
        }

        private static JObject LoadFrontierSequenceProfilesConfig()
        {
            if (FrontierSequenceProfileConfigLoader.TryLoad(out var root, out _))
                return root;
            return null;
        }

        private static JObject GetProfileById(JObject configRoot, string profileId)
        {
            var profiles = configRoot?["profiles"] as JArray;
            if (profiles == null || string.IsNullOrEmpty(profileId))
                return null;

            foreach (var token in profiles)
            {
                if (!(token is JObject profile))
                    continue;
                if (string.Equals(profile["id"]?.ToString(), profileId, StringComparison.OrdinalIgnoreCase))
                    return profile;
            }
            return null;
        }

        private static void NormalizeLocalKnowledgeRefsInPlace(JArray refs, ref int localEncodedCount)
        {
            if (refs == null || refs.Count == 0)
                return;

            foreach (var token in refs.ToList())
            {
                if (!(token is JObject item))
                    continue;

                string localPath = item["local_path"]?.ToString();
                if (string.IsNullOrEmpty(localPath))
                    localPath = item["image_path"]?.ToString();
                if (string.IsNullOrEmpty(localPath))
                    localPath = item["path"]?.ToString();
                if (string.IsNullOrEmpty(localPath))
                    continue;

                string absPath = ResolveToAbsolutePath(localPath);
                if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
                {
                    TJLog.LogWarning($"[GenerateImageTool] knowledge local file not found: {localPath}");
                    continue;
                }

                try
                {
                    byte[] bytes = File.ReadAllBytes(absPath);
                    item["content_base64"] = Convert.ToBase64String(bytes);
                    item["mime_type"] = GetMimeTypeByPath(absPath);
                    if (item["name"] == null)
                        item["name"] = Path.GetFileName(absPath);
                    item["source"] = "local_file";
                    localEncodedCount++;
                }
                catch (Exception e)
                {
                    TJLog.LogWarning($"[GenerateImageTool] Failed to encode local knowledge file '{localPath}': {e.Message}");
                }
            }
        }

        private static string ResolveToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (Path.IsPathRooted(path))
                return path;

            // Packages/、Assets/、Editor/（包内相对）与 PathUtils 行为一致；避免 Path.GetFullPath 依赖进程 CWD 导致读不到参考图
            return PathUtils.ToAbsoluteAssetPath(path.Replace("\\", "/"));
        }

        private static string GetMimeTypeByPath(string path)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
        }
#endif

        [ExecuteCustomTool.CustomTool("list_image_tasks", "List all active and recent image generation tasks")]
        public static object ListImageTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks    = ImageTaskTracker.GetAllTasks();
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
                    if (!string.IsNullOrEmpty(task.ResultPath))   taskData["image_path"]        = task.ResultPath;
                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
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
                TJLog.LogError($"[GenerateImageTool] List error: {e}");
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
                string uniqueName = "Image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
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

        private static void ApplyImageParameters(DynamicGenerator generator, JObject parameters)
        {
            if (parameters["size"] != null)
                generator.SetParameter("size", parameters["size"].ToString());

            if (parameters["is_segmentation"] != null)
                generator.SetParameter("isSegmentation", parameters["is_segmentation"].ToObject<bool>());

            if (parameters["q_value"] != null)
                generator.SetParameter("qValue", parameters["q_value"].ToObject<int>());

            if (parameters["resize_width"] != null)
                generator.SetParameter("resizeWidth", parameters["resize_width"].ToObject<int>());

            if (parameters["resolution"] != null)
                generator.SetParameter("resolution", parameters["resolution"].ToString());

            if (parameters["aspect_ratio"] != null)
                generator.SetParameter("aspectRatio", parameters["aspect_ratio"].ToString());

            if (parameters["output_format"] != null)
                generator.SetParameter("outputFormat", parameters["output_format"].ToString());

            if (parameters["imageSize"] != null)
                generator.SetParameter("imageSize", parameters["imageSize"].ToString());

            if (parameters["outputFormat"] != null)
                generator.SetParameter("outputFormat", parameters["outputFormat"].ToString());

            if (parameters["quality"] != null)
                generator.SetParameter("quality", parameters["quality"].ToString());
        }

        private static int GetImagePromptMaxLength(string generatorId)
            => TJGeneratorsPromptLimits.GetMaxLength(generatorId);
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// IGenerationPipelineHost implementation for headless image generation via custom tools.
    /// Keeps TextureImporterType.Default (not Sprite) to match the Image window behavior.
    /// </summary>
    internal class ImagePipelineHost : IGenerationPipelineHost
    {
        private readonly string _placeholderPath;
        private readonly TJGeneratorsAssetReference _placeholderRef;
        private readonly string _sessionId;
        private readonly Action<string, string> _onCompleted;
        private readonly Action<string> _onFailed;

        public ImagePipelineHost(string placeholderPath, string sessionId, Action<string, string> onCompleted, Action<string> onFailed)
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
            ErrorDialogUtils.ShowErrorDialog(title, message, (errorMessage) => _onFailed?.Invoke(errorMessage), "GenerateImageTool");
        }

        public string GetTextureSavePath(ModelGeneratorBase generator)
        {
            // 返回 placeholder 路径，pipeline 直接覆盖文件内容，保持 GUID 不变
            return _placeholderPath;
        }

        public void OnTextureSaved(string savePath, ModelGeneratorBase generator)
        {
            TJLog.Log($"[GenerateImageTool] Image saved: {savePath}");

            // 保持 TextureImporterType.Default（与 ImageWindow 行为一致，不改为 Sprite）
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
