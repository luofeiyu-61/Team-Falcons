#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Codely.Newtonsoft.Json;
using Codely.Newtonsoft.Json.Linq;
using TJGenerators.Config;
using TJGenerators.Generators;
using TJGenerators.UI;
using TJGenerators.Utils;
using Unity.UniAsset.Manager.Editor.InternalBridge;
using Unity.EditorCoroutines.Editor;

namespace TJGenerators
{
    /// <summary>
    /// AI参考图生成窗口 - 配置驱动，支持多模型选择、预览、重新生成、多视图模式
    /// </summary>
    public class AIReferenceImageWindow : EditorWindow
    {
        private string _prompt = "";
        private bool _isGenerating;
        private string _statusMessage = "";

        // 模型选择
        private List<ImageGeneratorConfig> _generators;
        private string[] _generatorNames;
        private int _selectedGeneratorIndex;

        // 单图模式
        private Action<string, Texture2D> _onSingleImageGenerated;
        private Texture2D _previewTexture;
        private string _previewFilePath;
        private bool _hasPreview;

        // 多视图模式
        private bool _isMultiViewMode;
        private Action<string[], Texture2D[]> _onMultiViewGenerated;
        private Texture2D[] _multiViewTextures;
        private string[] _multiViewFilePaths;
        private bool _hasMultiViewPreview;
        private int _multiViewProgress;
        /// <summary>本次多视图生成目标张数（1–4），由宿主窗口按 multiViewMinRequired 传入。</summary>
        private int _multiViewTargetCount = 4;

        private static string[] MultiViewOrderedLabels => new[] { TJGeneratorsL10n.L("正面"), TJGeneratorsL10n.L("左侧"), TJGeneratorsL10n.L("背面"), TJGeneratorsL10n.L("右侧") };

        private const float MinPreviewWindowHeight = 450f;
        private const float MinMultiPreviewWindowWidth = 500f;
        private const int ImageGenRequestTimeoutSeconds = 180;
        private const int ImageDownloadTimeoutSeconds = 120;
        private const int RequestBodyLogPreviewMaxChars = 12000;

        private static string StatusNoGeneratorConfig => $"<color=#ff6666>{TJGeneratorsL10n.L("没有可用的图片生成器配置")}</color>";
        private static string StatusAuthFailed =>
            $"<color=#ff6666>{TJGeneratorsL10n.L("登录权限检查失败，请确认编辑器左上角或者Hub内已登录")}</color>";
        private static string StatusMissingReferenceForSideViews =>
            $"<color=#ff6666>{TJGeneratorsL10n.L("缺少可用的参考图（本地文件或图片 URL），无法继续生成侧视/背视。请确认正面已生成成功且接口返回了地址。")}</color>";
        private static string StatusFrontUrlMissing =>
            $"<color=#ff6666>{TJGeneratorsL10n.L("正面图未返回可用的图片 URL，无法与后续视角对齐，已中止。")}</color>";
        private static string StatusViewUrlMissing =>
            $"<color=#ff6666>{TJGeneratorsL10n.L("当前视角未返回图片 URL，多视图链已中止。")}</color>";

        private ImageGeneratorConfig SelectedConfig =>
            _generators != null && _selectedGeneratorIndex < _generators.Count
                ? _generators[_selectedGeneratorIndex] : null;

        private static string RichError(string message) => $"<color=#ff6666>{message}</color>";

        /// <summary>创建窗口；多视图时 <paramref name="multiViewMinRequired"/> 钳制到 1–4。</summary>
        private static AIReferenceImageWindow CreateConfiguredWindow(bool multiView, int multiViewMinRequired = 4)
        {
            var w = CreateInstance<AIReferenceImageWindow>();
            w._isMultiViewMode = multiView;
            if (multiView)
            {
                int n = Mathf.Clamp(multiViewMinRequired, 1, 4);
                w._multiViewTargetCount = n;
                w._multiViewTextures = new Texture2D[n];
                w._multiViewFilePaths = new string[n];
                w.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 参考图生成 (多视图)"));
                w.minSize = new Vector2(500f, 200f);
            }
            else
            {
                w.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 参考图生成"));
                w.minSize = new Vector2(420f, 200f);
            }
            w.LoadGenerators();
            return w;
        }

        /// <summary>单图模式</summary>
        public static void Show(Action<string, Texture2D> onImageGenerated)
        {
            var w = CreateConfiguredWindow(false);
            w._onSingleImageGenerated = onImageGenerated;
            w.ShowUtility();
        }

        /// <summary>多视图：正面→左侧→背面→右侧，张数由 <paramref name="multiViewMinRequired"/> 决定（1–4）。</summary>
        public static void ShowMultiView(Action<string[], Texture2D[]> onMultiViewGenerated, int multiViewMinRequired)
        {
            var w = CreateConfiguredWindow(true, multiViewMinRequired);
            w._onMultiViewGenerated = onMultiViewGenerated;
            w.ShowUtility();
        }

        private void LoadGenerators()
        {
            _generators = ConfigManager.GetReferenceImageGenerators();
            if (_generators.Count == 0)
            {
                TJLog.LogWarning("[AI参考图生成] 没有找到启用的参考图生成器配置");
                _generatorNames = new[] { TJGeneratorsL10n.L("(无可用模型)") };
                return;
            }
            _generatorNames = new string[_generators.Count];
            for (int i = 0; i < _generators.Count; i++)
                _generatorNames[i] = TJGeneratorsL10n.L(_generators[i].displayName);
            _selectedGeneratorIndex = 0;
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), CommonStyles.WindowBackgroundColor);
            GUILayout.BeginVertical(CommonStyles.WindowContentStyle);
            if (_isMultiViewMode ? _hasMultiViewPreview : _hasPreview)
                DrawPreviewUI();
            else
                DrawInputUI();
            GUILayout.EndVertical();
        }

        private void DrawInputUI()
        {
            GUILayout.Label(
                _isMultiViewMode ? TJGeneratorsL10n.L("输入提示词生成多视图参考图") : TJGeneratorsL10n.L("输入提示词生成参考图"),
                CommonStyles.HeaderStyle);
            if (_isMultiViewMode)
            {
                int k = Mathf.Min(_multiViewTargetCount, MultiViewOrderedLabels.Length);
                string orderText = string.Join("、", MultiViewOrderedLabels.Take(k));
                GUILayout.Label(string.Format(TJGeneratorsL10n.L("将按顺序自动生成：{0}（共{1}张）"), orderText, _multiViewTargetCount), CommonStyles.SmallGreyLabelStyle);
            }
            GUILayout.Space(CommonStyles.Space2);

            if (_generators != null && _generators.Count > 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(TJGeneratorsL10n.L("模型:"), CommonStyles.HeaderStyle);
                GUI.enabled = !_isGenerating;
                _selectedGeneratorIndex = EditorGUILayout.Popup(_selectedGeneratorIndex, _generatorNames);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(CommonStyles.Space2);
            }

            GUI.enabled = !_isGenerating;
            _prompt = EditorGUILayout.TextField(_prompt, CommonStyles.TextFieldStyle);
            GUI.enabled = true;
            GUILayout.Space(CommonStyles.Space2);

            bool canGenerate = !_isGenerating && !string.IsNullOrEmpty(_prompt) && SelectedConfig != null;
            if (UIComponents.DrawGenerateButtonWithCost(
                    _isGenerating ? TJGeneratorsL10n.L("生成中...") : TJGeneratorsL10n.L("生成图片"),
                    0,
                    canGenerate,
                    _isGenerating,
                    GUILayout.Height(LeftPanelBottomDock.ActionButtonHeight)))
                StartGeneration();
            GUI.enabled = true;

            DrawStatusFooter(CommonStyles.Space2);
        }

        private void DrawStatusFooter(float spacingBeforeStatus)
        {
            if (string.IsNullOrEmpty(_statusMessage))
                return;
            GUILayout.Space(spacingBeforeStatus);
            GUILayout.Label(_statusMessage, CommonStyles.StatusStyle);
        }

        private void DrawPreviewUI()
        {
            GUILayout.Label(_isMultiViewMode ? TJGeneratorsL10n.L("多视图生成结果预览") : TJGeneratorsL10n.L("生成结果预览"), CommonStyles.HeaderStyle);
            GUILayout.Space(5);

            if (_isMultiViewMode)
            {
                int n = _multiViewTargetCount;
                float tileSize = Mathf.Max(
                    Mathf.Min((position.width - 60) / Mathf.Max(1, n), position.height - 140), 80f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                for (int i = 0; i < n; i++)
                {
                    GUILayout.BeginVertical();
                    Rect r = GUILayoutUtility.GetRect(tileSize, tileSize);
                    if (_multiViewTextures[i] != null)
                        GUI.DrawTexture(r, _multiViewTextures[i], ScaleMode.ScaleToFit);
                    else
                        EditorGUI.DrawRect(r, new Color(0.2f, 0.2f, 0.2f));
                    GUILayout.Label(
                        MultiViewOrderedLabels[i], CommonStyles.SmallGreyCenterLabelStyle, GUILayout.Width(tileSize));
                    GUILayout.EndVertical();
                    if (i < n - 1) GUILayout.Space(10);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else if (_previewTexture != null)
            {
                float maxSize = Mathf.Max(Mathf.Min(position.width - 20, position.height - 120), 100f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                Rect r = GUILayoutUtility.GetRect(maxSize, maxSize);
                GUI.DrawTexture(r, _previewTexture, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10);
            DrawConfirmButtons();
        }

        private void FailGeneration() { _isGenerating = false; Repaint(); }

        private void FailGeneration(string statusMessage)
        {
            _isGenerating = false;
            _statusMessage = statusMessage;
            Repaint();
        }

        private static string GetMultiViewPromptPrefix(ImageGenPromptsConfig p, int i) => i switch
        {
            0 => p?.multiViewFront ?? "",
            1 => p?.multiViewLeft ?? "",
            2 => p?.multiViewBack ?? "",
            3 => p?.multiViewRight ?? "",
            _ => "",
        };

        private bool TryPrepareSideViewReferences(
            ImageGeneratorConfig config,
            int viewIndex,
            string frontReferenceUrl,
            List<string> generatedImageUrls,
            out string[] referenceUrls,
            out string[] referenceLocalPaths
        )
        {
            referenceUrls = null;
            referenceLocalPaths = null;
            if (viewIndex <= 0)
                return true;

            bool repeatFrontAtEnd = viewIndex >= 2;
            referenceUrls = BuildMultiViewReferenceUrls(
                frontReferenceUrl,
                generatedImageUrls,
                repeatFrontAtEnd
            );
            referenceLocalPaths = BuildMultiViewReferenceLocalPaths(
                _multiViewFilePaths,
                viewIndex,
                repeatFrontAtEnd
            );

            bool preferBase64 = !string.IsNullOrEmpty(config.request?.referenceImagesBase64Field);
            bool okLocal = referenceLocalPaths != null && referenceLocalPaths.Length > 0;
            bool okUrl = referenceUrls != null && referenceUrls.Length > 0;

            if ((preferBase64 && !okLocal && !okUrl) || (!preferBase64 && !okUrl))
            {
                FailGeneration(StatusMissingReferenceForSideViews);
                return false;
            }

            return true;
        }

        private void EnsurePreviewWindowSize(bool multiViewLayout)
        {
            var pos = position;
            bool changed = false;
            if (pos.height < MinPreviewWindowHeight) { pos.height = MinPreviewWindowHeight; changed = true; }
            if (multiViewLayout && pos.width < MinMultiPreviewWindowWidth) { pos.width = MinMultiPreviewWindowWidth; changed = true; }
            if (changed) position = pos;
        }

        private static IEnumerable YieldUntilAsyncOperationDone(AsyncOperation op, Action<float> publishElapsed)
        {
            double t0 = EditorApplication.timeSinceStartup;
            while (op != null && !op.isDone)
                yield return null;
            publishElapsed?.Invoke((float)(EditorApplication.timeSinceStartup - t0));
        }

        private void DrawConfirmButtons()
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = !_isGenerating;

            if (GUILayout.Button(TJGeneratorsL10n.L("使用此图片"), CommonStyles.ButtonStyle, GUILayout.Width(130)))
                ScheduleUseImageAndClose();

            if (GUILayout.Button(TJGeneratorsL10n.L("重新生成"), CommonStyles.ButtonStyle, GUILayout.Width(130)))
                ResetPreviewForRegeneration();

            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawStatusFooter(5f);
        }

        private void ScheduleUseImageAndClose()
        {
            if (_isMultiViewMode)
            {
                string[] paths = _multiViewFilePaths;
                Texture2D[] textures = _multiViewTextures;
                Action<string[], Texture2D[]> cb = _onMultiViewGenerated;
                EditorApplication.delayCall += () => cb?.Invoke(paths, textures);
            }
            else
            {
                string path = _previewFilePath;
                Texture2D tex = _previewTexture;
                Action<string, Texture2D> cb = _onSingleImageGenerated;
                EditorApplication.delayCall += () => cb?.Invoke(path, tex);
            }

            EditorApplication.delayCall += Close;
        }

        private void ResetPreviewForRegeneration()
        {
            _hasPreview = false;
            _hasMultiViewPreview = false;
            _previewTexture = null;
            _previewFilePath = null;
            if (_isMultiViewMode)
            {
                _multiViewTextures = new Texture2D[_multiViewTargetCount];
                _multiViewFilePaths = new string[_multiViewTargetCount];
            }
            Repaint();
        }

        private void StartGeneration()
        {
            var config = SelectedConfig;
            if (config == null)
            {
                _statusMessage = StatusNoGeneratorConfig;
                Repaint();
                return;
            }

            _isGenerating = true;
            _statusMessage = _isMultiViewMode
                ? string.Format(TJGeneratorsL10n.L("正在生成多视图图片 (1/{0})..."), _multiViewTargetCount)
                : TJGeneratorsL10n.L("正在生成图片，请稍候...");
            Repaint();

            if (_isMultiViewMode)
                EditorCoroutineUtility.StartCoroutineOwnerless(GenerateMultiViewImages(config));
            else
                EditorCoroutineUtility.StartCoroutineOwnerless(GenerateSingleImage(config));
        }

        private IEnumerator GenerateSingleImage(ImageGeneratorConfig config)
        {
            string prefix = config.systemPrompts?.single ?? "";
            string fullPrompt = prefix + _prompt;
            yield return CallImageGenApi(config, fullPrompt, null, null, (path, tex, imageUrl) =>
            {
                _previewTexture = tex;
                _previewFilePath = path;
                _hasPreview = true;
                _isGenerating = false;
                _statusMessage = "";
                EnsurePreviewWindowSize(false);
                Repaint();
            });
        }

        private static void AppendRepeatedFrontIfNeeded(List<string> list, string front, bool repeatFrontAtEnd, bool allowAppend)
        {
            if (!repeatFrontAtEnd || !allowAppend || list.Count < 2 || list[list.Count - 1] == front)
                return;
            list.Add(front);
        }

        private static string[] BuildMultiViewReferenceUrls(
            string frontRemoteUrl,
            List<string> allPriorRemoteUrls,
            bool repeatFrontAtEnd
        )
        {
            if (allPriorRemoteUrls == null || allPriorRemoteUrls.Count == 0)
                return null;

            var list = new List<string>();

            if (!string.IsNullOrEmpty(frontRemoteUrl))
                list.Add(frontRemoteUrl);

            foreach (var url in allPriorRemoteUrls)
            {
                if (string.IsNullOrEmpty(url) || list.Contains(url))
                    continue;
                list.Add(url);
            }

            if (list.Count == 0)
                return null;

            AppendRepeatedFrontIfNeeded(
                list,
                frontRemoteUrl,
                repeatFrontAtEnd,
                allowAppend: !string.IsNullOrEmpty(frontRemoteUrl));

            return list.ToArray();
        }

        /// <summary>本地路径顺序与 <see cref="BuildMultiViewReferenceUrls"/> 一致（SeeDream images base64）。</summary>
        private static string[] BuildMultiViewReferenceLocalPaths(
            string[] slotPaths,
            int priorViewCount,
            bool repeatFrontAtEnd
        )
        {
            if (slotPaths == null || priorViewCount <= 0)
                return null;
            string front = slotPaths[0];
            if (string.IsNullOrEmpty(front) || !File.Exists(front))
                return null;

            var list = new List<string> { front };

            for (int j = 1; j < priorViewCount; j++)
            {
                if (j >= slotPaths.Length)
                    break;
                string p = slotPaths[j];
                if (string.IsNullOrEmpty(p) || !File.Exists(p))
                    continue;
                if (!list.Contains(p))
                    list.Add(p);
            }

            AppendRepeatedFrontIfNeeded(list, front, repeatFrontAtEnd, allowAppend: File.Exists(front));

            return list.Count > 0 ? list.ToArray() : null;
        }

        private IEnumerator GenerateMultiViewImages(ImageGeneratorConfig config)
        {
            _multiViewProgress = 0;
            ImageGenPromptsConfig prompts = config.systemPrompts;
            // 每次生成返回的远程 URL（顺序与视角一致）；正面 URL 单独保存，避免链式漂移时丢失锚点
            var generatedImageUrls = new List<string>();
            string frontReferenceUrl = null;

            for (int i = 0; i < _multiViewTargetCount; i++)
            {
                int idx = i;
                string prefix = GetMultiViewPromptPrefix(prompts, i);
                _statusMessage =
                    string.Format(TJGeneratorsL10n.L("正在生成多视图图片 ({0}/{1}) - {2}..."), i + 1, _multiViewTargetCount, MultiViewOrderedLabels[i]);
                Repaint();

                string fullPrompt = prefix + _prompt;

                if (!TryPrepareSideViewReferences(
                        config, i, frontReferenceUrl, generatedImageUrls,
                        out string[] referenceUrls, out string[] referenceLocalPaths))
                    yield break;

                bool done = false;
                string resultImageUrl = null;
                yield return CallImageGenApi(
                    config,
                    fullPrompt,
                    referenceUrls,
                    referenceLocalPaths,
                    (path, tex, imageUrl) =>
                    {
                        _multiViewTextures[idx] = tex;
                        _multiViewFilePaths[idx] = path;
                        resultImageUrl = imageUrl;
                        done = true;
                    }
                );

                if (!done)
                {
                    FailGeneration();
                    yield break;
                }

                if (string.IsNullOrEmpty(resultImageUrl))
                {
                    FailGeneration(i == 0 ? StatusFrontUrlMissing : StatusViewUrlMissing);
                    yield break;
                }

                generatedImageUrls.Add(resultImageUrl);
                if (i == 0)
                    frontReferenceUrl = resultImageUrl;

                _multiViewProgress = i + 1;
            }

            _hasMultiViewPreview = true;
            _isGenerating = false;
            _statusMessage = "";
            EnsurePreviewWindowSize(true);
            Repaint();
        }

        private void LogImageGenRequestFailed(UnityWebRequest uwr, float elapsed)
        {
            string suffix = uwr.responseCode == 504
                ? $"HTTP 504（多为网关/反向代理在 upstream 超时，与 Unity 本机 {ImageGenRequestTimeoutSeconds}s 无关）, elapsed={elapsed:F1}s"
                : $"HTTP {uwr.responseCode}, elapsed={elapsed:F1}s, timeout={ImageGenRequestTimeoutSeconds}s";
            TJLog.LogError($"[AI参考图生成] 请求失败: {uwr.error}, {suffix}");
            TJLog.LogError($"[AI参考图生成] 响应体: {uwr.downloadHandler?.text}");
        }

        // referenceImageUrls: 远程；referenceLocalPaths: 本地（SeeDream 等 base64）
        private IEnumerator CallImageGenApi(
            ImageGeneratorConfig config,
            string prompt,
            string[] referenceImageUrls,
            string[] referenceImageLocalPaths,
            Action<string, Texture2D, string> onSuccess
        )
        {
            string apiUrl = ConfigManager.GetApiBaseUrl() + config.endpoint;
            string json = BuildRequestJson(config, prompt, referenceImageUrls, referenceImageLocalPaths);
            TJLog.Log($"[AI生图] 请求URL: {apiUrl}");
            if (json.Length > RequestBodyLogPreviewMaxChars)
                TJLog.Log($"[AI生图] 请求体含大量 base64，已省略预览，length={json.Length}");
            else
                TJLog.Log($"[AI生图] 请求体: {json}");
            byte[] postData = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest uwr = new UnityWebRequest(apiUrl, "POST"))
            {
                uwr.uploadHandler = new UploadHandlerRaw(postData);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");
                // 该接口为耗时任务（endpoint 含 "-async"），实际生成耗时可能超过默认 60s
                // 这里把等待上限提高，避免拿到结果前就被 Unity 本地超时中断。
                uwr.timeout = ImageGenRequestTimeoutSeconds;

                string token = UnityConnectSession.instance.GetAccessToken();
                if (!string.IsNullOrEmpty(token))
                    uwr.SetRequestHeader("Authorization", $"Bearer {token}");
                uwr.SetRequestHeader("source", ConfigManager.GetRequestSource());
                uwr.SetRequestHeader(GenerationRequestOrigin.HeaderName, GenerationRequestOrigin.Ui);
                var pkgVer = GenerationRequestOrigin.GetPackageVersion();
                if (!string.IsNullOrEmpty(pkgVer))
                    uwr.SetRequestHeader(GenerationRequestOrigin.PackageVersionHeaderName, pkgVer);

                // SendWebRequest 必须在每帧轮询 isDone 才能正确统计等待时长；先 yield 再 while(InProgress) 时请求已结束，elapsed 会始终为 0
                var op = uwr.SendWebRequest();
                float elapsed = 0;
                foreach (object step in YieldUntilAsyncOperationDone(op, e => elapsed = e))
                    yield return step;

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    string text = uwr.downloadHandler.text;
                    TJLog.Log($"[AI参考图生成] 响应: {text}");
                    yield return HandleResponse(config, text, onSuccess);
                }
                else
                {
                    LogImageGenRequestFailed(uwr, elapsed);
                    FailGeneration(uwr.responseCode == 403 ? StatusAuthFailed : RichError(string.Format(TJGeneratorsL10n.L("请求失败: {0}"), uwr.error)));
                }
            }
        }

        private string BuildRequestJson(
            ImageGeneratorConfig config,
            string prompt,
            string[] referenceImageUrls = null,
            string[] referenceImageLocalPaths = null
        )
        {
            string promptField = config.request?.promptField ?? "prompt";
            var o = new JObject { [promptField] = prompt };

            ParameterJsonWriter.ApplyFixedFields(o, config.request?.fixedFields);

            AppendReferenceImagesToRequest(o, config, referenceImageUrls, referenceImageLocalPaths);
            return o.ToString(Formatting.None);
        }

        private static void AppendReferenceImagesToRequest(
            JObject o,
            ImageGeneratorConfig config,
            string[] referenceImageUrls,
            string[] referenceImageLocalPaths
        )
        {
            if (TryAppendBase64ReferenceImages(o, config, referenceImageLocalPaths))
                return;
            if (referenceImageUrls == null || referenceImageUrls.Length == 0)
                return;

            string fieldName = config.request?.referenceImagesField ?? "imageUrls";
            var arr = new JArray();
            foreach (string url in referenceImageUrls)
                arr.Add(url);
            o[fieldName] = arr;
        }

        private static bool TryAppendBase64ReferenceImages(
            JObject o, ImageGeneratorConfig config, string[] referenceImageLocalPaths)
        {
            if (string.IsNullOrEmpty(config.request?.referenceImagesBase64Field)
                || referenceImageLocalPaths == null
                || referenceImageLocalPaths.Length == 0)
                return false;

            var arr = new JArray();
            foreach (string p in referenceImageLocalPaths)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p))
                    return false;
                arr.Add(Convert.ToBase64String(File.ReadAllBytes(p)));
            }
            o[config.request.referenceImagesBase64Field] = arr;
            return true;
        }

        private static bool IsConfiguredResponseSuccess(ImageGenResponseConfig resp, string statusValue) =>
            resp?.successValues != null
                ? resp.successValues.Contains(statusValue)
                : statusValue == "success" || statusValue == "completed";

        private IEnumerator HandleResponse(ImageGeneratorConfig config, string responseText, Action<string, Texture2D, string> onSuccess)
        {
            var resp = config.response;
            string statusField = resp?.statusField ?? "status";
            string statusValue = ExtractJsonValue(responseText, statusField);

            if (IsConfiguredResponseSuccess(resp, statusValue))
            {
                string imageUrlPath = resp?.imageUrlPath ?? "output.data.image_urls[0]";
                string imageUrl = ExtractJsonValue(responseText, imageUrlPath);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    yield return DownloadImage(imageUrl, (path, tex) => { onSuccess?.Invoke(path, tex, imageUrl); });
                }
                else
                {
                    TJLog.LogError($"[AI参考图生成] 无法从路径 '{imageUrlPath}' 提取图片URL");
                    FailGeneration(RichError(TJGeneratorsL10n.L("生成失败: 无法从响应中提取图片URL")));
                }
            }
            else
            {
                string errorField = resp?.errorField ?? "error";
                string err = ExtractJsonValue(responseText, errorField);
                if (string.IsNullOrEmpty(err)) err = TJGeneratorsL10n.L("未知错误");
                FailGeneration(RichError(string.Format(TJGeneratorsL10n.L("生成失败: {0}"), err)));
            }
        }

        private IEnumerator DownloadImage(string imageUrl, Action<string, Texture2D> onSuccess)
        {
            using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(imageUrl))
            {
                uwr.timeout = ImageDownloadTimeoutSeconds;
                var op = uwr.SendWebRequest();
                float elapsed = 0;
                foreach (object step in YieldUntilAsyncOperationDone(op, e => elapsed = e))
                    yield return step;

                if (uwr.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(uwr);
                    string tempDir = Path.Combine(Application.temporaryCachePath, "AIImageGen");
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    string filePath = Path.Combine(tempDir,
                        $"aigen_{DateTime.Now:yyyyMMdd_HHmmss}_{UnityEngine.Random.Range(0, 9999)}.png");
                    File.WriteAllBytes(filePath, texture.EncodeToPNG());
                    TJLog.Log($"[AI参考图生成] 图片已保存: {filePath}");
                    onSuccess?.Invoke(filePath, texture);
                }
                else
                {
                    TJLog.LogError($"[AI参考图生成] 图片下载失败: {uwr.error}, HTTP {uwr.responseCode}, elapsed={elapsed:F1}s");
                    FailGeneration(uwr.responseCode == 403 ? StatusAuthFailed : RichError(string.Format(TJGeneratorsL10n.L("图片下载失败: {0}"), uwr.error)));
                }
            }
        }

        private static string ExtractJsonValue(string json, string path)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(path))
                return null;
            try
            {
                var token = JToken.Parse(json).SelectToken(path);
                if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                    return null;
                if (token is JValue jv)
                    return jv.Value == null ? null : jv.Type == JTokenType.String ? (string)jv.Value : jv.ToString();
                return token.ToString(Formatting.None);
            }
            catch (JsonReaderException) { return null; }
        }
    }
}
#endif