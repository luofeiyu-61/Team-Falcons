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
    /// CustomTool for generating sound effects (SFX) using TJGenerators Music pipeline (sound-effect generator).
    /// Supports text-to-audio generation for one-shot sound effects such as gunshots, footsteps, explosions, UI clicks, etc.
    /// Output is an audio asset saved to Assets/TJGenerators/History/.
    /// </summary>
    public static class GenerateSoundEffectTool
    {
        [ExecuteCustomTool.CustomTool("generate_sound_effect",
            "Generate a sound effect (SFX) from a text prompt using AI. " +
            "This tool is for one-shot sound effects ONLY — NOT for background music or looping ambient audio. " +
            "Use for: gunshots, footsteps, explosions, UI clicks, item pickups, environmental sounds, etc. " +
            "Output is an MP3 AudioClip asset saved to Assets/TJGenerators/History/. " +
            "Parameters: prompt (text description of the sound effect, required), " +
            "duration_seconds (optional float, 1-22 seconds, default 5), " +
            "prompt_influence (optional float, 0-1, default 0.5 — how strongly the prompt shapes the result), " +
            "output_format (optional string, e.g. 'mp3_44100_128'; default is server default), " +
            "loop (optional bool, default false — whether to generate a loopable sound effect), " +
            "output_path (optional asset save path). " +
            "IMPORTANT: Generation takes 10-60 seconds. After calling this tool, wait at least 5 seconds " +
            "before the first query_sound_effect_status call, then poll every 5-10 seconds. " +
            "A placeholder_path (MP3) is returned immediately — you can assign it to an AudioSource right away.")]
        public static object GenerateSoundEffect(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateSoundEffectTool] Generating sound effect with parameters: {parameters}");

                string prompt = parameters["prompt"]?.ToString();
                string outputPath = parameters["output_path"]?.ToString();
                string sessionId = parameters["session_id"]?.ToString() ?? "";
                bool playOnAwake = parameters["play_on_awake"] != null ? parameters["play_on_awake"].ToObject<bool>() : false;

                if (string.IsNullOrEmpty(prompt))
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "'prompt' parameter is required" }
                    };
                }

                int maxLen = TJGeneratorsPromptLimits.GetMaxLength("sound-effect");
                if (prompt.Length > maxLen)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error_code", "INVALID_PARAMS" },
                        { "message", $"Prompt length ({prompt.Length}) exceeds the {maxLen} character limit. Please shorten your sound effect description." }
                    };
                }

                // Load sound-effect generator config
                var config = ConfigManager.GetGeneratorConfig(ConfigType.Music, "sound-effect");
                if (config == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "success", false },
                        { "message", "Cannot find generator config for 'sound-effect'. Ensure the TJGenerators package is installed and the Editor has finished compiling." }
                    };
                }

                // Create generator and set inputs
                var generator = new DynamicGenerator(config);
                generator.SetTextPrompt(prompt);

                // Apply optional parameters
                ApplySfxParameters(generator, parameters);

                // 阶段1：同步提交任务到后端，立即获取 backendTaskId 或失败原因
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateSoundEffectTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateSoundEffectTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 提交成功后才创建 placeholder（避免在鉴权失败时留下无用文件）
                var (placeholderPath, audioDownloadPath) = BuildSfxPaths(outputPath);

                // Create tracked task (reuse shared AudioClipTaskTracker)
                string capturedBackendTaskId = submitResult.BackendTaskId;
                string taskId = AudioClipTaskTracker.CreateTask("sound-effect", prompt, placeholderPath, capturedBackendTaskId);

                // Create pipeline host with audio-specific callbacks
                var host = new AudioPipelineHost(placeholderPath, audioDownloadPath, sessionId, isBgm: false, playOnAwake: playOnAwake,
                    (savedPath, previewUrl) =>
                    {
                        AudioClipTaskTracker.MarkCompleted(taskId, savedPath, previewUrl);
                        var t = AudioClipTaskTracker.GetTask(taskId);
                        GenerationNotifier.NotifyCompleted("generate_sound_effect", taskId, capturedBackendTaskId,
                            new JObject
                            {
                                ["session_id"]       = sessionId,
                                ["generator_id"]     = "sound-effect",
                                ["prompt"]           = prompt ?? "",
                                ["audio_path"]       = savedPath ?? "",
                                ["preview_url"]      = previewUrl ?? "",
                                ["progress"]         = 100,
                                ["start_time"]       = t?.StartTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["end_time"]         = t?.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                                ["duration_seconds"] = (t != null && t.EndTime.HasValue) ? (int)(t.EndTime.Value - t.StartTime).TotalSeconds : 0
                            });
                    },
                    errorMsg =>
                    {
                        AudioClipTaskTracker.MarkFailed(taskId, errorMsg);
                        GenerationNotifier.NotifyFailed("generate_sound_effect", taskId, capturedBackendTaskId, errorMsg,
                            new JObject { ["session_id"] = sessionId, ["generator_id"] = "sound-effect", ["prompt"] = prompt ?? "" });
                    });

                // 阶段2：异步轮询（跳过提交）
                var pipeline = new GenerationPipeline(host, ConfigType.Music);
                string historyAssetGuid = CustomToolHistoryBindings.HistoryGuidFromPlaceholderAssetPath(placeholderPath);
                EditorCoroutineUtility.StartCoroutineOwnerless(
                    pipeline.StartFromSubmittedTask(generator, historyAssetGuid, submitResult.BackendTaskId));

                TJLog.Log($"[GenerateSoundEffectTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}, placeholder: {placeholderPath}, download: {audioDownloadPath}");

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Sound effect generation started. " +
                        "STEP 1 (do now): Note the placeholder_path — a silent placeholder is available immediately. " +
                        "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                        "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~30s) " +
                        "containing ALL generation results (audio_path, preview_url, timing, etc.). " +
                        "*** POLLING IS STRICTLY FORBIDDEN — do NOT call query_sound_effect_status repeatedly. " +
                        "Only call query_sound_effect_status ONCE as a last-resort fallback if no notification arrives. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       "sound-effect" },
                    { "prompt",             prompt },
                    { "placeholder_path",   placeholderPath },
                    { "estimated_wait_seconds", 30 },
                    { "notification_mode",  "bg_task_done" },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateSoundEffectTool] Error: {e}");
                return new Dictionary<string, object>
                {
                    { "success", false },
                    { "message", $"Error generating sound effect: {e.Message}" }
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

        [ExecuteCustomTool.CustomTool("query_sound_effect_status",
            "Query the status of a sound effect generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "When completed, returns 'audio_path' with the AudioClip asset path in the project. " +
            "Status values: 'generating', 'completed', 'failed'. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QuerySoundEffectStatus(JObject parameters)
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

                var task = AudioClipTaskTracker.GetTask(taskId);

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
                    { "prompt", task.Prompt },
                    { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.AudioPath))
                    result["audio_path"] = task.AudioPath;

                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

                if (!string.IsNullOrEmpty(task.ErrorMessage))
                    result["error"] = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
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
                TJLog.LogError($"[GenerateSoundEffectTool] Query error: {e}");
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

        [ExecuteCustomTool.CustomTool("list_sound_effect_tasks", "List all active and recent sound effect generation tasks")]
        public static object ListSoundEffectTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                // Filter to only show sound-effect tasks from the shared tracker
                var allTasks = AudioClipTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in allTasks)
                {
                    if (task.GeneratorId != "sound-effect")
                        continue;

                    var taskData = new Dictionary<string, object>
                    {
                        { "task_id", task.TaskId },
                        { "generator_id", task.GeneratorId },
                        { "status", task.Status },
                        { "progress", task.Progress },
                        { "prompt", task.Prompt },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.AudioPath))
                        taskData["audio_path"] = task.AudioPath;

                    taskData["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);

                    if (!string.IsNullOrEmpty(task.ErrorMessage))
                        taskData["error"] = task.ErrorMessage;

                    if (task.EndTime.HasValue)
                        taskData["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

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
                TJLog.LogError($"[GenerateSoundEffectTool] List error: {e}");
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
        private static (string placeholderPath, string downloadPath) BuildSfxPaths(string outputPath)
        {
            // Currently, SFX backend always returns MP3 — use .mp3 for both paths so the pipeline
            // overwrites the placeholder in-place when the download completes.
            string audioPath;
            if (!string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.GetDirectoryName(outputPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    EnsureAssetDatabaseFolder(dir);
                audioPath = AssetDatabase.GenerateUniqueAssetPath(
                    Path.ChangeExtension(outputPath, ".mp3"));
            }
            else
            {
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                    AssetDatabase.CreateFolder("Assets", "TJGenerators");
                if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                    AssetDatabase.CreateFolder("Assets/TJGenerators", "History");
                string uniqueName = "SFX_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                audioPath = AssetDatabase.GenerateUniqueAssetPath(
                    "Assets/TJGenerators/History/" + uniqueName + ".mp3");
            }

            // Create a blank MP3 placeholder so AI Agent can assign it immediately
            CreateBlankMp3Clip(audioPath);

            return (audioPath, audioPath);
        }

        /// <summary>
        /// Creates a minimal valid silent MP3 file at <paramref name="assetPath"/> and imports it.
        /// One MPEG1 Layer3 frame: 44100 Hz, 128 kbps, mono, 417 bytes.
        /// Header: FF FB 90 C4  (sync + MPEG1 + Layer3 + noCRC | 128kbps + 44100Hz + noPad | mono + original)
        /// All side-info and main-data bytes are zero → silent frame.
        /// </summary>
        private static void CreateBlankMp3Clip(string assetPath)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            string dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(absolutePath, CreateShortestValidMp3());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static byte[] CreateShortestValidMp3()
        {
            // MPEG1 Layer3 frame: 44100 Hz, 128 kbps, mono, no padding → 417 bytes
            // Byte 0: 0xFF         sync[11:4]
            // Byte 1: 0xFB = 1111 1011  sync[3:0]=1111 | MPEG1=11 | Layer3=01 | noCRC=1
            // Byte 2: 0x90 = 1001 0000  128kbps=1001 | 44100Hz=00 | noPad=0 | private=0
            // Byte 3: 0xC4 = 1100 0100  mono=11 | modeExt=00 | copyright=0 | original=1 | emphasis=00
            // Bytes 4–20: side info (17 bytes for mono), all zeros → silent granules
            // Bytes 21–416: main data, all zeros → silence
            var frame = new byte[417];
            frame[0] = 0xFF;
            frame[1] = 0xFB;
            frame[2] = 0x90;
            frame[3] = 0xC4;
            // remaining bytes are already zero (silence)
            return frame;
        }

        private static void EnsureAssetDatabaseFolder(string folderPath)
        {
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void ApplySfxParameters(DynamicGenerator generator, JObject parameters)
        {
            if (parameters["duration_seconds"] != null)
                generator.SetParameter("durationSeconds", parameters["duration_seconds"].ToObject<float>());

            if (parameters["prompt_influence"] != null)
                generator.SetParameter("promptInfluence", parameters["prompt_influence"].ToObject<float>());

            if (parameters["output_format"] != null)
            {
                string fmt = parameters["output_format"].ToString();
                if (!string.IsNullOrEmpty(fmt))
                    generator.SetParameter("outputFormat", fmt);
            }

            if (parameters["loop"] != null)
                generator.SetParameter("loop", parameters["loop"].ToObject<bool>());
        }
#endif
    }
}
