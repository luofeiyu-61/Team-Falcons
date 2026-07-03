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
    /// Tracks active animated character generation tasks.
    /// Uses SessionState to persist task info across script recompilation (domain reload).
    /// After domain reload, in-progress tasks are automatically resumed via TJGeneratorsTaskRecovery —
    /// the same mechanism used by the window UI.
    /// </summary>
    public static class AnimatedCharacterTaskTracker
    {
#if UNITY_EDITOR
        private static readonly Dictionary<string, AnimatedCharacterTaskInfo> _activeTasks =
            new Dictionary<string, AnimatedCharacterTaskInfo>();

        private static int _taskIdCounter = 0;

        // SessionState keys — survive domain reload within the same Editor session
        private const string SessionKeyIds   = "TJGen_AnimChar_Ids";
        private const string SessionKeyFmt   = "TJGen_AnimChar_{0}";

        [Serializable]
        private class PersistedTask
        {
            public string taskId;
            public string backendTaskId;       // backend task ID (set after OnCreated)
            public string generatorId;
            public string prompt;
            public string status;
            public int    progress;
            public string prefabPath;
            public string modelPath;
            public string animationPath;
            public string walkingAnimationPath;
            public string runningAnimationPath;
            public string errorMessage;
            public long   startTimeTicks;
            public long   endTimeTicks;   // 0 = not ended
            public string previewUrl;
        }

        public class AnimatedCharacterTaskInfo
        {
            public string   TaskId                { get; set; }
            public string   BackendTaskId         { get; set; }  // backend task ID for recovery matching
            public string   GeneratorId           { get; set; }
            public string   Prompt                { get; set; }
            public string   Status                { get; set; }
            public int      Progress              { get; set; }
            public string   ModelPath             { get; set; }
            public string   PrefabPath            { get; set; }
            public string   AnimationPath         { get; set; }
            public string   WalkingAnimationPath  { get; set; }
            public string   RunningAnimationPath  { get; set; }
            public string   ErrorMessage          { get; set; }
            public string   PreviewUrl            { get; set; }
            public DateTime StartTime             { get; set; }
            public DateTime? EndTime              { get; set; }
        }

        // ── Session persistence helpers ───────────────────────────────────────

        internal static void SaveToSession(AnimatedCharacterTaskInfo info)
        {
            var p = new PersistedTask
            {
                taskId               = info.TaskId,
                backendTaskId        = info.BackendTaskId ?? "",
                generatorId          = info.GeneratorId ?? "",
                prompt               = info.Prompt,
                status               = info.Status,
                progress             = info.Progress,
                prefabPath           = info.PrefabPath,
                modelPath            = info.ModelPath,
                animationPath        = info.AnimationPath,
                walkingAnimationPath = info.WalkingAnimationPath,
                runningAnimationPath = info.RunningAnimationPath,
                errorMessage         = info.ErrorMessage,
                startTimeTicks       = info.StartTime.Ticks,
                endTimeTicks         = info.EndTime?.Ticks ?? 0,
                previewUrl           = info.PreviewUrl ?? ""
            };
            SessionState.SetString(string.Format(SessionKeyFmt, info.TaskId), JsonUtility.ToJson(p));

            // Keep the global ID list up to date
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!ids.Contains(info.TaskId))
                SessionState.SetString(SessionKeyIds, string.IsNullOrEmpty(ids) ? info.TaskId : ids + "|" + info.TaskId);
        }

        private static void RemoveFromSession(string taskId)
        {
            SessionState.EraseString(string.Format(SessionKeyFmt, taskId));
            string ids = SessionState.GetString(SessionKeyIds, "");
            var list  = new List<string>(ids.Split('|'));
            list.Remove(taskId);
            SessionState.SetString(SessionKeyIds, string.Join("|", list));
        }

        /// <summary>
        /// Tries to restore a task from SessionState (called when not found in memory).
        /// If the backend task is still registered in TJGeneratorsTaskRecovery, it is marked
        /// "recovering" (pipeline will resume). Otherwise "interrupted".
        /// </summary>
        private static AnimatedCharacterTaskInfo TryRestoreFromSession(string taskId)
        {
            string json = SessionState.GetString(string.Format(SessionKeyFmt, taskId), "");
            if (string.IsNullOrEmpty(json))
                return null;

            PersistedTask p;
            try { p = JsonUtility.FromJson<PersistedTask>(json); }
            catch { return null; }

            var info = new AnimatedCharacterTaskInfo
            {
                TaskId               = p.taskId,
                BackendTaskId        = p.backendTaskId,
                GeneratorId          = p.generatorId,
                Prompt               = p.prompt,
                Status               = p.status,
                Progress             = p.progress,
                PrefabPath           = p.prefabPath,
                ModelPath            = p.modelPath,
                AnimationPath        = p.animationPath,
                WalkingAnimationPath = p.walkingAnimationPath,
                RunningAnimationPath = p.runningAnimationPath,
                ErrorMessage         = p.errorMessage,
                PreviewUrl           = p.previewUrl,
                StartTime            = new DateTime(p.startTimeTicks),
                EndTime              = p.endTimeTicks > 0 ? (DateTime?)new DateTime(p.endTimeTicks) : null
            };

            // If it was still in-progress, determine recovery state.
            // Also catch "running"/"processing" which are raw backend states stored before the
            // OnProgress normalization fix — treat them as generating.
            if (info.Status == "initializing" || info.Status == "generating" || info.Status == "recovering" ||
                info.Status == "running"       || info.Status == "processing" || info.Status == "pending")
            {
                bool canRecover = !string.IsNullOrEmpty(info.BackendTaskId) &&
                                  TJGeneratorsTaskRecovery.GetAllInterruptedTasks()
                                      .Any(t => t.backendTaskId == info.BackendTaskId);

                if (canRecover)
                {
                    // Recovery pipeline will resume polling automatically via [InitializeOnLoad]
                    info.Status = "recovering";
                }
                else
                {
                    // Backend task not found — truly interrupted, no recovery possible
                    info.Status       = "interrupted";
                    info.ErrorMessage = "Generation was interrupted (domain reload) and the backend task record was lost. Please re-generate.";
                    info.EndTime      = DateTime.Now;
                }
                SaveToSession(info);
            }

            _activeTasks[taskId] = info;
            return info;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public static string CreateTask(string prompt, TJGeneratorsTaskHandle handle, string prefabPath = null, string sessionId = "")
        {
            string taskId = $"animated_character_{++_taskIdCounter}_{DateTime.Now.Ticks}";

            var taskInfo = new AnimatedCharacterTaskInfo
            {
                TaskId      = taskId,
                GeneratorId = "meshy-animation",
                Prompt      = prompt ?? "",
                PrefabPath  = prefabPath ?? "",
                Status      = "initializing",
                Progress    = 0,
                StartTime   = DateTime.Now
            };

            _activeTasks[taskId] = taskInfo;
            SaveToSession(taskInfo);

            handle.OnCreated += (h) =>
            {
                taskInfo.BackendTaskId = h.BackendTaskId;
                taskInfo.Status = "generating";
                SaveToSession(taskInfo);
            };
            handle.OnProgress += (h) =>
            {
                // Normalize raw backend status strings ("running", "processing", "pending", etc.)
                // to our internal "generating" state — only "completed"/"failed" have dedicated callbacks.
                taskInfo.Status   = "generating";
                taskInfo.Progress = h.Progress;
                if (!string.IsNullOrEmpty(h.PreviewUrl))
                    taskInfo.PreviewUrl = h.PreviewUrl;
                SaveToSession(taskInfo);
            };
            handle.OnCompleted += (h) =>
            {
                taskInfo.Status    = "completed";
                taskInfo.Progress  = 100;
                taskInfo.ModelPath = h.ModelPath;
                taskInfo.PreviewUrl = h.PreviewUrl;
                taskInfo.EndTime   = DateTime.Now;

                if (!string.IsNullOrEmpty(h.ModelPath))
                {
                    string dir      = Path.GetDirectoryName(h.ModelPath);
                    string baseName = Path.GetFileNameWithoutExtension(h.ModelPath);
                    taskInfo.AnimationPath        = FindAnimFile(dir, baseName, "_animation");
                    taskInfo.WalkingAnimationPath = FindAnimFile(dir, baseName, "_walking");
                    taskInfo.RunningAnimationPath = FindAnimFile(dir, baseName, "_running");

                    // BindModelToPrefab already replaced the Placeholder child; now ensure
                    // the AnimatorController (auto-created during DownloadAnimationModels) is
                    // assigned to the prefab's root Animator if it wasn't set yet.
                    if (!string.IsNullOrEmpty(taskInfo.PrefabPath))
                        ReplaceAnimatedCharacterModelTool.AssignAnimatorControllerIfMissing(taskInfo.PrefabPath, h.ModelPath);
                }

                SaveToSession(taskInfo);
                GenerationNotifier.NotifyCompleted("generate_animated_character", taskId, taskInfo.BackendTaskId,
                    new JObject
                    {
                        ["session_id"]     = sessionId,
                        ["generator_id"]   = "meshy-animation",
                        ["prompt"]         = taskInfo.Prompt ?? "",
                        ["model_path"]     = h.ModelPath ?? "",
                        ["prefab_path"]    = taskInfo.PrefabPath ?? "",
                        ["preview_url"]    = h.PreviewUrl ?? "",
                        ["progress"]       = 100,
                        ["start_time"]     = taskInfo.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["end_time"]       = taskInfo.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        ["duration_seconds"] = taskInfo.EndTime.HasValue ? (int)(taskInfo.EndTime.Value - taskInfo.StartTime).TotalSeconds : 0
                    });
            };
            handle.OnFailed += (h) =>
            {
                taskInfo.Status       = "failed";
                taskInfo.ErrorMessage = h.ErrorMessage;
                taskInfo.EndTime      = DateTime.Now;
                SaveToSession(taskInfo);
                GenerationNotifier.NotifyFailed("generate_animated_character", taskId, taskInfo.BackendTaskId,
                    h.ErrorMessage,
                    new JObject
                    {
                        ["session_id"]   = sessionId,
                        ["generator_id"] = "meshy-animation",
                        ["prompt"]       = taskInfo.Prompt ?? ""
                    });
            };

            return taskId;
        }

        public static AnimatedCharacterTaskInfo GetTask(string taskId)
        {
            if (_activeTasks.TryGetValue(taskId, out var task))
                return task;

            // Not in memory — try to restore from SessionState (survives domain reload)
            return TryRestoreFromSession(taskId);
        }

        public static List<AnimatedCharacterTaskInfo> GetAllTasks()
        {
            // First, restore any session tasks not yet in memory
            string ids = SessionState.GetString(SessionKeyIds, "");
            if (!string.IsNullOrEmpty(ids))
            {
                foreach (var id in ids.Split('|'))
                {
                    if (!string.IsNullOrEmpty(id) && !_activeTasks.ContainsKey(id))
                        TryRestoreFromSession(id);
                }
            }

            return new List<AnimatedCharacterTaskInfo>(_activeTasks.Values);
        }

        /// <summary>
        /// Finds a tracker task by its backend task ID. Used by recovery host to update status.
        /// </summary>
        public static AnimatedCharacterTaskInfo GetTaskByBackendId(string backendTaskId)
        {
            if (string.IsNullOrEmpty(backendTaskId)) return null;

            // Ensure all session tasks are loaded
            GetAllTasks();

            return _activeTasks.Values.FirstOrDefault(t => t.BackendTaskId == backendTaskId);
        }

        /// <summary>
        /// Creates a tracker entry for a task that was recovered from TJGeneratorsTaskRecovery
        /// but has no SessionState data (e.g. after a full Editor restart).
        /// This allows query_animated_character_status to return meaningful results.
        /// </summary>
        public static AnimatedCharacterTaskInfo CreateRecoveredTask(
            string backendTaskId, string prompt, string prefabPath, long timestampMs)
        {
            // Avoid duplicates
            var existing = GetTaskByBackendId(backendTaskId);
            if (existing != null) return existing;

            string taskId = $"recovered_{backendTaskId}";
            var info = new AnimatedCharacterTaskInfo
            {
                TaskId        = taskId,
                BackendTaskId = backendTaskId,
                Prompt        = prompt ?? "",
                PrefabPath    = prefabPath ?? "",
                Status        = "recovering",
                Progress      = 0,
                StartTime     = timestampMs > 0
                                    ? DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).LocalDateTime
                                    : DateTime.Now
            };

            _activeTasks[taskId] = info;
            SaveToSession(info);
            return info;
        }

        public static void RemoveTask(string taskId)
        {
            _activeTasks.Remove(taskId);
            RemoveFromSession(taskId);
        }

        public static void CleanupCompletedTasks()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _activeTasks)
            {
                if ((kvp.Value.Status == "completed" || kvp.Value.Status == "failed" || kvp.Value.Status == "interrupted") &&
                    kvp.Value.EndTime.HasValue &&
                    (DateTime.Now - kvp.Value.EndTime.Value).TotalMinutes > 60)
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var id in toRemove)
                RemoveTask(id);
        }

        internal static string FindAnimFile(string directory, string baseName, string suffix)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseName))
                return null;

            string[] extensions = { ".fbx" };
            foreach (var ext in extensions)
            {
                string path = Path.Combine(directory, baseName + suffix + ext).Replace("\\", "/");
                if (File.Exists(path))
                    return path;
            }

            try
            {
                string[] files = Directory.GetFiles(directory, baseName + suffix + ".*");
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        string ext = Path.GetExtension(file).ToLower();
                        if (ext == ".fbx")
                            return file.Replace("\\", "/");
                    }
                }
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"[GenerateAnimatedCharacterTool] FindAnimFile error: {e.Message}");
            }

            return null;
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Automatically resumes interrupted meshy-animation tasks after domain reload,
    /// using the same TJGeneratorsTaskRecovery mechanism as the window UI.
    /// </summary>
    [InitializeOnLoad]
    public static class AnimatedCharacterDomainReloadRecovery
    {
        static AnimatedCharacterDomainReloadRecovery()
        {
            // Use a double delayCall so our recovery runs AFTER any EditorWindow's OnEnable
            // delayCall (registered during OnEnable, which fires after [InitializeOnLoad]).
            // This ensures the window's ResumeInterruptedTaskCore → MarkAsRecovering runs first;
            // we then check IsRecovering and skip tasks the window already grabbed.
            EditorApplication.delayCall += () => EditorApplication.delayCall += ResumeInterruptedTasks;
        }

        private static void ResumeInterruptedTasks()
        {
            var allInterrupted = TJGeneratorsTaskRecovery.GetAllInterruptedTasks();
            var meshyTasks = allInterrupted
                .Where(t => t.modelVersion == "meshy-animation" &&
                            !TJGeneratorsTaskRecovery.IsRecovering(t.backendTaskId))
                .ToList();

            if (meshyTasks.Count == 0) return;

            var config = ConfigManager.GetGeneratorConfig(ConfigType.Generator, "meshy-animation");
            if (config == null)
            {
                TJLog.LogWarning("[GenerateAnimatedCharacterTool] Cannot find meshy-animation config for task recovery.");
                return;
            }

            TJLog.Log($"[GenerateAnimatedCharacterTool] Resuming {meshyTasks.Count} interrupted task(s) after domain reload.");

            // Ensure tracker has all session tasks loaded
            AnimatedCharacterTaskTracker.GetAllTasks();

            foreach (var interrupted in meshyTasks)
            {
                TJGeneratorsTaskRecovery.MarkAsRecovering(interrupted.backendTaskId);

                var trackerTask = AnimatedCharacterTaskTracker.GetTaskByBackendId(interrupted.backendTaskId);

                if (trackerTask != null)
                {
                    // Upgrade any in-progress state (including raw backend "running"/"processing")
                    // to "recovering" so query_animated_character_status shows the right status.
                    bool isInProgress = trackerTask.Status == "initializing" ||
                                        trackerTask.Status == "generating"   ||
                                        trackerTask.Status == "running"      ||
                                        trackerTask.Status == "processing"   ||
                                        trackerTask.Status == "pending"      ||
                                        trackerTask.Status == "interrupted";
                    if (isInProgress)
                    {
                        trackerTask.Status = "recovering";
                        AnimatedCharacterTaskTracker.SaveToSession(trackerTask);
                    }
                }
                else
                {
                    // No SessionState data (e.g. full Editor restart). Create a tracker entry so
                    // query_animated_character_status can return meaningful results during recovery.
                    string prefabPath = !string.IsNullOrEmpty(interrupted.targetAssetGuid)
                        ? AssetDatabase.GUIDToAssetPath(interrupted.targetAssetGuid)
                        : "";
                    trackerTask = AnimatedCharacterTaskTracker.CreateRecoveredTask(
                        interrupted.backendTaskId, interrupted.prompt, prefabPath, interrupted.timestamp);
                }

                var generator = new DynamicGenerator(config);
                generator.RestoreFromInterruptedTask(interrupted);

                var targetAsset = !string.IsNullOrEmpty(interrupted.targetAssetGuid)
                    ? TJGeneratorsAssetReference.FromGuid(interrupted.targetAssetGuid)
                    : null;

                var host     = new AnimatedCharacterRecoveryHost(targetAsset, interrupted.backendTaskId, generator);
                var pipeline = new GenerationPipeline(host, ConfigType.Generator);

                TJLog.Log($"[GenerateAnimatedCharacterTool] Resuming backend task: {interrupted.backendTaskId}");
                EditorCoroutineUtility.StartCoroutineOwnerless(pipeline.PollTaskStatus(generator, interrupted.backendTaskId));
            }
        }
    }

    /// <summary>
    /// Headless pipeline host for resuming animated character tasks after domain reload.
    /// Updates AnimatedCharacterTaskTracker on completion/failure.
    /// </summary>
    internal class AnimatedCharacterRecoveryHost : IGenerationPipelineHost
    {
        private readonly TJGeneratorsAssetReference _targetAsset;
        private readonly string _backendTaskId;
        private readonly ModelGeneratorBase _generator;
        private readonly string _sessionId;

        public AnimatedCharacterRecoveryHost(TJGeneratorsAssetReference targetAsset, string backendTaskId, ModelGeneratorBase generator, string sessionId = "")
        {
            _targetAsset   = targetAsset;
            _backendTaskId = backendTaskId;
            _generator     = generator;
            _sessionId     = sessionId;
        }

        public TJGeneratorsAssetReference GetTargetAsset() => _targetAsset;

        public void ShowPreviewModel(string modelPath)
        {
            // Called by CompleteGeneration() — model was downloaded and bound to prefab.
            // Update ALL tracker tasks that share the same backendTaskId or prefab path so that
            // the original animated_character_N_... task is also marked completed, not just the
            // recovered_{backendId} task created after a domain reload.
            string prefabPath = _targetAsset?.GetPath();

            // Collect all tasks to update: by backendId + by prefabPath (covers domain-reload split)
            var tasksToUpdate = new List<AnimatedCharacterTaskTracker.AnimatedCharacterTaskInfo>();
            var byBackend = AnimatedCharacterTaskTracker.GetTaskByBackendId(_backendTaskId);
            if (byBackend != null) tasksToUpdate.Add(byBackend);

            if (!string.IsNullOrEmpty(prefabPath))
            {
                foreach (var t in AnimatedCharacterTaskTracker.GetAllTasks())
                {
                    if (!tasksToUpdate.Contains(t) &&
                        string.Equals(t.PrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase) &&
                        (t.Status == "generating" || t.Status == "recovering" || t.Status == "initializing"))
                    {
                        tasksToUpdate.Add(t);
                    }
                }
            }

            string dir      = string.IsNullOrEmpty(modelPath) ? null : Path.GetDirectoryName(modelPath);
            string baseName = string.IsNullOrEmpty(modelPath) ? null : Path.GetFileNameWithoutExtension(modelPath);
            string animPath    = dir != null ? AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_animation") : null;
            string walkPath    = dir != null ? AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_walking")   : null;
            string runPath     = dir != null ? AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_running")   : null;

            foreach (var trackerTask in tasksToUpdate)
            {
                trackerTask.Status    = "completed";
                trackerTask.Progress  = 100;
                trackerTask.ModelPath = modelPath;
                trackerTask.EndTime   = DateTime.Now;
                if (animPath  != null) trackerTask.AnimationPath        = animPath;
                if (walkPath  != null) trackerTask.WalkingAnimationPath = walkPath;
                if (runPath   != null) trackerTask.RunningAnimationPath = runPath;
                AnimatedCharacterTaskTracker.SaveToSession(trackerTask);
            }

            // Ensure AnimatorController is assigned on the prefab (handles domain-reload recovery path)
            if (!string.IsNullOrEmpty(modelPath) && !string.IsNullOrEmpty(prefabPath))
                ReplaceAnimatedCharacterModelTool.AssignAnimatorControllerIfMissing(prefabPath, modelPath);

            TJLog.Log($"[GenerateAnimatedCharacterTool] Recovered task completed ({tasksToUpdate.Count} task(s) updated): {modelPath}");

            var notifyTask = AnimatedCharacterTaskTracker.GetTaskByBackendId(_backendTaskId);
            if (notifyTask != null)
                GenerationNotifier.NotifyCompleted("generate_animated_character", notifyTask.TaskId, _backendTaskId,
                    new JObject
                    {
                        ["session_id"]     = _sessionId,
                        ["generator_id"]   = notifyTask.GeneratorId ?? "meshy-animation",
                        ["prompt"]         = notifyTask.Prompt ?? "",
                        ["model_path"]     = modelPath ?? "",
                        ["prefab_path"]    = notifyTask.PrefabPath ?? "",
                        ["preview_url"]    = notifyTask.PreviewUrl ?? "",
                        ["progress"]       = 100,
                        ["start_time"]     = notifyTask.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["end_time"]       = notifyTask.EndTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                        ["duration_seconds"] = notifyTask.EndTime.HasValue
                                               ? (int)(notifyTask.EndTime.Value - notifyTask.StartTime).TotalSeconds : 0
                    });
        }

        public void ShowDialog(string title, string message)
        {
            ErrorDialogUtils.ShowErrorDialog(title, message, "AnimatedCharacterRecovery");

            // If it's an error dialog, mark the task as failed
            if (ErrorDialogUtils.IsErrorDialog(title))
            {
                var trackerTask = AnimatedCharacterTaskTracker.GetTaskByBackendId(_backendTaskId);
                if (trackerTask != null)
                {
                    var friendlyError = ErrorDialogUtils.ConvertToUserFriendlyError(title, message);
                    trackerTask.Status       = "failed";
                    trackerTask.ErrorMessage = friendlyError.TechnicalMessage;
                    trackerTask.EndTime      = DateTime.Now;
                    AnimatedCharacterTaskTracker.SaveToSession(trackerTask);
                    GenerationNotifier.NotifyFailed("generate_animated_character", trackerTask.TaskId, _backendTaskId,
                        friendlyError.TechnicalMessage,
                        new JObject
                        {
                            ["session_id"]   = _sessionId,
                            ["generator_id"] = trackerTask.GeneratorId ?? "meshy-animation",
                            ["prompt"]       = trackerTask.Prompt ?? ""
                        });
                }
            }
        }

        public void RefreshHistory()  { }
        public void RefreshUserInfo() { }

        public void Repaint()
        {
            // 同步 generator 的轮询进度到 tracker，使 query_animated_character_status 能反映真实进度
            if (_generator == null) return;
            var trackerTask = AnimatedCharacterTaskTracker.GetTaskByBackendId(_backendTaskId);
            if (trackerTask == null) return;
            bool isActive = trackerTask.Status == "recovering" || trackerTask.Status == "generating";
            if (!isActive) return;
            int progress = _generator.CurrentProgress;
            if (progress > trackerTask.Progress)
            {
                trackerTask.Status   = "generating";
                trackerTask.Progress = progress;
                AnimatedCharacterTaskTracker.SaveToSession(trackerTask);
            }
        }

        public void StartGeneration(ModelGeneratorBase generator) { }

        public string GetTextureSavePath(ModelGeneratorBase generator) => null;
        public void OnTextureSaved(string savePath, ModelGeneratorBase generator) { }
        public string GetAudioSavePath(ModelGeneratorBase generator) => null;
        public void OnAudioSaved(string savePath, ModelGeneratorBase generator) { }
        public string GetVideoSavePath(ModelGeneratorBase generator) => null;
        public void OnVideoSaved(string savePath, ModelGeneratorBase generator) { }
    }
#endif

    /// <summary>
    /// CustomTool for generating rigged humanoid 3D characters with animations using Meshy AI.
    /// Output: rigged humanoid FBX model + custom/walking/running animation clips.
    /// An AnimatorController with Idle/Walk/Run/Action states is auto-created.
    /// </summary>
    public static class GenerateAnimatedCharacterTool
    {
        [ExecuteCustomTool.CustomTool("generate_animated_character",
            "Generate a rigged HUMANOID 3D character with animations from a text prompt using Meshy AI. " +
            "The output is a humanoid character (bipedal, human skeleton structure) — NOT a generic 3D object. " +
            "Produces up to 4 FBX files: " +
            "(1) rigged_model — the main rigged humanoid mesh with skeleton, " +
            "(2) custom_animation — the requested action clip (specified by action_id, default 452=Backflip), " +
            "(3) walking_animation — a walking loop clip, " +
            "(4) running_animation — a running loop clip. " +
            "An AnimatorController is automatically created with Idle/Walk/Run/Action states and Speed (Float) + Action (Trigger) parameters. " +
            "Key parameters: prompt (required, max 600 chars), prefab_output_path (optional), " +
            "force_overwrite (bool, default false — set true to delete existing placeholder prefab and re-generate), " +
            "action_id (int, animation action to generate, default 452=Backflip), " +
            "target_polycount (int, default 15000), pose_mode ('t-pose'|'a-pose'|''), " +
            "height_meters (float, default 1.7), enable_pbr (bool, default true), seed (int, default 0=random). " +
            "Generation takes 3-10 minutes. After calling this tool, wait at least 5 seconds " +
            "before the first query_animated_character_status call, then poll every 10-15 seconds. " +
            "NOTE: If a domain reload (script compilation) occurs mid-generation, the task is automatically " +
            "resumed in the background — status will show 'recovering' until polling restarts.")]
        public static object GenerateCharacter(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                TJLog.Log($"[GenerateAnimatedCharacterTool] Generating with parameters: {parameters}");

                string prompt          = parameters["prompt"]?.ToString();
                string prefabOutputPath = parameters["prefab_output_path"]?.ToString();
                bool   forceOverwrite  = parameters["force_overwrite"]?.ToObject<bool>() ?? false;
                string sessionId       = parameters["session_id"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(prompt))
                    return Fail("'prompt' parameter is required");

                int maxLen = TJGeneratorsPromptLimits.GetMaxLength("meshy-animation");
                if (prompt.Length > maxLen)
                    return Fail($"Prompt exceeds {maxLen} character limit (current: {prompt.Length} chars)");

                // Resolve prefab path
                if (string.IsNullOrEmpty(prefabOutputPath))
                {
                    prefabOutputPath = "Assets/TJGenerators/History/AnimatedCharacter.prefab";
                    string defaultDir = Path.GetDirectoryName(prefabOutputPath)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(defaultDir))
                        PathUtils.EnsureAssetFolder(defaultDir);
                    prefabOutputPath = AssetDatabase.GenerateUniqueAssetPath(prefabOutputPath);
                    if (string.IsNullOrEmpty(prefabOutputPath))
                        prefabOutputPath = "Assets/TJGenerators/History/AnimatedCharacter.prefab";
                }
                else
                {
                    prefabOutputPath = Path.ChangeExtension(prefabOutputPath, ".prefab");

                    if (File.Exists(prefabOutputPath))
                    {
                        if (forceOverwrite)
                        {
                            AssetDatabase.DeleteAsset(prefabOutputPath);
                        }
                        else
                        {
                            string existingDir = Path.GetDirectoryName(prefabOutputPath)?.Replace('\\', '/');
                            if (!string.IsNullOrEmpty(existingDir))
                                PathUtils.EnsureAssetFolder(existingDir);
                            prefabOutputPath = AssetDatabase.GenerateUniqueAssetPath(prefabOutputPath);
                            if (string.IsNullOrEmpty(prefabOutputPath))
                                prefabOutputPath = Path.ChangeExtension(parameters["prefab_output_path"]?.ToString(), ".prefab");
                        }
                    }
                }

                var config = ConfigManager.GetGeneratorConfig(ConfigType.Generator, "meshy-animation");
                if (config == null)
                    return Fail("Cannot find generator config for 'meshy-animation'. Ensure cn.tuanjie.ai.generators package is installed.");

                var generator = new DynamicGenerator(config);
                generator.SetTextPrompt(prompt);
                ApplyParameters(generator, parameters);

                // 阶段1：同步提交任务到后端，立即获取 backendTaskId 或失败原因
                var submitResult = TJGeneratorsGenerationService.SubmitTaskSync(generator);
                if (!submitResult.Success)
                {
                    TJLog.LogError($"[GenerateAnimatedCharacterTool] 任务提交失败 [{submitResult.ErrorCode}]: {submitResult.Message}");
                    return new Dictionary<string, object>
                    {
                        { "success",    false },
                        { "error_code", submitResult.ErrorCode },
                        { "message",    submitResult.Message }
                    };
                }

                TJLog.Log($"[GenerateAnimatedCharacterTool] 任务提交成功，backend_task_id={submitResult.BackendTaskId}");

                // 提交成功后才创建占位符 prefab
                string createdPrefabPath = CreateBlankPrefab(prefabOutputPath);
                if (string.IsNullOrEmpty(createdPrefabPath))
                    return Fail($"Failed to create prefab at: {prefabOutputPath}");

                TJGeneratorsGenerationLabel.EnableSessionLabel(
                    TJGeneratorsAssetReference.FromPath(createdPrefabPath), sessionId);

                var context = new TJGeneratorsGenerationContext
                {
                    TargetAsset            = TJGeneratorsAssetReference.FromPath(createdPrefabPath),
                    AutoCreateTargetPrefab = false
                };

                // 阶段2：异步轮询（跳过提交）
                var taskHandle = TJGeneratorsGenerationService.GenerateFromSubmittedTask(
                    generator, context, submitResult.BackendTaskId);
                string taskId  = AnimatedCharacterTaskTracker.CreateTask(prompt, taskHandle, createdPrefabPath, sessionId);

                TJLog.Log($"[GenerateAnimatedCharacterTool] 轮询已启动，task_id={taskId}, backend_task_id={submitResult.BackendTaskId}");

                return new Dictionary<string, object>
                {
                    { "success",            true },
                    { "submission_success", true },
                    { "message",
                        "Humanoid animated character generation started. " +
                        "STEP 1 (do now): Instantiate the prefab at prefab_output_path — it contains a placeholder. " +
                        "STEP 2 (critical): END THIS RESPONSE TURN immediately. " +
                        "STEP 3 (automatic): A <bg_task_done> notification will appear in your next turn (~5 min) " +
                        "containing ALL generation results (model_path, animation_path, prefab_path, timing, etc.). " +
                        "*** POLLING IS STRICTLY FORBIDDEN — do NOT call query_animated_character_status repeatedly. " +
                        "Only call it ONCE as a last-resort fallback if no notification arrives. ***" },
                    { "task_id",            taskId },
                    { "backend_task_id",    submitResult.BackendTaskId },
                    { "status",             "submitted" },
                    { "generator_id",       "meshy-animation" },
                    { "prompt",             prompt },
                    { "prefab_output_path", createdPrefabPath },
                    { "preview_url",        PreviewUrlHelper.BuildFixedPreviewUrl(submitResult.BackendTaskId) },
                    { "estimated_wait_seconds", 300 },
                    { "notification_mode",  "bg_task_done" }
                };
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateAnimatedCharacterTool] Error: {e}");
                return Fail($"Error: {e.Message}");
            }
#else
            return Fail("This tool only works in Unity Editor.");
#endif
        }

        [ExecuteCustomTool.CustomTool("query_animated_character_status",
            "Query the status of a humanoid animated character generation task. Use ONLY as a one-time fallback if no <bg_task_done> notification arrives. " +
            "Status values: 'initializing', 'generating', 'recovering', 'completed', 'failed', 'interrupted'. " +
            "'recovering' means Unity recompiled scripts (domain reload) and the task was automatically resumed. " +
            "'interrupted' means the backend task record was lost and recovery is not possible — re-generate. " +
            "When completed, returns a 'files' array with: path, type (rigged_model / custom_animation / walking_animation / running_animation), description. " +
            "Also returns result_summary. " +
            "WARNING: Do NOT call this tool repeatedly. Polling is forbidden.")]
        public static object QueryStatus(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                string taskId = parameters["task_id"]?.ToString();
                if (string.IsNullOrEmpty(taskId))
                    return Fail("'task_id' parameter is required");

                var task = AnimatedCharacterTaskTracker.GetTask(taskId);
                if (task == null)
                    return Fail($"Task '{taskId}' not found. It may have been cleaned up (tasks expire after 60 min) or Unity was fully restarted.");

                // Opportunistic reconciliation: if backend is near completion, recover final
                // completion state from history when OnCompleted callback was lost.
                if ((task.Status == "generating" || task.Status == "recovering") && task.Progress >= 95)
                    TryUpdateFromHistory(task);

                var result = new Dictionary<string, object>
                {
                    { "success",     true },
                    { "task_id",     task.TaskId },
                    { "status",      task.Status },
                    { "progress",    task.Progress },
                    { "prompt",      task.Prompt },
                    { "start_time",  task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };

                if (!string.IsNullOrEmpty(task.PrefabPath))           result["prefab_path"]            = task.PrefabPath;
                if (!string.IsNullOrEmpty(task.ModelPath))            result["model_path"]             = task.ModelPath;
                if (!string.IsNullOrEmpty(task.AnimationPath))        result["animation_path"]         = task.AnimationPath;
                if (!string.IsNullOrEmpty(task.WalkingAnimationPath)) result["walking_animation_path"] = task.WalkingAnimationPath;
                if (!string.IsNullOrEmpty(task.RunningAnimationPath)) result["running_animation_path"] = task.RunningAnimationPath;
                result["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                if (!string.IsNullOrEmpty(task.ErrorMessage))         result["error"]                  = task.ErrorMessage;

                if (task.EndTime.HasValue)
                {
                    result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                }

                if (task.Status == "completed")
                {
                    var files = BuildFilesArray(task);
                    if (files.Count > 0)
                    {
                        result["files"]          = files;
                        result["result_summary"] = BuildResultSummary(task);
                    }
                }

                if (task.Status == "interrupted")
                {
                    result["hint"] = "Re-generate using generate_animated_character with force_overwrite=true and the same prefab_output_path.";
                }

                if (task.Status == "recovering")
                {
                    // Check if the task was completed by the window's recovery pipeline:
                    // When window pipeline finishes, it removes the task from TJGeneratorsTaskRecovery
                    // but doesn't update our AnimatedCharacterTaskTracker (it uses its own host).
                    // Detect this by checking whether the backendTaskId is still registered.
                    if (!string.IsNullOrEmpty(task.BackendTaskId))
                    {
                        bool stillInRecovery = TJGeneratorsTaskRecovery.GetAllInterruptedTasks()
                            .Any(t => t.backendTaskId == task.BackendTaskId);

                        if (!stillInRecovery)
                        {
                            // Task was removed from recovery → pipeline finished (success or fail).
                            // Try to find the completed model in TJGeneratorsHistoryManager.
                            TryUpdateFromHistory(task);
                            // Re-read status after potential update
                            result["status"]   = task.Status;
                            result["progress"] = task.Progress;
                            if (!string.IsNullOrEmpty(task.ModelPath))            result["model_path"]             = task.ModelPath;
                            if (!string.IsNullOrEmpty(task.AnimationPath))        result["animation_path"]         = task.AnimationPath;
                            if (!string.IsNullOrEmpty(task.WalkingAnimationPath)) result["walking_animation_path"] = task.WalkingAnimationPath;
                            if (!string.IsNullOrEmpty(task.RunningAnimationPath)) result["running_animation_path"] = task.RunningAnimationPath;
                            if (!string.IsNullOrEmpty(task.ErrorMessage))         result["error"]                  = task.ErrorMessage;
                            if (task.EndTime.HasValue)
                            {
                                result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                                result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                            }
                        }
                    }

                    if (task.Status == "recovering")
                        result["hint"] = "Task was interrupted by a domain reload and is now being resumed automatically. Keep polling every 15-20 seconds.";
                }

                // Bug fix: if OnCompleted was lost (e.g. domain reload discarded the closure),
                // the task can get stuck at generating+100%. Fall back to history lookup.
                if (task.Status == "generating" && task.Progress >= 100)
                {
                    TryUpdateFromHistory(task);
                    result["status"]   = task.Status;
                    result["progress"] = task.Progress;
                    if (!string.IsNullOrEmpty(task.ModelPath))            result["model_path"]             = task.ModelPath;
                    if (!string.IsNullOrEmpty(task.AnimationPath))        result["animation_path"]         = task.AnimationPath;
                    if (!string.IsNullOrEmpty(task.WalkingAnimationPath)) result["walking_animation_path"] = task.WalkingAnimationPath;
                    if (!string.IsNullOrEmpty(task.RunningAnimationPath)) result["running_animation_path"] = task.RunningAnimationPath;
                    if (!string.IsNullOrEmpty(task.ErrorMessage))         result["error"]                  = task.ErrorMessage;
                    if (task.EndTime.HasValue)
                    {
                        result["end_time"]         = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                        result["duration_seconds"] = (int)(task.EndTime.Value - task.StartTime).TotalSeconds;
                    }
                }

                if (task.Status == "completed")
                {
                    var files = BuildFilesArray(task);
                    if (files.Count > 0)
                    {
                        result["files"]          = files;
                        result["result_summary"] = BuildResultSummary(task);
                    }
                }

                return result;
            }
            catch (Exception e)
            {
                TJLog.LogError($"[GenerateAnimatedCharacterTool] Query error: {e}");
                return Fail($"Error querying status: {e.Message}");
            }
#else
            return Fail("This tool only works in Unity Editor.");
#endif
        }

        [ExecuteCustomTool.CustomTool("list_animated_character_tasks",
            "List all active and recent humanoid animated character generation tasks. " +
            "Tasks survive script recompilation (domain reload) within the same Editor session — " +
            "in-progress tasks are automatically resumed and show status 'recovering'. " +
            "Tasks with status 'interrupted' lost their backend record and must be re-generated.")]
        public static object ListTasks(JObject parameters)
        {
#if UNITY_EDITOR
            try
            {
                var tasks    = AnimatedCharacterTaskTracker.GetAllTasks();
                var taskList = new List<Dictionary<string, object>>();

                foreach (var task in tasks)
                {
                    var d = new Dictionary<string, object>
                    {
                        { "task_id",    task.TaskId },
                        { "status",     task.Status },
                        { "progress",   task.Progress },
                        { "prompt",     task.Prompt },
                        { "start_time", task.StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                    };

                    if (!string.IsNullOrEmpty(task.PrefabPath))           d["prefab_path"]            = task.PrefabPath;
                    if (!string.IsNullOrEmpty(task.ModelPath))            d["model_path"]             = task.ModelPath;
                    if (!string.IsNullOrEmpty(task.AnimationPath))        d["animation_path"]         = task.AnimationPath;
                    if (!string.IsNullOrEmpty(task.WalkingAnimationPath)) d["walking_animation_path"] = task.WalkingAnimationPath;
                    if (!string.IsNullOrEmpty(task.RunningAnimationPath)) d["running_animation_path"] = task.RunningAnimationPath;
                    d["preview_url"] = PreviewUrlHelper.GetPreviewUrl(task.PreviewUrl, task.BackendTaskId);
                    if (!string.IsNullOrEmpty(task.ErrorMessage))         d["error"]                  = task.ErrorMessage;
                    if (task.EndTime.HasValue) d["end_time"] = task.EndTime.Value.ToString("yyyy-MM-dd HH:mm:ss");

                    if (task.Status == "completed")
                    {
                        var files = BuildFilesArray(task);
                        if (files.Count > 0)
                        {
                            d["files"]          = files;
                            d["result_summary"] = BuildResultSummary(task);
                        }
                    }

                    taskList.Add(d);
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
                TJLog.LogError($"[GenerateAnimatedCharacterTool] List error: {e}");
                return Fail($"Error listing tasks: {e.Message}");
            }
#else
            return Fail("This tool only works in Unity Editor.");
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// When a task's tracker status is "recovering" but the backendTaskId is no longer in
        /// TJGeneratorsTaskRecovery, the pipeline (window or ours) has already finished.
        /// Look in history for the most recently completed meshy-animation model for this prefab
        /// and update the tracker to reflect the final outcome.
        /// </summary>
        private static void TryUpdateFromHistory(AnimatedCharacterTaskTracker.AnimatedCharacterTaskInfo task)
        {
            string guid = string.IsNullOrEmpty(task.PrefabPath) ? "" : AssetDatabase.AssetPathToGUID(task.PrefabPath);
            var history = !string.IsNullOrEmpty(guid)
                ? TJGeneratorsHistoryManager.LoadHistoryForAsset(guid)
                : new List<TJGeneratorsGenerationHistoryItem>();

            if (history == null || history.Count == 0)
                history = TJGeneratorsHistoryManager.LoadHistory();

            TJGeneratorsGenerationHistoryItem completedItem = null;
            long latestTimestamp = 0;
            string prompt = task.Prompt ?? "";
            long taskStartMs = new DateTimeOffset(task.StartTime).ToUnixTimeMilliseconds();
            long lowerBoundMs = taskStartMs - (10 * 60 * 1000);
            foreach (var h in history)
            {
                if (h == null || h.isGenerating || string.IsNullOrEmpty(h.modelPath))
                    continue;
                if (!IsAnimatedCharacterHistoryModelVersion(h.modelVersion))
                    continue;

                bool guidMatch = !string.IsNullOrEmpty(guid) && (h.assetGuid ?? "") == guid;
                bool promptMatch = !string.IsNullOrEmpty(prompt) &&
                                   string.Equals((h.prompt ?? "").Trim(), prompt.Trim(), StringComparison.OrdinalIgnoreCase);
                bool timeMatch = h.timestamp >= lowerBoundMs;

                if (!(guidMatch || (promptMatch && timeMatch)))
                    continue;

                if (h.timestamp > latestTimestamp)
                {
                    completedItem = h;
                    latestTimestamp = h.timestamp;
                }
            }

            if (completedItem != null)
            {
                task.Status    = "completed";
                task.Progress  = 100;
                task.ModelPath = completedItem.modelPath;
                task.EndTime   = DateTimeOffset.FromUnixTimeMilliseconds(completedItem.timestamp).LocalDateTime;

                string dir      = Path.GetDirectoryName(completedItem.modelPath);
                string baseName = Path.GetFileNameWithoutExtension(completedItem.modelPath);
                task.AnimationPath        = AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_animation");
                task.WalkingAnimationPath = AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_walking");
                task.RunningAnimationPath = AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_running");

                if (!string.IsNullOrEmpty(task.PrefabPath))
                {
                    // Make recovery truly end-to-end: bind model into prefab if Placeholder is still present,
                    // then ensure Animator controller/avatar are assigned on prefab + scene instances.
                    TryBindRecoveredModelToPrefab(task.PrefabPath, completedItem.modelPath,
                        task.AnimationPath, task.WalkingAnimationPath, task.RunningAnimationPath);
                    ReplaceAnimatedCharacterModelTool.AssignAnimatorControllerIfMissing(task.PrefabPath, completedItem.modelPath);
                }

                AnimatedCharacterTaskTracker.SaveToSession(task);
                TJLog.Log($"[GenerateAnimatedCharacterTool] Recovery task completed via history: {completedItem.modelPath}");
            }
            else
            {
                // History may be missing/stale after domain reload. Fall back to the prefab binding:
                // if GeneratedModel is already bound in the target prefab, infer the source model path.
                if (TryUpdateFromPrefabBinding(task))
                {
                    AnimatedCharacterTaskTracker.SaveToSession(task);
                    TJLog.Log($"[GenerateAnimatedCharacterTool] Recovery task completed via prefab binding: {task.ModelPath}");
                    return;
                }

                // Last resort: directly scan the History directory for model files that match the
                // expected naming pattern derived from the prefab name.  This covers the case where
                // a domain reload interrupted the pipeline between download and CompleteGeneration so
                // neither the history record nor the prefab binding are up to date yet.
                if (TryUpdateFromFileScan(task))
                {
                    AnimatedCharacterTaskTracker.SaveToSession(task);
                    TJLog.Log($"[GenerateAnimatedCharacterTool] Recovery task completed via file scan: {task.ModelPath}");
                    return;
                }

                // No completed model found. If enough time has elapsed, mark as failed.
                if ((DateTime.Now - task.StartTime).TotalMinutes > 20)
                {
                    task.Status       = "failed";
                    task.ErrorMessage = "Recovery finished but no completed model was found in history. The generation may have failed.";
                    task.EndTime      = DateTime.Now;
                    AnimatedCharacterTaskTracker.SaveToSession(task);
                    TJLog.LogWarning($"[GenerateAnimatedCharacterTool] Recovery task timed out without result: {task.TaskId}");
                }
                // else: not enough time has passed — keep "recovering" and wait
            }
        }

        private static bool IsAnimatedCharacterHistoryModelVersion(string modelVersion)
        {
            if (string.IsNullOrEmpty(modelVersion)) return false;
            if (modelVersion == "meshy-animation") return true;

            string normalized = modelVersion.ToLowerInvariant();
            return normalized.Contains("meshy") && normalized.Contains("animation");
        }

        private static bool TryUpdateFromPrefabBinding(AnimatedCharacterTaskTracker.AnimatedCharacterTaskInfo task)
        {
            if (task == null || string.IsNullOrEmpty(task.PrefabPath))
                return false;

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(task.PrefabPath);
                if (prefab == null) return false;

                var generatedModel = prefab.transform.Find("GeneratedModel");
                if (generatedModel == null) return false;

                // PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot only works on scene instances,
                // not on prefab assets loaded via LoadAssetAtPath. Use GetCorrespondingObjectFromSource
                // which correctly traverses the prefab-within-prefab nesting for asset objects.
                string modelPath = null;
                var sourceObj = PrefabUtility.GetCorrespondingObjectFromSource(generatedModel.gameObject);
                if (sourceObj != null)
                    modelPath = AssetDatabase.GetAssetPath(sourceObj);

                // Fallback: open prefab in edit scope to get a proper scene-instance context
                if (string.IsNullOrEmpty(modelPath))
                {
                    string prefabAssetPath = task.PrefabPath.Replace("\\", "/");
                    using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabAssetPath))
                    {
                        var gm = scope.prefabContentsRoot.transform.Find("GeneratedModel");
                        if (gm != null)
                        {
                            var nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gm.gameObject);
                            if (nearestRoot != null && nearestRoot != scope.prefabContentsRoot)
                            {
                                var src = PrefabUtility.GetCorrespondingObjectFromSource(nearestRoot);
                                if (src != null)
                                    modelPath = AssetDatabase.GetAssetPath(src);
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(modelPath))
                    return false;

                var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (modelAsset == null)
                    return false;

                task.Status    = "completed";
                task.Progress  = 100;
                task.ModelPath = modelPath;
                task.EndTime   = DateTime.Now;

                string dir      = Path.GetDirectoryName(modelPath);
                string baseName = Path.GetFileNameWithoutExtension(modelPath);
                task.AnimationPath        = AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_animation");
                task.WalkingAnimationPath = AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_walking");
                task.RunningAnimationPath = AnimatedCharacterTaskTracker.FindAnimFile(dir, baseName, "_running");

                ReplaceAnimatedCharacterModelTool.AssignAnimatorControllerIfMissing(task.PrefabPath, modelPath);
                return true;
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"[GenerateAnimatedCharacterTool] TryUpdateFromPrefabBinding error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Scans Assets/TJGenerators/History/{prefabName}/ (including nested <c>01/</c>, <c>02/</c> version folders)
        /// for the newest main mesh; excludes _animation / _walking / _running sidecars.
        /// Fallback when history records and prefab-binding checks fail after domain reload.
        /// </summary>
        private static bool TryUpdateFromFileScan(AnimatedCharacterTaskTracker.AnimatedCharacterTaskInfo task)
        {
            if (string.IsNullOrEmpty(task.PrefabPath))
                return false;

            try
            {
                string prefabName    = Path.GetFileNameWithoutExtension(task.PrefabPath);
                string safeDirName   = PathUtils.SanitizeAssetFolderName(prefabName);
                string absHistoryDir = Path.Combine(Application.dataPath, "TJGenerators", "History");
                string absPrefabDir  = Path.Combine(absHistoryDir, safeDirName);

                if (!Directory.Exists(absPrefabDir))
                    return false;

                static bool IsAnimSidecar(string fileNameWithoutExtension)
                {
                    return fileNameWithoutExtension.EndsWith("_animation", StringComparison.OrdinalIgnoreCase)
                        || fileNameWithoutExtension.EndsWith("_walking", StringComparison.OrdinalIgnoreCase)
                        || fileNameWithoutExtension.EndsWith("_running", StringComparison.OrdinalIgnoreCase);
                }

                string[] meshExtensions = { ".fbx" };
                var modelFiles = Directory.GetFiles(absPrefabDir, "*.*", SearchOption.AllDirectories)
                    .Where(p => meshExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                    .Where(p => !IsAnimSidecar(Path.GetFileNameWithoutExtension(p)))
                    .OrderByDescending(p => File.GetLastWriteTime(p))
                    .ToList();

                string dataPath = Application.dataPath.Replace("\\", "/");

                foreach (var modelFile in modelFiles)
                {
                    string modelUnity = "Assets" + modelFile.Replace("\\", "/").Substring(dataPath.Length);
                    string dirUnity   = Path.GetDirectoryName(modelUnity)?.Replace("\\", "/") ?? "";
                    string baseName   = Path.GetFileNameWithoutExtension(modelFile);

                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(modelUnity) == null)
                    {
                        AssetDatabase.ImportAsset(modelUnity, ImportAssetOptions.ForceSynchronousImport);
                        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(modelUnity) == null)
                            continue;
                    }

                    task.Status    = "completed";
                    task.Progress  = 100;
                    task.ModelPath = modelUnity;
                    task.EndTime   = DateTime.Now;

                    task.AnimationPath        = AnimatedCharacterTaskTracker.FindAnimFile(dirUnity, baseName, "_animation");
                    task.WalkingAnimationPath = AnimatedCharacterTaskTracker.FindAnimFile(dirUnity, baseName, "_walking");
                    task.RunningAnimationPath = AnimatedCharacterTaskTracker.FindAnimFile(dirUnity, baseName, "_running");

                    if (!string.IsNullOrEmpty(task.PrefabPath))
                    {
                        TryBindRecoveredModelToPrefab(task.PrefabPath, modelUnity,
                            task.AnimationPath, task.WalkingAnimationPath, task.RunningAnimationPath);
                        ReplaceAnimatedCharacterModelTool.AssignAnimatorControllerIfMissing(task.PrefabPath, modelUnity);
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"[GenerateAnimatedCharacterTool] TryUpdateFromFileScan error: {e.Message}");
            }

            return false;
        }

        private static void TryBindRecoveredModelToPrefab(
            string prefabPath,
            string modelPath,
            string animationPath,
            string walkingPath,
            string runningPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(modelPath))
                return;

            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) return;

                bool hasPlaceholder = prefab.transform.Find("Placeholder") != null;
                bool hasGeneratedModel = prefab.transform.Find("GeneratedModel") != null;
                if (!hasPlaceholder && hasGeneratedModel) return;

                bool hasAnimFiles = !string.IsNullOrEmpty(animationPath) ||
                                    !string.IsNullOrEmpty(walkingPath)   ||
                                    !string.IsNullOrEmpty(runningPath);

                ReplaceAnimatedCharacterModelTool.ConfigureFbxImport(modelPath, isMainModel: true, hasAnimationFiles: hasAnimFiles);
                if (!string.IsNullOrEmpty(animationPath)) ReplaceAnimatedCharacterModelTool.ConfigureFbxImport(animationPath, isMainModel: false);
                if (!string.IsNullOrEmpty(walkingPath))   ReplaceAnimatedCharacterModelTool.ConfigureFbxImport(walkingPath,   isMainModel: false);
                if (!string.IsNullOrEmpty(runningPath))   ReplaceAnimatedCharacterModelTool.ConfigureFbxImport(runningPath,   isMainModel: false);
                AssetDatabase.Refresh();

                var host = new AnimatedCharacterReplaceHost(prefabPath);
                var pipeline = new GenerationPipeline(host, ConfigType.Generator);
                pipeline.BindModelToPrefab(modelPath, 1f, default);

                TJLog.Log($"[GenerateAnimatedCharacterTool] Re-bound recovered model to prefab: {prefabPath}");
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"[GenerateAnimatedCharacterTool] Failed to bind recovered model to prefab '{prefabPath}': {e.Message}");
            }
        }

        private static List<Dictionary<string, object>> BuildFilesArray(AnimatedCharacterTaskTracker.AnimatedCharacterTaskInfo task)
        {
            var files = new List<Dictionary<string, object>>();

            if (!string.IsNullOrEmpty(task.ModelPath))
                files.Add(new Dictionary<string, object>
                {
                    { "path", task.ModelPath },
                    { "type", "rigged_model" },
                    { "description", "Main rigged humanoid FBX. Import with Animation Type: Humanoid. Use as the character mesh in your prefab." }
                });

            if (!string.IsNullOrEmpty(task.AnimationPath))
                files.Add(new Dictionary<string, object>
                {
                    { "path", task.AnimationPath },
                    { "type", "custom_animation" },
                    { "description", "Custom action clip (action_id). Assigned to the 'Action' state, triggered by the 'Action' trigger parameter." }
                });

            if (!string.IsNullOrEmpty(task.WalkingAnimationPath))
                files.Add(new Dictionary<string, object>
                {
                    { "path", task.WalkingAnimationPath },
                    { "type", "walking_animation" },
                    { "description", "Walking loop clip. Assigned to the 'Walk' state (Speed > 0.1) and also used for the 'Idle' state (first frame). Loops continuously." }
                });

            if (!string.IsNullOrEmpty(task.RunningAnimationPath))
                files.Add(new Dictionary<string, object>
                {
                    { "path", task.RunningAnimationPath },
                    { "type", "running_animation" },
                    { "description", "Running loop clip. Assigned to the 'Run' state (Speed > 0.5). Loops continuously." }
                });

            return files;
        }

        private static string BuildResultSummary(AnimatedCharacterTaskTracker.AnimatedCharacterTaskInfo task)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(task.ModelPath))            parts.Add("rigged model");
            if (!string.IsNullOrEmpty(task.AnimationPath))        parts.Add("custom animation");
            if (!string.IsNullOrEmpty(task.WalkingAnimationPath)) parts.Add("walking animation");
            if (!string.IsNullOrEmpty(task.RunningAnimationPath)) parts.Add("running animation");

            return $"Generation completed: {string.Join(", ", parts)} ({parts.Count} files). " +
                   $"AnimatorController with Idle/Walk/Run/Action states auto-created. " +
                   $"Prefab: {task.PrefabPath ?? "N/A"}.";
        }

        internal static string CreateBlankPrefab(string path)
        {
            path = Path.ChangeExtension(path, ".prefab").Replace("\\", "/");

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            try
            {
                var placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.name = "Placeholder";
                placeholder.transform.SetParent(root.transform);
                placeholder.transform.localPosition = Vector3.zero;
                placeholder.transform.localRotation = Quaternion.identity;
                placeholder.transform.localScale    = Vector3.one;

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(path));

            return path;
        }

        private static void ApplyParameters(DynamicGenerator generator, JObject parameters)
        {
            // Note: art_style is NOT a parameter in the meshy-animation config — removed to prevent
            // silent no-ops where the caller believes it takes effect but it is never sent to the API.

            if (parameters["topology"] != null)
                generator.SetParameter("topology", parameters["topology"].ToString());

            if (parameters["target_polycount"] != null)
                generator.SetParameter("targetPolycount", parameters["target_polycount"].ToObject<int>());

            if (parameters["should_remesh"] != null)
                generator.SetParameter("shouldRemesh", parameters["should_remesh"].ToObject<bool>());

            if (parameters["symmetry_mode"] != null)
                generator.SetParameter("symmetryMode", parameters["symmetry_mode"].ToString());

            if (parameters["pose_mode"] != null)
                generator.SetParameter("poseMode", parameters["pose_mode"].ToString());

            if (parameters["enable_pbr"] != null)
                generator.SetParameter("enablePbr", parameters["enable_pbr"].ToObject<bool>());

            if (parameters["height_meters"] != null)
                generator.SetParameter("heightMeters", parameters["height_meters"].ToObject<float>());

            if (parameters["action_id"] != null)
                generator.SetParameter("actionId", parameters["action_id"].ToObject<int>());

            if (parameters["seed"] != null)
                generator.SetParameter("seed", parameters["seed"].ToObject<int>());
        }

        private static Dictionary<string, object> Fail(string message)
        {
            return new Dictionary<string, object>
            {
                { "success", false },
                { "message", message }
            };
        }
#endif
    }

    /// <summary>
    /// Internal helper methods for animated character model replacement and FBX configuration.
    /// Used by GenerateAnimatedCharacterTool and AnimatedCharacterDomainReloadRecovery.
    /// </summary>
    public static class ReplaceAnimatedCharacterModelTool
    {
#if UNITY_EDITOR
        /// <summary>
        /// Configure a FBX file's ModelImporter so it is imported as a Humanoid rig.
        /// When it is the main model and separate animation files exist, animation import is
        /// disabled on the main model (animations live in the dedicated files).
        /// </summary>
        internal static void ConfigureFbxImport(string path, bool isMainModel, bool hasAnimationFiles = false)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return;
            importer.animationType = ModelImporterAnimationType.Human;
            if (isMainModel && hasAnimationFiles)
                importer.importAnimation = false;
            AssetDatabase.WriteImportSettingsIfDirty(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// Assigns the AnimatorController (named {baseName}_Controller.controller in the model
        /// directory) to the prefab's root Animator, but only when the Animator has no controller.
        /// Also sets Avatar from the model Animator when one is present.
        /// </summary>
        internal static void AssignAnimatorControllerIfMissing(string prefabPath, string modelPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || string.IsNullOrEmpty(modelPath)) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;

            // Find AnimatorController in model directory
            string dir        = Path.GetDirectoryName(modelPath);
            string baseName   = Path.GetFileNameWithoutExtension(modelPath);
            string ctrlPath   = Path.Combine(dir, baseName + "_Controller.controller").Replace("\\", "/");
            var controller    = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
            if (controller == null && !string.IsNullOrEmpty(dir))
            {
                string[] guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { dir.Replace("\\", "/") });
                foreach (var g in guids)
                {
                    string candidatePath = AssetDatabase.GUIDToAssetPath(g);
                    if (!string.IsNullOrEmpty(candidatePath) &&
                        Path.GetFileNameWithoutExtension(candidatePath).Contains(baseName))
                    {
                        controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(candidatePath);
                        if (controller != null)
                        {
                            ctrlPath = candidatePath;
                            break;
                        }
                    }
                }
            }

            var existingAnim  = prefab.GetComponent<Animator>();
            bool hasController = existingAnim != null && existingAnim.runtimeAnimatorController != null;
            bool hasAvatar = existingAnim != null && existingAnim.avatar != null;
            if (controller == null && hasController && hasAvatar) return; // nothing to do

            // Get avatar from the model
            var modelGO    = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var modelAnim  = modelGO?.GetComponent<Animator>();
            var avatar     = modelAnim?.avatar;
            if (avatar == null)
            {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                avatar = subAssets?.OfType<Avatar>().FirstOrDefault(a => a != null && a.isValid);
            }

            string prefabAssetPath = prefabPath.Replace("\\", "/");
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabAssetPath))
            {
                var root     = scope.prefabContentsRoot;
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                if (controller != null && animator.runtimeAnimatorController == null)
                    animator.runtimeAnimatorController = controller;
                if (avatar != null && animator.avatar == null)
                    animator.avatar = avatar;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(prefabAssetPath, ImportAssetOptions.ForceUpdate);

            // Best-effort: keep open scene instances in sync when they have missing refs.
            var sceneAnimators = UnityEngine.Object.FindObjectsOfType<Animator>(true);
            foreach (var anim in sceneAnimators)
            {
                if (anim == null) continue;
                string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(anim.gameObject);
                if (!string.Equals(sourcePath, prefabAssetPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool changed = false;
                if (controller != null && anim.runtimeAnimatorController == null)
                {
                    anim.runtimeAnimatorController = controller;
                    changed = true;
                }
                if (avatar != null && anim.avatar == null)
                {
                    anim.avatar = avatar;
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(anim);
                    TJLog.Log($"[ReplaceAnimatedCharacterModelTool] Synced Animator on scene instance '{anim.gameObject.name}'.");
                }
            }

            TJLog.Log($"[ReplaceAnimatedCharacterModelTool] AnimatorController assigned: {ctrlPath}");
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Lightweight IGenerationPipelineHost for headless model replacement in animated character prefabs.
    /// </summary>
    internal class AnimatedCharacterReplaceHost : IGenerationPipelineHost
    {
        private readonly string _prefabPath;

        public AnimatedCharacterReplaceHost(string prefabPath) => _prefabPath = prefabPath;

        public TJGeneratorsAssetReference GetTargetAsset() => TJGeneratorsAssetReference.FromPath(_prefabPath);

        public void StartGeneration(ModelGeneratorBase generator) { }
        public void RefreshHistory()  { }
        public void RefreshUserInfo() { }
        public void Repaint()         { }
        public void ShowPreviewModel(string assetPath) { }

        public void ShowDialog(string title, string message)
        {
            ErrorDialogUtils.ShowErrorDialog(title, message, "ReplaceAnimatedCharacterModelTool");
        }

        public string GetTextureSavePath(ModelGeneratorBase generator) => null;
        public void OnTextureSaved(string savePath, ModelGeneratorBase generator) { }
        public string GetAudioSavePath(ModelGeneratorBase generator) => null;
        public void OnAudioSaved(string savePath, ModelGeneratorBase generator) { }
        public string GetVideoSavePath(ModelGeneratorBase generator) => null;
        public void OnVideoSaved(string savePath, ModelGeneratorBase generator) { }
    }
#endif
}
