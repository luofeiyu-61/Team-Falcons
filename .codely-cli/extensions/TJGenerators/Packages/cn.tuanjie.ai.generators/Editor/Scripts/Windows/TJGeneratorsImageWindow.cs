#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Codely.Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Unity.EditorCoroutines.Editor;
using TJGenerators.Config;
using TJGenerators.Generators;
using TJGenerators.Pipeline;
using TJGenerators.UI;
using TJGenerators.Utils;

namespace TJGenerators
{
    /// <summary>
    /// TJGenerators 图片生成窗口（文生图 / 图生图）。
    /// </summary>
    public class TJGeneratorsImageWindow : GenerationWindowBase, IGenerationPipelineHost
    {
        // ========== 固定配置 ==========
        protected override ConfigType WindowConfigType => ConfigType.Image;
        protected override string LogTag => "[TJGeneratorsImage]";

        [SerializeField]
        private string textPrompt = "";

        [SerializeField]
        private TJGeneratorsAssetReference targetImageAsset;

        private readonly List<string> referenceImagePaths = new List<string>();
        private readonly List<Texture2D> referenceUploadedImages = new List<Texture2D>();

        private static readonly Dictionary<string, TJGeneratorsImageWindow> imageOpenWindows =
            new Dictionary<string, TJGeneratorsImageWindow>();

        private Texture2D imagePreviewTexture;
        [SerializeField]
        private string forcedGeneratorId;

        /// <summary>outputType 为 image 且配置启用时可选；prompt 经 DynamicRequestJsonBuilder.BuildEnhancedPrompt 拼为前缀</summary>
        private MaterialTemplateOptionConfig selectedPromptTemplate;
        private JObject frontierSequenceProfilesRoot;
        private string frontierSequenceResolvedConfigPath;
        private readonly List<string> frontierSequenceProfileIds = new List<string>();
        private readonly List<string> frontierSequenceProfileNames = new List<string>();
        [SerializeField] private int frontierSequenceProfileIndex;
        [SerializeField] private string frontierSequenceProfileId;

        private const string UnityTerrainHeightmapTemplateId = "unity_terrain_heightmap";

        [SerializeField]
        private bool terrainHeightmapGaussianBlur = true;

        [SerializeField]
        private bool terrainHeightmapMedian3x3 = true;

        [SerializeField]
        [Range(0.5f, 3f)]
        private float terrainHeightmapBlurSigma = 1.2f;

        [SerializeField]
        private bool terrainHeightmapRemapFoldout = true;

        [SerializeField]
        private bool terrainHeightmapPercentileNormalize = true;

        [SerializeField]
        [Range(0f, 0.2f)]
        private float terrainHeightmapPercentileLow = 0.05f;

        [SerializeField]
        [Range(0.8f, 1f)]
        private float terrainHeightmapPercentileHigh = 0.95f;

        [SerializeField]
        [Range(0.35f, 2.5f)]
        private float terrainHeightmapHeightGamma = 1f;

        [SerializeField]
        [Range(0f, 1f)]
        private float terrainHeightmapRemapOutMin = 0.02f;

        [SerializeField]
        [Range(0f, 1f)]
        private float terrainHeightmapRemapOutMax = 0.98f;

        // ========== 序列帧切图/抠图 ==========
        [SerializeField]
        private int spriteSliceColumns = 4;
        [SerializeField]
        private int spriteSliceRows = 4;
        [SerializeField]
        [Range(1f, 60f)]
        private float spriteSliceFps = 12f;
        [SerializeField]
        private bool spriteSliceLoop = true;
        [SerializeField]
        private bool showSpriteCutoutSettings;
        [SerializeField]
        [Range(0.05f, 0.35f)]
        private float chromaKeyTolerance = 0.16f;
        [SerializeField]
        [Range(0f, 0.3f)]
        private float chromaFeather = 0.04f;
        private Texture2D processedPreviewTexture;
        private string processedPreviewSourcePath;
        private bool processedPreviewValid;
        [SerializeField]
        private ImagePreview historyMainPreview = new ImagePreview();

        // ========== 静态入口 ==========
        public static void ShowWindow()
        {
            var rect = GetDefaultMainWindowRect();
            var window = GetWindowWithRect<TJGeneratorsImageWindow>(
                rect,
                utility: false,
                title: TJGeneratorsL10n.L("TJGenerators 图片生成"),
                focus: true
            );
            window.forcedGeneratorId = null;
            window.titleContent = new GUIContent(TJGeneratorsL10n.L("TJGenerators 图片生成"));
            // Handle window reuse: if the window is already alive, OnEnable may not run again.
            // Rebuild generator list for normal image mode immediately.
            window.InitializeGeneratorsFromConfig(ConfigType.Image);
            window.ApplyForcedGeneratorFilterIfNeeded();
            window.LoadFrontierSequenceProfilesIfNeeded();
            window.Repaint();
        }

        public static void ShowFrontierSequenceWindow()
        {
            string title = TJGeneratorsL10n.L("TJGenerators 序列帧（Frontier）");
            var rect = GetDefaultMainWindowRect();
            var window = GetWindowWithRect<TJGeneratorsImageWindow>(
                rect,
                utility: false,
                title: title,
                focus: true
            );
            window.ApplyFrontierSequenceMode();
        }

        private void ApplyFrontierSequenceMode()
        {
            string title = TJGeneratorsL10n.L("TJGenerators 序列帧（Frontier）");
            forcedGeneratorId = "frontier-game-design";
            titleContent = new GUIContent(title);
            InitializeGeneratorsFromConfig(ConfigType.Image);
            ApplyForcedGeneratorFilterIfNeeded();
            LoadFrontierSequenceProfilesIfNeeded();
            ApplySliceSettingsFromSelectedProfile();

            // Frontier 模式下强制刷新历史：确保绑定资产切换/GUID 重写后仍能加载到历史记录
            RefreshHistory();
        }

        public static void OpenForAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                ShowWindow();
                return;
            }

            if (!IsSupportedImageAssetPath(assetPath))
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("TJGenerators 图片生成"),
                    TJGeneratorsL10n.L("仅支持绑定 .jpg / .jpeg / .png 的图片资产。\r\n\r\n建议先创建「生成图片」新资产。"),
                    "[TJGeneratorsImage]"
                );
                return;
            }

            GenerationWindowBase.OpenForAsset(
                assetPath,
                imageOpenWindows,
                "[TJGeneratorsImage]",
                TJGeneratorsL10n.L("TJGenerators 图片 - {0}"),
                () =>
                {
                    var window = CreateInstance<TJGeneratorsImageWindow>();
                    SetDefaultWindowSize(window);
                    return window;
                },
                (w, r) => w.targetImageAsset = r,
                ShowWindow
            );
        }

        /// <summary>
        /// 绑定已有图片资产并进入 Frontier 序列帧模式（菜单「生成序列帧（Frontier）」创建占位后调用）。
        /// </summary>
        public static void OpenForAssetAsFrontierSequence(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                ShowFrontierSequenceWindow();
                return;
            }

            if (!IsSupportedImageAssetPath(assetPath))
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("TJGenerators 序列帧（Frontier）"),
                    TJGeneratorsL10n.L("仅支持绑定 .jpg / .jpeg / .png 的图片资产。\r\n\r\n建议先通过菜单创建新图片资产。"),
                    "[TJGeneratorsImage]"
                );
                return;
            }

            OpenForAsset(assetPath);

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            EditorApplication.delayCall += () =>
            {
                if (string.IsNullOrEmpty(guid))
                    return;
                if (!imageOpenWindows.TryGetValue(guid, out var window) || window == null)
                    return;
                window.ApplyFrontierSequenceMode();
            };
        }

        private static bool IsSupportedImageAssetPath(string assetPath) =>
            assetPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || assetPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        // ========== 生命周期 ==========
        protected override void OnEnable()
        {
            base.OnEnable();
            wantsMouseMove = true;
            InitializeGeneratorsFromConfig(ConfigType.Image);
            ApplyForcedGeneratorFilterIfNeeded();
            EnsureWindowTitle();
            LoadFrontierSequenceProfilesIfNeeded();

            // 延迟加载历史记录，避免 OnEnable 内触发多次重绘
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                    return;
                RefreshHistory();
            };

            // 获取用户积分
            EditorCoroutineUtility.StartCoroutineOwnerless(
                UserInfoHelper.GetUserInfoCoroutine(
                    ConfigManager.GetUserInfoUrl(),
                    OnUserInfoLoaded
                )
            );

            CheckAndRecoverInterruptedTasks();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            wantsMouseMove = false;
            if (targetImageAsset != null && !string.IsNullOrEmpty(targetImageAsset.guid))
            {
                imageOpenWindows.Remove(targetImageAsset.guid);
            }

            imagePreviewTexture = null;
            ClearPreviewCaches();

            foreach (var tex in referenceUploadedImages)
            {
                if (tex != null)
                    DestroyImmediate(tex);
            }
            referenceUploadedImages.Clear();
            referenceImagePaths.Clear();
        }

        // ========== 任务恢复 ==========
        protected override string GetCurrentAssetGuid() => GetCurrentImageAssetGuid();

        protected override void SetHistory(List<TJGeneratorsGenerationHistoryItem> history) =>
            generationHistory = history;

        /// <summary>
        /// Frontier 序列帧模式：历史不依赖占位图 GUID，应显示所有图片生成历史，
        /// 避免新建窗口时占位图 GUID 变化导致历史丢失。
        /// </summary>
        public override void RefreshHistory()
        {
            string oldSelectedPath = null;
            string oldSelectedTaskId = null;
            if (generationHistory != null && selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count)
            {
                var item = generationHistory[selectedHistoryIndex];
                oldSelectedPath = item.modelPath ?? item.imagePath;
                oldSelectedTaskId = item.taskId;
            }

            generationHistory = TJGeneratorsHistoryManager.LoadHistoryForAsset(GetCurrentImageAssetGuid());

            if (generationHistory.Count > 0)
            {
                int newIndex = -1;
                if (!string.IsNullOrEmpty(oldSelectedPath) || !string.IsNullOrEmpty(oldSelectedTaskId))
                {
                    newIndex = generationHistory.FindIndex(x =>
                        (!string.IsNullOrEmpty(oldSelectedPath) && (x.modelPath == oldSelectedPath || x.imagePath == oldSelectedPath)) ||
                        (!string.IsNullOrEmpty(oldSelectedTaskId) && x.taskId == oldSelectedTaskId));
                }
                selectedHistoryIndex = newIndex >= 0 ? newIndex : 0;
            }
            else
            {
                selectedHistoryIndex = -1;
            }

            Repaint();
        }

        protected override void OnGeneratorRestoredFromTask(ModelGeneratorBase generator)
        {
            base.OnGeneratorRestoredFromTask(generator);
            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("恢复中...");
            Repaint();
        }

        // ========== UI ==========
        private void OnGUI()
        {
            if (Event.current.type == EventType.MouseMove)
                Repaint();
            var splitLayout = UIComponents.CalculateFixedSplitLayout(
                position.width,
                CommonStyles.MainWindowMinSize.y,
                CommonStyles.LeftPanelFixedWidth,
                CommonStyles.MinHistoryPanelWidth,
                CommonStyles.OuterMargin);
            minSize = new Vector2(splitLayout.WindowMinWidth, splitLayout.WindowMinHeight);
            maxSize = new Vector2(10000f, 10000f);
            isVerticalLayout = false;
            currentHistoryPanelWidth = splitLayout.RightPanelWidth;
            _effectiveLeftPanelWidth = CommonStyles.LeftComponentWidth;

            if (_generators == null || _generators.Count == 0)
            {
                EditorGUI.DrawRect(
                    new Rect(0, 0, position.width, position.height),
                    CommonStyles.WindowBackgroundColor
                );
                EditorGUILayout.HelpBox(TJGeneratorsL10n.L("未找到可用的图片生成器，请检查配置"), MessageType.Error);
                return;
            }

            UIComponents.DrawAdaptiveLayoutBackground(
                new Rect(0, 0, position.width, position.height),
                false,
                splitLayout.LeftPanelWidth,
                position.height
            );

            GUILayout.BeginHorizontal();
            DrawLeftPanelColumn(
                splitLayout.LeftPanelWidth,
                ref scrollPosition,
                () =>
                {
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                    GUILayout.BeginVertical(
                        GUILayout.Width(CommonStyles.LeftComponentWidth),
                        GUILayout.MinWidth(CommonStyles.LeftComponentWidth),
                        GUILayout.MaxWidth(CommonStyles.LeftComponentWidth));

                    UIComponents.DrawTargetHeaderComposite(
                        TJGeneratorsL10n.L("目标图片"),
                        DrawTargetHeaderContentRect,
                        SelectTargetImageAsset
                    );
                    GUILayout.Space(CommonStyles.Space2);

                    if (string.IsNullOrEmpty(forcedGeneratorId))
                    {
                        UIComponents.DrawModelSelector(
                            currentSelectedModel?.Name ?? _currentGenerator?.DisplayName ?? TJGeneratorsL10n.L("未选择"),
                            currentSelectedModel,
                            OnModelSelected,
                            ConfigType.Image
                        );
                    }
                    else
                    {
                        UIComponents.DrawFixedModelSelector(
                            currentSelectedModel?.Name ?? _currentGenerator?.DisplayName ?? "Frontier",
                            currentSelectedModel);
                    }

                    GUILayout.Space(CommonStyles.Space3);

                    DrawInputSection();

                    GUILayout.Space(CommonStyles.Space3);

                    DrawConfigurationSection();

                    GUILayout.Space(CommonStyles.Space3);

                    DrawTerrainHeightmapAfterGenerationSection();
                    DrawFrontierSequenceCutoutAfterGenerationSection();

                    GUILayout.Space(CommonStyles.Space3);

                    GUILayout.EndVertical();
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                    GUILayout.EndHorizontal();
                    GUILayout.Space(CommonStyles.LeftContentPadding);
                });

            GUILayout.Space(splitLayout.GapWidth);

            DrawHistoryPanel(currentHistoryPanelWidth);
            GUILayout.EndHorizontal();

            DrawLeftPanelBottomDock(splitLayout.LeftPanelWidth, DrawGenerationSection);
        }

        private void DrawTargetHeaderContentRect(Rect rect)
        {
            if (targetImageAsset != null && targetImageAsset.IsValid())
            {
                string imageName = Path.GetFileNameWithoutExtension(targetImageAsset.GetPath());
                if (GUI.Button(rect, imageName, CommonStyles.TargetPrefabNameStyle))
                    SelectTargetImageAsset();
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
            else
            {
                GUI.Label(rect, TJGeneratorsL10n.L("未绑定（生成时自动创建）"), CommonStyles.ContentStyle);
            }
        }

        private void SelectTargetImageAsset()
        {
            if (targetImageAsset == null || !targetImageAsset.IsValid())
                return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(targetImageAsset.GetPath());
            if (tex != null)
            {
                EditorGUIUtility.PingObject(tex);
                Selection.activeObject = tex;
            }
        }

        private GeneratorConfig GetActiveImageGeneratorConfig()
        {
            return _currentGenerator == null
                ? null
                : GetGeneratorConfigFromIndex(_currentGenerator.GeneratorId);
        }

        /// <summary>
        /// 是否处于「生成序列帧（Frontier）」专用工具：仅当通过 <see cref="ShowFrontierSequenceWindow"/> 打开且锁定 frontier-game-design 时为真。
        /// instruction 模板与网格规格（固定 4×4）只在此模式下生效；普通图片窗口中选 Frontier 模型不走该管线。
        /// </summary>
        private bool IsFrontierSequenceMode()
        {
            return string.Equals(forcedGeneratorId, "frontier-game-design", StringComparison.OrdinalIgnoreCase);
        }

        private bool HasFrontierSequenceProfile()
        {
            return !string.IsNullOrEmpty(frontierSequenceProfileId);
        }

        private void ShowPromptTemplateSelectorWindow()
        {
            var cfg = GetActiveImageGeneratorConfig();
            if (cfg?.promptTemplateSelector?.options == null || cfg.promptTemplateSelector.options.Count == 0)
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("提示词模板不可用"),
                    TJGeneratorsL10n.L("当前模型未配置提示词模板选项（options 为空）"),
                    LogTag
                );
                return;
            }

            TJGeneratorsMaterialTemplateSelectorWindow.ShowWindow(
                cfg.promptTemplateSelector.options,
                OnPromptTemplateSelected,
                string.IsNullOrEmpty(cfg.promptTemplateSelector.title)
                    ? TJGeneratorsL10n.L("选择提示词")
                    : TJGeneratorsL10n.L(cfg.promptTemplateSelector.title),
                showPreviewThumbnails: false
            );
        }

        private void OnPromptTemplateSelected(MaterialTemplateOptionConfig template)
        {
            selectedPromptTemplate = template;

            if (_currentGenerator is DynamicGenerator dg)
                dg.SetPromptTemplateSelection(selectedPromptTemplate);

            Repaint();
        }

        private void DrawPromptTemplateSelector()
        {
            var cfg = GetActiveImageGeneratorConfig();
            if (cfg?.promptTemplateSelector == null
                || !cfg.promptTemplateSelector.enabled
                || cfg.promptTemplateSelector.options == null
                || cfg.promptTemplateSelector.options.Count == 0)
            {
                return;
            }

            string title = string.IsNullOrEmpty(cfg.promptTemplateSelector.title)
                ? TJGeneratorsL10n.L("提示词模板")
                : TJGeneratorsL10n.L(cfg.promptTemplateSelector.title);

            UIComponents.DrawSelectionRow(
                title,
                TJGeneratorsL10n.L("选择提示词"),
                CommonStyles.DropBoxRightArrow4xTexture,
                ShowPromptTemplateSelectorWindow,
                TJGeneratorsL10n.L(selectedPromptTemplate?.name));

            GUILayout.Space(CommonStyles.Space3);
        }

        private void DrawFrontierSequenceProfileSelector()
        {
            // 兜底：某些窗口生命周期顺序下，forcedGeneratorId 可能在 OnEnable 之后才写入。
            // Frontier 模式且列表为空时，这里主动重载一次配置，避免“明明有配置却提示找不到”。
            if (IsFrontierSequenceMode() && frontierSequenceProfileNames.Count == 0)
                LoadFrontierSequenceProfilesIfNeeded();

            GUILayout.Space(10);
            GUILayout.Label(TJGeneratorsL10n.L("网格规格："), CommonStyles.HeaderStyle);

            if (frontierSequenceProfileNames.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(frontierSequenceResolvedConfigPath)
                        ? TJGeneratorsL10n.L("未找到模板配置，请检查 package 内 Editor/Config/FrontierSequenceProfiles.json")
                        : string.Format(TJGeneratorsL10n.L("未找到可用模板，配置文件：{0}"), frontierSequenceResolvedConfigPath),
                    MessageType.Warning
                );
                return;
            }

            int newIndex = EditorGUILayout.Popup(
                TJGeneratorsL10n.L("帧网格"),
                Mathf.Clamp(frontierSequenceProfileIndex, 0, frontierSequenceProfileNames.Count - 1),
                frontierSequenceProfileNames.ToArray()
            );
            if (newIndex != frontierSequenceProfileIndex)
            {
                frontierSequenceProfileIndex = newIndex;
                frontierSequenceProfileId = frontierSequenceProfileIds[frontierSequenceProfileIndex];
                ApplySliceSettingsFromSelectedProfile();
            }

            GUILayout.Space(8);
        }

        private void DrawInputSection()
        {
            // Frontier 序列帧模式下，始终使用默认 instruction/profile，
            // 不向用户暴露模板选择 UI，避免额外认知负担。
            if (!IsFrontierSequenceMode())
                DrawPromptTemplateSelector();

            var genConfig = GetCurrentGeneratorConfig();
            textPrompt = DrawConfiguredTextPromptInput(textPrompt, "image_prompt_input", genConfig);

            if (ShouldShowImageUpload(genConfig))
            {
                GUILayout.Space(CommonStyles.Space3);
                DrawReferenceImagesSection();
            }
        }

        private void DrawReferenceImagesSection()
        {
            DrawConfiguredReferenceImageUpload(
                referenceImagePaths,
                referenceUploadedImages,
                "image_reference_upload");
        }

        private void DrawConfigurationSection()
        {
            var provider = _currentGenerator as IGeneratorParameterProvider;

            var allParams = GetCurrentGeneratorParameters();
            List<ParameterConfig> filteredParams = null;
            if (allParams != null && allParams.Count > 0)
            {
                filteredParams = new List<ParameterConfig>(allParams.Count);
                for (int i = 0; i < allParams.Count; i++)
                {
                    var p = allParams[i];
                    if (p == null || string.IsNullOrEmpty(p.id))
                        continue;

                    if (p.id == "isSegmentation" || p.id == "qValue" || p.id == "resizeWidth")
                        continue;
                    if (IsFrontierSequenceMode() &&
                        (string.Equals(p.id, "aspectRatio", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(p.id, "aspect_ratio", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(p.id, "resolution", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(p.id, "imageSize", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    filteredParams.Add(p);
                }
            }

            showAdvancedSettings = DrawConfiguredAdvancedSettingsFoldout(
                showAdvancedSettings,
                provider,
                filteredParams
            );

            if (provider is DynamicGenerator dyn)
            {
                bool hasRef = referenceImagePaths != null && referenceImagePaths.Count > 0;
                dyn.SyncReferenceImagesForCostPreview(hasRef);
            }
            SyncGenerationCostWithCurrentGeneratorState();
        }

        private void DrawGenerationSection(LeftPanelBottomDock.Layout layout)
        {
            bool canGenerate =
                _currentGenerator != null && !string.IsNullOrWhiteSpace(textPrompt);
            UIComponents.DrawGenerationSectionAt(
                layout,
                isGenerating,
                generationProgress,
                generationStatus,
                canGenerate,
                StartGeneration,
                null,
                Repaint,
                currentGenerationCost
            );
        }

        private bool IsUnityTerrainHeightmapTemplateSelected()
        {
            return string.Equals(
                selectedPromptTemplate?.id,
                UnityTerrainHeightmapTemplateId,
                StringComparison.OrdinalIgnoreCase
            );
        }

        /// <summary>地形模板：后处理选项与「一键生成地形」位于生成按钮下方，顺序为「生成 → 后处理设置 → 建地形」。</summary>
        private void DrawTerrainHeightmapAfterGenerationSection()
        {
            if (!IsUnityTerrainHeightmapTemplateSelected())
                return;

            GUILayout.Label(TJGeneratorsL10n.L("地形高度图（生成后）"), CommonStyles.HeaderStyle);
            GUILayout.Space(6);

            GUILayout.Label(
                TJGeneratorsL10n.L("在右侧历史记录中选中对应 PNG 后，应用后处理并创建场景地形。"),
                CommonStyles.SmallGreyLabelStyle
            );
            GUILayout.Space(8);

            terrainHeightmapMedian3x3 = EditorGUILayout.ToggleLeft(
                TJGeneratorsL10n.L("后处理：Median 3x3 去尖刺（散点离群点）"),
                terrainHeightmapMedian3x3
            );

            GUILayout.Space(4);
            terrainHeightmapGaussianBlur = EditorGUILayout.ToggleLeft(
                TJGeneratorsL10n.L("后处理：高斯模糊平滑"),
                terrainHeightmapGaussianBlur
            );
            if (terrainHeightmapGaussianBlur)
            {
                EditorGUI.indentLevel++;
                terrainHeightmapBlurSigma = EditorGUILayout.Slider(
                    TJGeneratorsL10n.L("模糊强度 (σ)"),
                    terrainHeightmapBlurSigma,
                    0.5f,
                    3f
                );
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(8);
            terrainHeightmapRemapFoldout = EditorGUILayout.Foldout(
                terrainHeightmapRemapFoldout,
                TJGeneratorsL10n.L("高度重映射（类似 Terrain Tools · Height Remap）"),
                true
            );
            if (terrainHeightmapRemapFoldout)
            {
                EditorGUI.indentLevel++;
                terrainHeightmapPercentileNormalize = EditorGUILayout.ToggleLeft(
                    TJGeneratorsL10n.L("百分位拉伸（去掉极暗/极亮离群点再起有效对比）"),
                    terrainHeightmapPercentileNormalize
                );
                EditorGUI.BeginDisabledGroup(!terrainHeightmapPercentileNormalize);
                terrainHeightmapPercentileLow = EditorGUILayout.Slider(
                    new GUIContent(
                        TJGeneratorsL10n.L("低端截断"),
                        TJGeneratorsL10n.L("低于该百分位的亮度视作海平面一端，类似压低海底噪声")
                    ),
                    terrainHeightmapPercentileLow,
                    0f,
                    0.2f
                );
                terrainHeightmapPercentileHigh = EditorGUILayout.Slider(
                    new GUIContent(
                        TJGeneratorsL10n.L("高端截断"),
                        TJGeneratorsL10n.L("高于该百分位的亮度视作山顶一端")
                    ),
                    terrainHeightmapPercentileHigh,
                    0.8f,
                    1f
                );
                EditorGUI.EndDisabledGroup();
                if (terrainHeightmapPercentileHigh <= terrainHeightmapPercentileLow)
                    terrainHeightmapPercentileHigh =
                        Mathf.Min(1f, terrainHeightmapPercentileLow + 0.02f);

                terrainHeightmapHeightGamma = EditorGUILayout.Slider(
                    new GUIContent(
                        TJGeneratorsL10n.L("高度曲线 Gamma"),
                        TJGeneratorsL10n.L("1 = 线性；小于 1 中间调抬高（更陡）；大于 1 更平（更多平原）")
                    ),
                    terrainHeightmapHeightGamma,
                    0.35f,
                    2.5f
                );

                EditorGUILayout.LabelField(
                    TJGeneratorsL10n.L("输出垂直范围（归一化高度映射到 [最低, 最高]）"),
                    CommonStyles.SmallGreyLabelStyle
                );
                terrainHeightmapRemapOutMin = EditorGUILayout.Slider(
                    new GUIContent(TJGeneratorsL10n.L("输出最低"), TJGeneratorsL10n.L("地形最凹处对应高度图灰度下限")),
                    terrainHeightmapRemapOutMin,
                    0f,
                    1f
                );
                terrainHeightmapRemapOutMax = EditorGUILayout.Slider(
                    new GUIContent(TJGeneratorsL10n.L("输出最高"), TJGeneratorsL10n.L("地形最高处对应高度图灰度上限")),
                    terrainHeightmapRemapOutMax,
                    0f,
                    1f
                );
                if (terrainHeightmapRemapOutMax <= terrainHeightmapRemapOutMin)
                    terrainHeightmapRemapOutMax =
                        Mathf.Min(1f, terrainHeightmapRemapOutMin + 0.02f);

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);

            var selectedHistoryItem =
                selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count
                    ? generationHistory[selectedHistoryIndex]
                    : null;
            bool canTerrain =
                CanGenerateTerrainFromHistoryItem(selectedHistoryItem);
            EditorGUI.BeginDisabledGroup(!canTerrain);
            if (GUILayout.Button(TJGeneratorsL10n.L("一键生成地形"), GUILayout.Height(28)))
                GenerateTerrainFromHeightmap(selectedHistoryIndex);
            EditorGUI.EndDisabledGroup();

            if (!canTerrain)
            {
                GUILayout.Space(4);
                GUILayout.Label(
                    TJGeneratorsL10n.L("请先在历史中选中由本模板生成的已完成 PNG。"),
                    CommonStyles.SmallGreyLabelStyle
                );
            }
        }

        /// <summary>Frontier 序列帧：抠图与切割折叠区（与高级设置同款交互）。</summary>
        private void DrawFrontierSequenceCutoutAfterGenerationSection()
        {
            if (!IsFrontierSequenceMode())
                return;

            showSpriteCutoutSettings = UIComponents.DrawSettingsFoldout(
                showSpriteCutoutSettings,
                TJGeneratorsL10n.L("抠图与切割"),
                DrawSpriteCutoutAndSliceContent,
                uppercaseLabel: true);
        }

        private void DrawHistoryPanel(float panelWidth)
        {
            float historyPanelHeight = isVerticalLayout ? position.height * 0.45f : position.height;
            float historyPanelInner = CommonStyles.HistoryPanelInnerWidth(panelWidth);
            float historyScrollInner = CommonStyles.HistoryScrollViewLayoutWidth(panelWidth);
            EnsureHistorySelectionAndFallback();

            UIComponents.BeginHistoryPanel(panelWidth, historyPanelHeight, isVerticalLayout);

            Texture2D historyPreviewTex = null;
            if (selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count)
            {
                var selectedItem = generationHistory[selectedHistoryIndex];
                if (!selectedItem.isGenerating)
                    historyPreviewTex = GetPreviewTextureForHistoryItem(selectedItem);
            }

            // 没有选择历史时，回退到当前绑定资产预览
            if (historyPreviewTex == null)
                historyPreviewTex = imagePreviewTexture;

            // processedPreview 需要绑定“当前实际处理的源图”（可能来自历史，也可能来自目标资产）。
            // 否则当用户未选中历史、直接处理目标图时，会出现“抠图没反应/预览不更新”的错觉。
            string activeSourcePath = GetActivePreviewSourcePath()?.Replace('\\', '/');
            historyPreviewTex = GetEffectivePreviewTexture(historyPreviewTex, activeSourcePath);

            float previewBlockHeight = historyMainPreview.Draw(
                historyPreviewTex,
                historyPanelInner,
                position.height,
                isVerticalLayout,
                Repaint,
                IsFrontierSequenceMode() && !string.IsNullOrEmpty(GetActivePreviewSourcePath())
                    ? (Action<Rect, Rect>)((drawRect, texCoords) => ImagePreview.DrawSliceGridOverlay(
                        drawRect,
                        Mathf.Max(1, spriteSliceColumns),
                        Mathf.Max(1, spriteSliceRows),
                        texCoords))
                    : null
            );
            GUILayout.Space(12);

            const float historyBottomMargin = 90f;
            float scrollHeight = historyPanelHeight - previewBlockHeight - 12f - historyBottomMargin;
            historyScrollPosition = GUILayout.BeginScrollView(
                historyScrollPosition,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.Height(scrollHeight));

            if (generationHistory.Count == 0)
                UIComponents.DrawHistoryEmptyState();
            else
                DrawHistoryGrid(historyScrollInner);

            GUILayout.EndScrollView();

            DrawHistoryActions();

            UIComponents.EndHistoryPanel();
        }

        /// <summary>
        /// 校正历史列表与选中索引。不在此处把绑定目标塞进历史，避免与精灵窗口不一致（无真实生成记录时不应出现占位条目）。
        /// </summary>
        private void EnsureHistorySelectionAndFallback()
        {
            if (generationHistory == null)
                generationHistory = new List<TJGeneratorsGenerationHistoryItem>();

            if (generationHistory.Count > 0)
                selectedHistoryIndex = Mathf.Clamp(selectedHistoryIndex, 0, generationHistory.Count - 1);
            else
                selectedHistoryIndex = -1;
        }

        private Texture2D GetEffectivePreviewTexture(Texture2D historyPreviewTex, string selectedHistoryPath)
        {
            // Non-frontier windows must never show cutout/slicing processed previews.
            if (!IsFrontierSequenceMode())
            {
                if (processedPreviewValid || processedPreviewTexture != null)
                    ResetProcessedPreview();
                return historyPreviewTex;
            }

            if (!processedPreviewValid || processedPreviewTexture == null)
                return historyPreviewTex;

            if (string.IsNullOrEmpty(selectedHistoryPath) || !string.Equals(selectedHistoryPath, processedPreviewSourcePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetProcessedPreview();
                return historyPreviewTex;
            }

            return processedPreviewTexture;
        }

        private void DrawHistoryGrid(float historyContentWidth)
        {
            float tileWidth = currentHistoryTileSize;
            float labelHeight = currentHistoryTileSize >= 100f ? 40f : 32f;
            float tileHeight = tileWidth + labelHeight;
            int itemsPerRow = ComputeHistoryItemsPerRow(historyContentWidth, tileWidth);

            for (int i = 0; i < generationHistory.Count; i += itemsPerRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = 0; j < itemsPerRow && (i + j) < generationHistory.Count; j++)
                {
                    int index = i + j;
                    var item = generationHistory[index];
                    bool isSelected = selectedHistoryIndex == index;

                    GUILayout.BeginVertical(
                        GetScaledHistoryTileStyle(isSelected),
                        GUILayout.Width(tileWidth),
                        GUILayout.Height(tileHeight)
                    );

                    float previewSize = GetScaledHistoryPreviewSize(tileWidth);
                    Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize);

                    DrawImageHistoryPreview(previewRect, item);

                    if (
                        !item.isGenerating
                        && Event.current.type == EventType.MouseDown
                        && previewRect.Contains(Event.current.mousePosition)
                    )
                    {
                        if (selectedHistoryIndex != index)
                            ResetProcessedPreview();
                        selectedHistoryIndex = index;
                        Event.current.Use();
                        Repaint();
                    }

                    if (
                        !item.isGenerating
                        && Event.current.type == EventType.ContextClick
                        && previewRect.Contains(Event.current.mousePosition)
                    )
                    {
                        ShowHistoryContextMenu(index);
                        Event.current.Use();
                    }

                    GUILayout.Label(GetHistoryUserPromptLabel(item), CommonStyles.HistoryLabelStyle);
                    GUILayout.Label(
                        GetModelDisplayLabelFromIndex(item.modelVersion),
                        CommonStyles.SmallGreyCenterLabelStyle
                    );

                    GUILayout.EndVertical();
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawImageHistoryPreview(Rect rect, TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating)
            {
                UIComponents.DrawLoadingSpinner(rect, CommonStyles.SmallGreyLabelStyle, Repaint);
                return;
            }

            if (!string.IsNullOrEmpty(item.modelPath))
            {
                if (
                    historyPreviewCache.TryGetValue(item.modelPath, out var cached)
                    && cached != null
                )
                {
                    GUI.DrawTexture(rect, cached, ScaleMode.ScaleToFit);
                    return;
                }

                var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(item.modelPath);
                if (assetTex != null)
                {
                    historyPreviewCache[item.modelPath] = assetTex;
                    GUI.DrawTexture(rect, assetTex, ScaleMode.ScaleToFit);
                    return;
                }

                string absPath = PathUtils.ToAbsoluteAssetPath(item.modelPath);
                if (File.Exists(absPath))
                {
                    // 异步加载本地预览图到缓存，避免OnGUI卡顿
                    EnqueuePreviewLoad(item.modelPath, absPath, false);
                }
            }

            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
            var iconRect = new Rect(
                rect.x + rect.width / 4,
                rect.y + rect.height / 4,
                rect.width / 2,
                rect.height / 2
            );
            GUI.Label(iconRect, EditorGUIUtility.IconContent("d_Texture2D Icon"));
        }

        private Texture2D GetPreviewTextureForHistoryItem(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating)
                return null;

            if (!string.IsNullOrEmpty(item.modelPath))
            {
                if (
                    historyPreviewCache.TryGetValue(item.modelPath, out var cached)
                    && cached != null
                )
                    return cached;

                var assetTex = AssetDatabase.LoadAssetAtPath<Texture2D>(item.modelPath);
                if (assetTex != null)
                {
                    historyPreviewCache[item.modelPath] = assetTex;
                    return assetTex;
                }

                string absPath = PathUtils.ToAbsoluteAssetPath(item.modelPath);
                if (File.Exists(absPath))
                {
                    EnqueuePreviewLoad(item.modelPath, absPath, false);
                }
            }

            // 可选：如果历史项已经有 URL 预览缓存，也可以复用
            if (
                item.isTextToModel
                && !string.IsNullOrEmpty(item.previewImageUrl)
                && urlPreviewCache.TryGetValue(item.previewImageUrl, out var urlTex)
                && urlTex != null
            )
            {
                return urlTex;
            }

            return null;
        }

        private void DrawHistoryActions()
        {
            GUILayout.Space(5);
            GUILayout.BeginHorizontal();

            bool hasSelection = selectedHistoryIndex >= 0 && selectedHistoryIndex < generationHistory.Count;
            bool isGenerating = hasSelection && generationHistory[selectedHistoryIndex].isGenerating;
            GUI.enabled = hasSelection && !isGenerating;

            if (GUILayout.Button(TJGeneratorsL10n.L("应用到当前图片"), GUILayout.Height(25)))
                ApplyHistoryToImage(selectedHistoryIndex);

            if (GUILayout.Button(TJGeneratorsL10n.L("在项目中显示"), GUILayout.Height(25)))
                ShowHistoryInProject(selectedHistoryIndex);

            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.Space(6);
            historyMainPreview.DrawZoomSlider(90f);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        private static string GetHistoryUserPromptLabel(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null)
                return "";
            return TJGenerators.Utils.TJGeneratorsPromptDisplay.FormatHistoryTileLabel(item.GetUserFacingPrompt());
        }

        private void DrawSpriteCutoutAndSliceContent()
        {
            string activeSourcePath = GetActivePreviewSourcePath();

            GUILayout.Label(
                TJGeneratorsL10n.L("在右侧预览区选中历史图片后，可抠图、切割并导出 Sprite 动画；也可直接导入本地图片。"),
                CommonStyles.HelpBoxStyle,
                GUILayout.MaxWidth(CommonStyles.LeftComponentWidth));
            GUILayout.Space(CommonStyles.Space2);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(TJGeneratorsL10n.L("导入本地图片…"), GUILayout.Height(22), GUILayout.ExpandWidth(true)))
                ImportLocalImageToHistory();
            GUILayout.Space(CommonStyles.Space2);
            if (GUILayout.Button(TJGeneratorsL10n.L("使用当前选中图片"), GUILayout.Height(22), GUILayout.ExpandWidth(true)))
                AddSelectedProjectTextureToHistory();
            GUILayout.EndHorizontal();

            GUILayout.Space(CommonStyles.Space2);

            if (string.IsNullOrEmpty(activeSourcePath))
            {
                GUILayout.Label(
                    TJGeneratorsL10n.L("未找到可处理的图片：请先在右侧历史中选一张，或点击上方「导入本地图片 / 使用当前选中图片」。"),
                    CommonStyles.HelpBoxStyle,
                    GUILayout.MaxWidth(CommonStyles.LeftComponentWidth));
                return;
            }

            chromaKeyTolerance = UIComponents.DrawAdvancedStyleSliderRow(
                TJGeneratorsL10n.L("绿幕容差"), chromaKeyTolerance, 0.05f, 0.35f);
            GUILayout.Space(CommonStyles.Space2);
            chromaFeather = UIComponents.DrawAdvancedStyleSliderRow(
                TJGeneratorsL10n.L("边缘羽化"), chromaFeather, 0f, 0.3f);

            GUILayout.Space(CommonStyles.Space2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(TJGeneratorsL10n.L("执行抠图并预览"), GUILayout.Height(24), GUILayout.ExpandWidth(true)))
                ApplyGreenScreenCutoutPreview();
            GUILayout.Space(CommonStyles.Space2);
            if (GUILayout.Button(TJGeneratorsL10n.L("恢复原图预览"), GUILayout.Height(24), GUILayout.ExpandWidth(true)))
            {
                ResetProcessedPreview();
                Repaint();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(CommonStyles.Space2);
            spriteSliceColumns = Mathf.Max(
                1,
                UIComponents.DrawAdvancedStyleIntRow(TJGeneratorsL10n.L("切割列数"), spriteSliceColumns, "sprite_slice_columns"));
            GUILayout.Space(CommonStyles.Space2);
            spriteSliceRows = Mathf.Max(
                1,
                UIComponents.DrawAdvancedStyleIntRow(TJGeneratorsL10n.L("切割行数"), spriteSliceRows, "sprite_slice_rows"));
            GUILayout.Space(CommonStyles.Space2);
            spriteSliceFps = UIComponents.DrawAdvancedStyleSliderRow(TJGeneratorsL10n.L("动画 FPS"), spriteSliceFps, 1f, 60f);
            GUILayout.Space(CommonStyles.Space2);
            spriteSliceLoop = UIComponents.DrawAdvancedStyleBoolRow(TJGeneratorsL10n.L("动画循环 (Loop)"), spriteSliceLoop);
            GUILayout.Space(CommonStyles.Space2);
            GUILayout.Label(
                TJGeneratorsL10n.L("预览图中的红色线条为切割线。"),
                CommonStyles.HelpBoxStyle,
                GUILayout.MaxWidth(CommonStyles.LeftComponentWidth));

            GUILayout.Space(CommonStyles.Space2);
            if (GUILayout.Button(TJGeneratorsL10n.L("切割并导出为 Sprite"), GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                SliceSelectedHistoryToSprites();
        }

        private void ImportLocalImageToHistory()
        {
            string file = EditorUtility.OpenFilePanel(TJGeneratorsL10n.L("选择要导入的图片"), "", "png,jpg,jpeg");
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                return;

            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/Imported"))
                AssetDatabase.CreateFolder("Assets/TJGenerators", "Imported");

            string ext = Path.GetExtension(file);
            if (string.IsNullOrEmpty(ext))
                ext = ".png";
            ext = ext.ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
                ext = ".png";

            string baseName = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrEmpty(baseName))
                baseName = "ImportedImage";

            string dstAssetPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/TJGenerators/Imported/{baseName}{ext}");
            string dstAbs = PathUtils.ToAbsoluteAssetPath(dstAssetPath);
            try
            {
                string dir = Path.GetDirectoryName(dstAbs);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(file, dstAbs, true);
            }
            catch (Exception e)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("导入失败"), e.Message, LogTag);
                return;
            }

            AssetDatabase.ImportAsset(dstAssetPath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(dstAssetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            AddImageAssetPathToHistoryAndSelect(dstAssetPath);
            Repaint();
        }

        private void AddSelectedProjectTextureToHistory()
        {
            var tex = Selection.activeObject as Texture2D;
            if (tex == null)
            {
                Debug.LogWarning($"{LogTag} 请先在 Project 里选中一张 Texture2D 图片（png/jpg）。");
                return;
            }

            string p = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(p))
            {
                Debug.LogWarning($"{LogTag} 无法获取选中图片的路径。");
                return;
            }

            p = p.Replace('\\', '/');
            if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                && !p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"{LogTag} 当前仅支持 png/jpg/jpeg。");
                return;
            }

            AddImageAssetPathToHistoryAndSelect(p);
            Repaint();
        }

        private void AddImageAssetPathToHistoryAndSelect(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            assetPath = assetPath.Replace('\\', '/');
            if (!File.Exists(PathUtils.ToAbsoluteAssetPath(assetPath)))
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("图片文件不存在，无法加入历史。"), LogTag);
                return;
            }

            EnsureHistorySelectionAndFallback();

            // 去重：已在历史中则直接选中
            for (int i = 0; i < generationHistory.Count; i++)
            {
                var it = generationHistory[i];
                if (it != null && !string.IsNullOrEmpty(it.modelPath)
                    && string.Equals(it.modelPath.Replace('\\', '/'), assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (selectedHistoryIndex != i)
                        ResetProcessedPreview();
                    selectedHistoryIndex = i;
                    var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                    if (obj != null)
                    {
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                    return;
                }
            }

            generationHistory.Insert(0, new TJGeneratorsGenerationHistoryItem
            {
                modelPath = assetPath,
                isGenerating = false,
                prompt = TJGeneratorsL10n.L("（手动导入图片）"),
                modelVersion = "manual_import"
            });
            ResetProcessedPreview();
            selectedHistoryIndex = 0;

            var assetObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (assetObj != null)
            {
                Selection.activeObject = assetObj;
                EditorGUIUtility.PingObject(assetObj);
            }
        }

        private void ShowHistoryContextMenu(int index)
        {
            if (index < 0 || index >= generationHistory.Count)
                return;
            var item = generationHistory[index];
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(TJGeneratorsL10n.L("应用到当前图片")), false, () => ApplyHistoryToImage(index));
            menu.AddItem(new GUIContent(TJGeneratorsL10n.L("在项目中显示")), false, () => ShowHistoryInProject(index));

            if (CanGenerateTerrainFromHistoryItem(item))
                menu.AddItem(
                    new GUIContent(TJGeneratorsL10n.L("一键生成地形")),
                    false,
                    () => GenerateTerrainFromHeightmap(index)
                );

            menu.AddSeparator("");

            if (!string.IsNullOrEmpty(item.modelPath))
                menu.AddItem(
                    new GUIContent(TJGeneratorsL10n.L("在资源管理器中显示")),
                    false,
                    () => EditorUtility.RevealInFinder(item.modelPath)
                );

            menu.AddSeparator("");

            menu.AddItem(
                new GUIContent(TJGeneratorsL10n.L("从历史记录中移除")),
                false,
                () =>
                {
                    TJGeneratorsHistoryManager.RemoveFromHistory(item.modelPath);
                    RefreshHistory();
                    if (generationHistory.Count == 0)
                        selectedHistoryIndex = -1;
                    else if (selectedHistoryIndex >= generationHistory.Count)
                        selectedHistoryIndex = Mathf.Max(0, generationHistory.Count - 1);
                    Repaint();
                }
            );

            menu.ShowAsContext();
        }

        private void ApplyHistoryToImage(int index)
        {
            if (index < 0 || index >= generationHistory.Count)
                return;
            var item = generationHistory[index];

            if (item.isGenerating)
            {
                Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("请等待该条生成完成后再应用。")}");
                return;
            }

            if (
                string.IsNullOrEmpty(item.modelPath)
                || !File.Exists(PathUtils.ToAbsoluteAssetPath(item.modelPath))
            )
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("该历史记录的图片文件不存在。"), LogTag);
                if (!string.IsNullOrEmpty(item.modelPath))
                    TJGeneratorsHistoryManager.RemoveFromHistory(item.modelPath);
                RefreshHistory();
                Repaint();
                return;
            }

            if (targetImageAsset == null || !targetImageAsset.IsValid())
            {
                Debug.LogWarning($"{LogTag} {TJGeneratorsL10n.L("请先绑定或创建目标图片资产。")}");
                return;
            }

            string srcExt = string.IsNullOrEmpty(item.modelPath) ? ".png" : Path.GetExtension(item.modelPath);
            if (string.IsNullOrEmpty(srcExt)) srcExt = ".png";
            string targetPathForDialog = Path.ChangeExtension(targetImageAsset.GetPath(), srcExt);
            if (
                !EditorUtility.DisplayDialog(
                    TJGeneratorsL10n.L("确认替换"),
                    string.Format(TJGeneratorsL10n.L("确定将选中的图片应用到当前目标「{0}」吗？"), Path.GetFileNameWithoutExtension(targetPathForDialog)),
                    TJGeneratorsL10n.L("确定"),
                    TJGeneratorsL10n.L("取消")
                )
            )
            {
                return;
            }

            if (!ReplaceTargetImageFromSource(item.modelPath, TJGeneratorsL10n.L("已将历史图片应用到"), out string err))
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), string.IsNullOrEmpty(err) ? TJGeneratorsL10n.L("应用失败（详见控制台）。") : string.Format(TJGeneratorsL10n.L("应用失败: {0}"), err), LogTag);
            else
                RefreshHistory();

            Repaint();
        }

        /// <summary>
        /// 覆盖目标纹理资产前释放本窗口持有的引用，避免 Windows 下文件仍被 Unity/预览占用导致 File.Copy 失败。
        /// 行为对齐 <see cref="TJGeneratorsSpriteWindow"/> 在应用历史前对 <c>spritePreviewTexture</c> 的处理。
        /// </summary>
        private void ReleaseTextureHandlesForTargetOverwrite(string targetAssetPath)
        {
            if (string.IsNullOrEmpty(targetAssetPath))
                return;

            targetAssetPath = targetAssetPath.Replace('\\', '/');

            if (
                processedPreviewValid
                && !string.IsNullOrEmpty(processedPreviewSourcePath)
                && string.Equals(
                    processedPreviewSourcePath.Replace('\\', '/'),
                    targetAssetPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ResetProcessedPreview();
            }

            if (imagePreviewTexture != null)
            {
                string pt = AssetDatabase.GetAssetPath(imagePreviewTexture).Replace('\\', '/');
                if (string.Equals(pt, targetAssetPath, StringComparison.OrdinalIgnoreCase))
                    imagePreviewTexture = null;
            }

            var keysToRemove = new List<string>();
            foreach (var kv in historyPreviewCache)
            {
                if (string.Equals(kv.Key.Replace('\\', '/'), targetAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(kv.Key);
                    continue;
                }

                if (kv.Value == null)
                    continue;
                string cachedPath = AssetDatabase.GetAssetPath(kv.Value).Replace('\\', '/');
                if (string.Equals(cachedPath, targetAssetPath, StringComparison.OrdinalIgnoreCase))
                    keysToRemove.Add(kv.Key);
            }

            foreach (var k in keysToRemove)
                historyPreviewCache.Remove(k);
        }

        /// <summary>
        /// 将源图片复制到当前目标资产；若扩展名变化则删除旧占位文件并更新 GUID / 历史记录，
        /// 与生成完成回调 <see cref="OnTextureSaved"/> 保持一致，避免同基名下残留 .jpg 与 .png 两个文件。
        /// </summary>
        private bool ReplaceTargetImageFromSource(string sourceAssetPath, string okLogVerb, out string errorMessage)
        {
            errorMessage = null;

            string ext = Path.GetExtension(sourceAssetPath).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                ext = ".png";

            if (targetImageAsset == null || !targetImageAsset.IsValid())
            {
                EnsureTargetImage(ext);
                if (targetImageAsset == null || !targetImageAsset.IsValid())
                {
                    errorMessage = TJGeneratorsL10n.L("目标图片无效");
                    return false;
                }
            }

            string oldTargetGuid = targetImageAsset.guid;
            string originalPath = targetImageAsset.GetPath();
            string targetPath = Path.ChangeExtension(originalPath, ext);
            string sourceAbsolute = PathUtils.ToAbsoluteAssetPath(sourceAssetPath);
            string targetAbsolute = PathUtils.ToAbsoluteAssetPath(targetPath);

            try
            {
                if (string.IsNullOrEmpty(sourceAbsolute) || !File.Exists(sourceAbsolute))
                {
                    errorMessage = TJGeneratorsL10n.L("源图片文件不存在");
                    return false;
                }

                string targetDir = Path.GetDirectoryName(targetAbsolute);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                ReleaseTextureHandlesForTargetOverwrite(originalPath);
                File.Copy(sourceAbsolute, targetAbsolute, true);
                AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);

                var importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.SaveAndReimport();
                }

                TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(targetPath));
                TJLog.Log($"{LogTag} {okLogVerb} {targetPath}");

                if (!string.Equals(originalPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    string originalAbsolute = PathUtils.ToAbsoluteAssetPath(originalPath);
                    if (File.Exists(originalAbsolute))
                    {
                        AssetDatabase.DeleteAsset(originalPath);
                        TJLog.Log($"{LogTag} 已删除旧占位文件: {originalPath}");
                    }

                    if (!string.IsNullOrEmpty(oldTargetGuid))
                        imageOpenWindows.Remove(oldTargetGuid);

                    targetImageAsset = TJGeneratorsAssetReference.FromPath(targetPath);
                    titleContent = new GUIContent(string.Format(TJGeneratorsL10n.L("TJGenerators 图片 - {0}"), Path.GetFileNameWithoutExtension(targetPath)));
                    string newGuid = targetImageAsset.guid;
                    if (!string.IsNullOrEmpty(newGuid))
                    {
                        imageOpenWindows[newGuid] = this;
                        TJGeneratorsHistoryManager.RewriteAssetGuid(oldTargetGuid, newGuid);
                    }
                }

                var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
                if (newTex != null)
                {
                    imagePreviewTexture = newTex;
                    Selection.activeObject = newTex;
                    EditorGUIUtility.PingObject(newTex);
                }

                TJGeneratorsGenerationLabel.EnableLabel(targetImageAsset);
                return true;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                TJLog.LogWarning($"{LogTag} 复制到目标图片失败: {e.Message}");
                return false;
            }
        }

        private void ShowHistoryInProject(int index)
        {
            if (index < 0 || index >= generationHistory.Count)
                return;

            var item = generationHistory[index];
            if (string.IsNullOrEmpty(item.modelPath))
                return;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.modelPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        private void ApplyGreenScreenCutoutPreview()
        {
            string sourcePath = GetActivePreviewSourcePath();
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"{LogTag} 未找到可处理的预览图，请先生成或选中一张历史图片。");
                return;
            }

            Texture2D src = SpriteSequencePostProcessService.LoadReadableTextureFromAssetPath(sourcePath);
            if (src == null)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("无法读取选中的历史图片。"), LogTag);
                return;
            }

            try
            {
                Texture2D cutout = SpriteSequencePostProcessService.BuildGreenScreenCutoutTexture(src, chromaKeyTolerance, chromaFeather);
                ReplaceProcessedPreview(cutout, sourcePath);
                Repaint();
            }
            finally
            {
                DestroyImmediate(src);
            }
        }

        private void SliceSelectedHistoryToSprites()
        {
            string sourcePath = GetActivePreviewSourcePath();
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning($"{LogTag} 未找到可切割的图片，请先生成或选中一张历史图片。");
                return;
            }

            Texture2D sourceTexture = processedPreviewValid && processedPreviewTexture != null && string.Equals(processedPreviewSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)
                ? processedPreviewTexture
                : SpriteSequencePostProcessService.LoadReadableTextureFromAssetPath(sourcePath);

            if (sourceTexture == null)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("无法读取选中的历史图片。"), LogTag);
                return;
            }

            bool shouldDestroyLoaded = sourceTexture != processedPreviewTexture;
            try
            {
                var sliceResult = SpriteSequencePostProcessService.SliceTextureToSpritesAndAnimation(
                    sourceTexture,
                    sourcePath,
                    spriteSliceColumns,
                    spriteSliceRows,
                    spriteSliceFps,
                    spriteSliceLoop
                );
                string outputDir = sliceResult.OutputDirectory;
                int exported = sliceResult.ExportedCount;
                string clipPath = sliceResult.AnimationClipPath;
                string msg = string.IsNullOrEmpty(clipPath)
                    ? TJGeneratorsL10n.L("已导出 {0} 张 Sprite。\n路径：{1}", exported, outputDir)
                    : TJGeneratorsL10n.L("已导出 {0} 张 Sprite，并创建动画文件。\nSprite路径：{1}\n动画路径：{2}", exported, outputDir, clipPath);
                Debug.Log($"{LogTag} 切割完成：\n{msg}");

                if (IsFrontierSequenceMode())
                {
                    foreach (var spritePath in sliceResult.SpriteAssetPaths)
                    {
                        var spriteAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(spritePath);
                        TJGeneratorsGenerationLabel.EnableFrontierLabel(spriteAsset);
                    }
                    if (!string.IsNullOrEmpty(clipPath))
                    {
                        var clipObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(clipPath);
                        TJGeneratorsGenerationLabel.EnableFrontierLabel(clipObj);
                    }
                }

                EditorGUIUtility.PingObject(
                    !string.IsNullOrEmpty(clipPath)
                        ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(clipPath)
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(outputDir)
                );
            }
            finally
            {
                if (shouldDestroyLoaded)
                    DestroyImmediate(sourceTexture);
            }
        }

        private string GetSelectedHistoryModelPath()
        {
            if (selectedHistoryIndex < 0 || selectedHistoryIndex >= generationHistory.Count)
                return null;
            var item = generationHistory[selectedHistoryIndex];
            if (item == null || string.IsNullOrEmpty(item.modelPath))
                return null;
            return item.modelPath.Replace('\\', '/');
        }

        private string GetActivePreviewSourcePath()
        {
            string historyPath = GetSelectedHistoryModelPath();
            if (!string.IsNullOrEmpty(historyPath))
                return historyPath;

            if (targetImageAsset != null && targetImageAsset.IsValid())
            {
                string p = targetImageAsset.GetPath();
                if (!string.IsNullOrEmpty(p))
                    return p.Replace('\\', '/');
            }

            return null;
        }

        private void ReplaceProcessedPreview(Texture2D newTexture, string sourcePath)
        {
            ResetProcessedPreview();
            processedPreviewTexture = newTexture;
            processedPreviewSourcePath = sourcePath;
            processedPreviewValid = processedPreviewTexture != null;
        }

        private void ResetProcessedPreview()
        {
            if (processedPreviewTexture != null)
                DestroyImmediate(processedPreviewTexture);
            processedPreviewTexture = null;
            processedPreviewSourcePath = null;
            processedPreviewValid = false;
        }

        /// <summary>
        /// Frontier 序列帧模式：必须能从 FrontierSequenceProfiles.json 解析出非空 instructions，否则禁止提交生成。
        /// </summary>
        private bool TryValidateFrontierSequenceInstructionsForGeneration(out string failureMessage)
        {
            failureMessage = null;
            if (!IsFrontierSequenceMode())
                return true;

            LoadFrontierSequenceProfilesIfNeeded();
            if (frontierSequenceProfilesRoot == null)
            {
                failureMessage =
                    TJGeneratorsL10n.L("未找到或无法读取 FrontierSequenceProfiles.json。\n\n请确认包内含 Editor/Config/FrontierSequenceProfiles.json，或通过 Package Manager 正确安装 cn.tuanjie.ai.generators。");
                return false;
            }

            if (string.IsNullOrEmpty(frontierSequenceProfileId))
            {
                failureMessage =
                    TJGeneratorsL10n.L("序列帧模板不可用：请检查 FrontierSequenceProfiles.json 中的 profiles 与 defaultProfileId 是否有效。");
                return false;
            }

            string envelopeRaw = BuildFrontierSequenceEnvelopeRawFromSelectedProfile(referenceImagePaths);
            if (string.IsNullOrEmpty(envelopeRaw))
            {
                failureMessage = TJGeneratorsL10n.L("无法根据当前模板构建序列帧指令包（frontier_sequence_envelope），请检查配置文件。");
                return false;
            }

            string instructions = ExtractInstructionsFromEnvelopeRaw(envelopeRaw);
            if (string.IsNullOrWhiteSpace(instructions))
            {
                failureMessage =
                    TJGeneratorsL10n.L("当前 profile 的 instructions 为空或缺失，请编辑 FrontierSequenceProfiles.json 填写完整指令。");
                return false;
            }

            return true;
        }

        // ========== 生成 ==========
        private void StartGeneration()
        {
            if (string.IsNullOrWhiteSpace(textPrompt))
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("请输入文本提示词。"), LogTag);
                return;
            }

            int frontierUserRefCount = 0;
            List<string> effectiveReferencePaths;

            // 必须先加载 profile；指令校验也依赖 profile。
            if (IsFrontierSequenceMode())
            {
                LoadFrontierSequenceProfilesIfNeeded();
                if (!TryValidateFrontierSequenceInstructionsForGeneration(out string frontierInstrFail))
                {
                    ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("缺少序列帧指令配置"), frontierInstrFail, LogTag);
                    return;
                }
            }

            if (IsFrontierSequenceMode())
            {
                var split = BuildEffectiveReferenceImagePathsWithUserCount(referenceImagePaths);
                effectiveReferencePaths = split.paths;
                frontierUserRefCount = split.userImageCount;
            }
            else
            {
                effectiveReferencePaths = new List<string>(referenceImagePaths);
            }

            bool hasImage = effectiveReferencePaths.Count > 0;

            if (_currentGenerator == null)
            {
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("错误"), TJGeneratorsL10n.L("未选择可用的生成模型。"), LogTag);
                return;
            }

            EnsureTargetImage();

            isGenerating = true;
            generationStatus = TJGeneratorsL10n.L("准备中...");
            generationProgress = 0f;

            if (_currentGenerator is DynamicGenerator dynamicGen)
            {
                dynamicGen.SetParameter("isSegmentation", false);
                dynamicGen.ClearExtraRawJsonFields();

                string finalPrompt = textPrompt.Trim();
                if (IsFrontierSequenceMode())
                {
                    // FrontierSequenceProfiles 要求 1:1 spritesheet，固定 square_hd + png 便于绿幕抠图
                    dynamicGen.SetParameter("imageSize", "square_hd");
                    dynamicGen.SetParameter("outputFormat", "png");
                    dynamicGen.SetPromptTemplateSelection(null);
                    string envelopeRaw = BuildFrontierSequenceEnvelopeRawFromSelectedProfile(referenceImagePaths);
                    if (!string.IsNullOrEmpty(envelopeRaw))
                    {
                        dynamicGen.SetExtraRawJsonField("frontier_sequence_envelope", envelopeRaw);
                        string instructions = ExtractInstructionsFromEnvelopeRaw(envelopeRaw);
                        if (!string.IsNullOrEmpty(instructions))
                            finalPrompt = BuildPromptWithInstructionsFallback(finalPrompt, instructions);

                        if (effectiveReferencePaths.Count > 0)
                            finalPrompt = FrontierSequenceImageOrderHint.AppendToPrompt(
                                finalPrompt,
                                effectiveReferencePaths.Count,
                                frontierUserRefCount
                            );
                    }
                }
                else
                {
                    dynamicGen.SetPromptTemplateSelection(selectedPromptTemplate);
                }
                dynamicGen.SetTextPrompt(finalPrompt);
                dynamicGen.SetHistoryDisplayPrompt(textPrompt.Trim());
                dynamicGen.SetImagePaths(hasImage ? effectiveReferencePaths : null);
            }

            string assetGuid = targetImageAsset?.guid ?? "";
            EditorCoroutineUtility.StartCoroutineOwnerless(
                _pipeline.StartGeneration(_currentGenerator, assetGuid)
            );
        }

        // ========== IGenerationPipelineHost ==========
        public TJGeneratorsAssetReference GetTargetAsset() => targetImageAsset;

        public void StartGeneration(ModelGeneratorBase generator)
        {
            if (generator == _currentGenerator)
                StartGeneration();
        }


        public void ShowPreviewModel(string assetPath)
        {
            if (generationHistory != null && !string.IsNullOrEmpty(assetPath))
            {
                int index = generationHistory.FindIndex(x => x.imagePath == assetPath || x.modelPath == assetPath);
                if (index >= 0)
                {
                    selectedHistoryIndex = index;
                }
            }
            // 图片窗口不需要 3D/Prefab 预览
        }

        public string GetTextureSavePath(ModelGeneratorBase generator)
        {
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators"))
                AssetDatabase.CreateFolder("Assets", "TJGenerators");
            if (!AssetDatabase.IsValidFolder("Assets/TJGenerators/History"))
                AssetDatabase.CreateFolder("Assets/TJGenerators", "History");

            // 先用 .jpg 占位符路径（主图 savePath 会保留这个扩展名）
            string uniqueName = "Image_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg";
            return AssetDatabase.GenerateUniqueAssetPath(
                "Assets/TJGenerators/History/" + uniqueName
            );
        }

        public void OnTextureSaved(string savePath, ModelGeneratorBase generator)
        {
            // 地形高度图后处理改为「一键生成地形」时执行，此处仅保留后端原图

            // 设置 history 文件本身的导入器
            var textureImporter = AssetImporter.GetAtPath(savePath) as TextureImporter;
            if (textureImporter != null)
            {
                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.SaveAndReimport();
            }

            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(savePath));

            // 同步更新绑定资产：扩展名变化时自动删除旧占位并重写历史 GUID，避免残留同名异扩展名文件
            if (!ReplaceTargetImageFromSource(savePath, TJGeneratorsL10n.L("已生成图片并复制到"), out string replaceErr))
                TJLog.LogWarning($"{LogTag} 同步目标图片失败: {replaceErr}");

            // 更新生成状态（历史刷新由 GenerationPipeline 在 CompletePlaceholder 后统一处理）
            generationStatus = TJGeneratorsL10n.L("完成");
            generationProgress = 1f;
            isGenerating = false;

            // Frontier 序列帧：生成完成后自动执行绿幕抠图 + 切片 + 创建 AnimationClip
            if (IsFrontierSequenceMode())
            {
                var frontierAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(savePath);
                TJGeneratorsGenerationLabel.EnableFrontierLabel(frontierAsset);
                if (targetImageAsset != null && targetImageAsset.IsValid())
                {
                    var targetAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetImageAsset.GetPath());
                    TJGeneratorsGenerationLabel.EnableFrontierLabel(targetAsset);
                }
                AutoPostProcessFrontierSequence(savePath);
            }
        }

        /// <summary>
        /// Frontier 序列帧自动后处理：绿幕抠图 → 按网格切片导出 Sprite → 创建 AnimationClip → PingObject。
        /// </summary>
        private void AutoPostProcessFrontierSequence(string sourceAssetPath)
        {
            try
            {
                Texture2D sourceTex = SpriteSequencePostProcessService.LoadReadableTextureFromAssetPath(sourceAssetPath);
                if (sourceTex == null)
                {
                    TJLog.LogWarning($"{LogTag} Frontier 自动后处理：无法读取源图 {sourceAssetPath}");
                    return;
                }

                // 绿幕抠图
                Texture2D cutoutTex = SpriteSequencePostProcessService.BuildGreenScreenCutoutTexture(
                    sourceTex, chromaKeyTolerance, chromaFeather);
                DestroyImmediate(sourceTex);

                if (cutoutTex == null)
                {
                    TJLog.LogWarning($"{LogTag} Frontier 自动后处理：绿幕抠图失败");
                    return;
                }

                // 将抠图结果写回源路径（覆盖原图），后续预览和切片都使用抠图后的版本
                string cutoutAbsPath = PathUtils.ToAbsoluteAssetPath(sourceAssetPath);
                File.WriteAllBytes(cutoutAbsPath, cutoutTex.EncodeToPNG());
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceUpdate);
                var cutoutImporter = AssetImporter.GetAtPath(sourceAssetPath) as TextureImporter;
                if (cutoutImporter != null)
                {
                    cutoutImporter.textureType = TextureImporterType.Default;
                    cutoutImporter.alphaIsTransparency = true;
                    cutoutImporter.SaveAndReimport();
                }

                // 同步更新目标资产（ReplaceTargetImageFromSource 已将原图复制到目标路径，需同步抠图版本）
                if (targetImageAsset != null && targetImageAsset.IsValid())
                {
                    string targetPath = targetImageAsset.GetPath();
                    if (!string.IsNullOrEmpty(targetPath))
                    {
                        string targetAbs = PathUtils.ToAbsoluteAssetPath(targetPath);
                        File.WriteAllBytes(targetAbs, cutoutTex.EncodeToPNG());
                        AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                        var tImporter = AssetImporter.GetAtPath(targetPath) as TextureImporter;
                        if (tImporter != null)
                        {
                            tImporter.textureType = TextureImporterType.Default;
                            tImporter.alphaIsTransparency = true;
                            tImporter.SaveAndReimport();
                        }
                    }
                }

                // 切片 + AnimationClip
                var sliceResult = SpriteSequencePostProcessService.SliceTextureToSpritesAndAnimation(
                    cutoutTex,
                    sourceAssetPath,
                    spriteSliceColumns,
                    spriteSliceRows,
                    spriteSliceFps,
                    spriteSliceLoop
                );
                DestroyImmediate(cutoutTex);

                // 更新预览为抠图后结果
                processedPreviewTexture = SpriteSequencePostProcessService.LoadReadableTextureFromAssetPath(sourceAssetPath);
                if (processedPreviewTexture != null)
                {
                    processedPreviewSourcePath = sourceAssetPath;
                    processedPreviewValid = true;
                }

                // 给切割出的帧图片和 AnimationClip 打上 Frontier 标签
                foreach (var spritePath in sliceResult.SpriteAssetPaths)
                {
                    var spriteAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(spritePath);
                    TJGeneratorsGenerationLabel.EnableFrontierLabel(spriteAsset);
                }
                if (!string.IsNullOrEmpty(sliceResult.AnimationClipPath))
                {
                    var clipObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sliceResult.AnimationClipPath);
                    TJGeneratorsGenerationLabel.EnableFrontierLabel(clipObj);
                }

                // PingObject 到 AnimationClip 或切片目录
                if (!string.IsNullOrEmpty(sliceResult.AnimationClipPath))
                {
                    var clipAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sliceResult.AnimationClipPath);
                    if (clipAsset != null)
                    {
                        EditorGUIUtility.PingObject(clipAsset);
                        Selection.activeObject = clipAsset;
                    }
                }
                else
                {
                    var dirAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sliceResult.OutputDirectory);
                    if (dirAsset != null)
                        EditorGUIUtility.PingObject(dirAsset);
                }

                TJLog.Log($"{LogTag} Frontier 自动后处理完成：{sliceResult.ExportedCount} 帧，AnimationClip={sliceResult.AnimationClipPath}");
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"{LogTag} Frontier 自动后处理异常: {e.Message}");
            }
        }

        public string GetAudioSavePath(ModelGeneratorBase generator) => null;

        public void OnAudioSaved(string savePath, ModelGeneratorBase generator) { }

        public string GetVideoSavePath(ModelGeneratorBase generator) => null;
        public void OnVideoSaved(string savePath, ModelGeneratorBase generator) { }

        void IGenerationPipelineHost.Repaint()
        {
            Repaint();
        }

        /// <summary>
        /// 允许一键生成：已完成、本地 PNG 存在；且（历史里保存了地形模板 id，或当前窗口正选中地形高度图模板）。
        /// 避免仅依赖 <see cref="TJGeneratorsGenerationHistoryItem.promptTemplateId"/>（旧历史或序列化前记录为空时按钮长期灰色）。
        /// </summary>
        private bool CanGenerateTerrainFromHistoryItem(TJGeneratorsGenerationHistoryItem item)
        {
            if (item == null || item.isGenerating || string.IsNullOrEmpty(item.modelPath))
                return false;
            if (!item.modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!File.Exists(PathUtils.ToAbsoluteAssetPath(item.modelPath)))
                return false;

            if (
                string.Equals(
                    item.promptTemplateId,
                    UnityTerrainHeightmapTemplateId,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return true;

            return IsUnityTerrainHeightmapTemplateSelected();
        }

        /// <summary>
        /// 复制历史中的原始高度图 → 后处理写入单独 PNG → 按 PNG 宽高设置 Terrain 世界尺寸并创建场景地形。
        /// </summary>
        private void GenerateTerrainFromHeightmap(int historyIndex)
        {
            if (historyIndex < 0 || historyIndex >= generationHistory.Count)
                return;

            var item = generationHistory[historyIndex];
            if (!CanGenerateTerrainFromHistoryItem(item))
            {
                ErrorDialogUtils.ShowErrorDialog(
                    TJGeneratorsL10n.L("无法生成地形"),
                    TJGeneratorsL10n.L("请选择由「Unity 地形高度图」模板生成且已完成的 PNG 历史记录。"),
                    LogTag
                );
                return;
            }

            var hmOpts = new TerrainHeightmapPostProcessOptions
            {
                median3x3 = terrainHeightmapMedian3x3,
                gaussianBlur = terrainHeightmapGaussianBlur,
                gaussianSigma = terrainHeightmapBlurSigma,
                percentileNormalization = terrainHeightmapPercentileNormalize,
                percentileLow = terrainHeightmapPercentileLow,
                percentileHigh = terrainHeightmapPercentileHigh,
                heightGamma = terrainHeightmapHeightGamma,
                remapOutputMin = terrainHeightmapRemapOutMin,
                remapOutputMax = Mathf.Max(
                    terrainHeightmapRemapOutMax,
                    terrainHeightmapRemapOutMin + 0.01f
                ),
            };

            var (_, _, _, error) = TerrainCreationUtils.PostProcessAndCreateTerrain(
                item.modelPath, hmOpts);

            if (!string.IsNullOrEmpty(error))
                ErrorDialogUtils.ShowErrorDialog(TJGeneratorsL10n.L("地形生成失败"), error, LogTag);

            Repaint();
        }

        // ========== 辅助方法 ==========
        private string GetCurrentImageAssetGuid() => targetImageAsset?.guid ?? "";

        private void OnModelSelected(AIModelInfo model) => OnModelSelectedBase(model);

        private void ApplyForcedGeneratorFilterIfNeeded()
        {
            if (string.IsNullOrEmpty(forcedGeneratorId) || _generators == null || _generators.Count == 0)
                return;

            var filtered = new List<ModelGeneratorBase>();
            for (int i = 0; i < _generators.Count; i++)
            {
                var g = _generators[i];
                if (string.Equals(g.GeneratorId, forcedGeneratorId, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(g);
            }
            if (filtered.Count == 0)
                return;

            _generators = filtered;
            _currentGeneratorIndex = 0;
            _currentGenerator = _generators[0];
            currentSelectedModel = BuildModelInfoFromGenerator(_currentGenerator);
            EnsureWindowTitle();
        }

        private void EnsureWindowTitle()
        {
            if (titleContent != null && !string.IsNullOrEmpty(titleContent.text))
                return;

            string title = string.IsNullOrEmpty(forcedGeneratorId)
                ? TJGeneratorsL10n.L("TJGenerators 图片生成")
                : TJGeneratorsL10n.L("TJGenerators 序列帧（Frontier）");
            titleContent = new GUIContent(title);
        }

        private void LoadFrontierSequenceProfilesIfNeeded()
        {
            frontierSequenceProfilesRoot = null;
            frontierSequenceResolvedConfigPath = null;
            frontierSequenceProfileIds.Clear();
            frontierSequenceProfileNames.Clear();
            frontierSequenceProfileIndex = 0;

            if (!IsFrontierSequenceMode())
            {
                frontierSequenceProfileId = null;
                return;
            }

            try
            {
                if (!FrontierSequenceProfileConfigLoader.TryLoad(out frontierSequenceProfilesRoot, out frontierSequenceResolvedConfigPath)
                    || frontierSequenceProfilesRoot == null)
                    return;
                var profiles = frontierSequenceProfilesRoot["profiles"] as JArray;
                if (profiles == null || profiles.Count == 0)
                    return;

                foreach (var token in profiles)
                {
                    if (!(token is JObject item))
                        continue;
                    string id = item["id"]?.ToString();
                    if (string.IsNullOrEmpty(id))
                        continue;
                    string name = item["name"]?.ToString();
                    string displayName = string.IsNullOrEmpty(name) ? id : $"{TJGeneratorsL10n.L(name)} ({id})";
                    frontierSequenceProfileIds.Add(id);
                    frontierSequenceProfileNames.Add(displayName);
                }

                if (frontierSequenceProfileIds.Count == 0)
                    return;

                string defaultId = frontierSequenceProfilesRoot["defaultProfileId"]?.ToString();
                string targetId = !string.IsNullOrEmpty(frontierSequenceProfileId) ? frontierSequenceProfileId : defaultId;
                int idx = !string.IsNullOrEmpty(targetId)
                    ? frontierSequenceProfileIds.FindIndex(x => string.Equals(x, targetId, StringComparison.OrdinalIgnoreCase))
                    : -1;
                frontierSequenceProfileIndex = idx >= 0 ? idx : 0;
                frontierSequenceProfileId = frontierSequenceProfileIds[frontierSequenceProfileIndex];

                ApplySliceSettingsFromSelectedProfile();
            }
            catch (Exception e)
            {
                TJLog.LogWarning($"{LogTag} 读取序列帧模板配置失败: {e.Message}");
            }
        }

        /// <summary>
        /// 从当前选中的 Frontier profile 读取 sliceColumns / sliceRows 并同步到窗口切片参数。
        /// </summary>
        private void ApplySliceSettingsFromSelectedProfile()
        {
            var profileObj = FindSelectedProfileObject();
            if (profileObj == null)
                return;

            JToken colsToken = profileObj["sliceColumns"];
            JToken rowsToken = profileObj["sliceRows"];
            if (colsToken != null && int.TryParse(colsToken.ToString(), out int cols) && cols > 0)
                spriteSliceColumns = cols;
            if (rowsToken != null && int.TryParse(rowsToken.ToString(), out int rows) && rows > 0)
                spriteSliceRows = rows;
        }

        private JObject FindSelectedProfileObject()
        {
            if (frontierSequenceProfilesRoot == null || string.IsNullOrEmpty(frontierSequenceProfileId))
                return null;
            var profiles = frontierSequenceProfilesRoot["profiles"] as JArray;
            if (profiles == null)
                return null;
            foreach (var token in profiles)
            {
                if (!(token is JObject item))
                    continue;
                if (string.Equals(item["id"]?.ToString(), frontierSequenceProfileId, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private string BuildFrontierSequenceEnvelopeRawFromSelectedProfile(List<string> userReferenceImagePaths)
        {
            if (!IsFrontierSequenceMode() || frontierSequenceProfilesRoot == null || string.IsNullOrEmpty(frontierSequenceProfileId))
                return null;

            var profiles = frontierSequenceProfilesRoot["profiles"] as JArray;
            if (profiles == null)
                return null;

            JObject profile = null;
            foreach (var token in profiles)
            {
                if (!(token is JObject item))
                    continue;
                if (string.Equals(item["id"]?.ToString(), frontierSequenceProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    profile = item;
                    break;
                }
            }
            if (profile == null)
                return null;

            var envelope = new JObject
            {
                ["instructions"] = profile["instructions"]?.ToString() ?? "",
                ["knowledge_refs"] = profile["knowledge_refs"] is JArray refs
                    ? (JArray)refs.DeepClone()
                    : new JArray(),
                ["reference_channel_policy"] = new JObject
                {
                    ["user_reference_channel"] = "imageUrls",
                    ["knowledge_reference_channel"] = "frontier_sequence_envelope.knowledge_refs",
                    ["identity_priority"] = "user_reference_first",
                    ["knowledge_usage"] = "layout_alignment_only"
                },
                ["user_reference_refs"] = BuildUserReferenceRefs(userReferenceImagePaths)
            };
            return envelope.ToString();
        }

        private List<string> BuildEffectiveReferenceImagePaths(List<string> userReferenceImagePaths)
        {
            return BuildEffectiveReferenceImagePathsWithUserCount(userReferenceImagePaths).paths;
        }

        /// <summary>
        /// Frontier 序列帧：合并用户参考图与 profile 内 knowledge 本地图；<paramref name="userImageCount"/> 为「前半段」张数（用户上传），后半段为布局参考。
        /// </summary>
        private (List<string> paths, int userImageCount) BuildEffectiveReferenceImagePathsWithUserCount(
            List<string> userReferenceImagePaths
        )
        {
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int userImageCount = 0;

            if (userReferenceImagePaths != null)
            {
                for (int i = 0; i < userReferenceImagePaths.Count; i++)
                {
                    string p = NormalizeToAbsoluteImagePath(userReferenceImagePaths[i]);
                    if (string.IsNullOrEmpty(p) || !File.Exists(p) || !seen.Add(p))
                        continue;
                    merged.Add(p);
                    userImageCount++;
                }
            }

            var knowledgePaths = GetKnowledgeLocalImagePathsFromSelectedProfile();
            for (int i = 0; i < knowledgePaths.Count; i++)
            {
                string p = NormalizeToAbsoluteImagePath(knowledgePaths[i]);
                if (string.IsNullOrEmpty(p) || !File.Exists(p) || !seen.Add(p))
                    continue;
                merged.Add(p);
            }

            return (merged, userImageCount);
        }

        private List<string> GetKnowledgeLocalImagePathsFromSelectedProfile()
        {
            var paths = new List<string>();
            if (!IsFrontierSequenceMode() || frontierSequenceProfilesRoot == null || string.IsNullOrEmpty(frontierSequenceProfileId))
                return paths;

            var profiles = frontierSequenceProfilesRoot["profiles"] as JArray;
            if (profiles == null)
                return paths;

            JObject profile = null;
            foreach (var token in profiles)
            {
                if (!(token is JObject item))
                    continue;
                if (string.Equals(item["id"]?.ToString(), frontierSequenceProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    profile = item;
                    break;
                }
            }

            if (!(profile?["knowledge_refs"] is JArray refs) || refs.Count == 0)
                return paths;

            foreach (var token in refs)
            {
                if (!(token is JObject item))
                    continue;
                string localPath = item["local_path"]?.ToString();
                if (string.IsNullOrEmpty(localPath))
                    localPath = item["image_path"]?.ToString();
                if (string.IsNullOrEmpty(localPath))
                    localPath = item["path"]?.ToString();
                if (!string.IsNullOrEmpty(localPath))
                    paths.Add(localPath);
            }

            return paths;
        }

        private static string NormalizeToAbsoluteImagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (Path.IsPathRooted(path))
                return path;
            return PathUtils.ToAbsoluteAssetPath(path.Replace("\\", "/"));
        }

        private static JArray BuildUserReferenceRefs(List<string> userReferenceImagePaths)
        {
            var result = new JArray();
            if (userReferenceImagePaths == null || userReferenceImagePaths.Count == 0)
                return result;

            for (int i = 0; i < userReferenceImagePaths.Count; i++)
            {
                string path = userReferenceImagePaths[i];
                if (string.IsNullOrEmpty(path))
                    continue;

                result.Add(new JObject
                {
                    ["index"] = i,
                    ["source"] = "user_upload",
                    ["role"] = "identity_primary",
                    ["path"] = path,
                    ["name"] = Path.GetFileName(path)
                });
            }

            return result;
        }

        private static string ExtractInstructionsFromEnvelopeRaw(string envelopeRaw)
        {
            if (string.IsNullOrEmpty(envelopeRaw))
                return null;
            try
            {
                var obj = JObject.Parse(envelopeRaw);
                return obj["instructions"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string BuildPromptWithInstructionsFallback(string prompt, string instructions)
        {
            if (string.IsNullOrWhiteSpace(instructions))
                return prompt ?? "";
            if (string.IsNullOrWhiteSpace(prompt))
                return instructions;
            return instructions + "\n\n【用户补充】\n" + prompt.Trim();
        }

        protected override void ResetInputStateAfterModelChange()
        {
            var config = GetCurrentGeneratorConfig();
            ResetTextPromptIfHidden(config, ref textPrompt);
            ClearReferenceImagesWhenUploadHidden(config, referenceImagePaths, referenceUploadedImages);
        }

        protected override void OnModelSelectedBase(AIModelInfo model)
        {
            base.OnModelSelectedBase(model);
            selectedPromptTemplate = null;
            if (_currentGenerator is DynamicGenerator dg)
                dg.SetPromptTemplateSelection(null);
            UploadImageComponents.TrimReferenceImagesToMax(
                referenceImagePaths,
                referenceUploadedImages,
                GetMaxReferenceImages());
        }

        private void EnsureTargetImage()
        {
            // 初始化阶段：只在未绑定/无效时创建占位图，不强制改动用户已绑定的扩展名。
            if (targetImageAsset != null && targetImageAsset.IsValid())
                return;

            EnsureTargetImage(".jpg");
        }

        private void EnsureTargetImage(string desiredExt)
        {
            desiredExt = (desiredExt ?? ".jpg").Trim();
            if (!desiredExt.StartsWith("."))
                desiredExt = "." + desiredExt;
            desiredExt = desiredExt.ToLowerInvariant();

            // 目标已有效时直接使用（无论扩展名是否与 desiredExt 一致）；
            // 后续 ReplaceTargetImageFromSource 会在保存实际结果时处理扩展名变化。
            if (targetImageAsset != null && targetImageAsset.IsValid())
                return;

            string folder = PathUtils.GetProjectBrowserInsertionFolderAssetPath();
            // 跨所有常见图片扩展名检查同基名占用，避免与同名但不同后缀的文件冲突（如已有 New Image.png 时不创建 New Image.jpg）。
            string path = TJGeneratorsImageAssetPathUtility.GenerateUniqueImagePath(
                $"{folder}/New Image{desiredExt}"
            );
            path = CreateBlankImage(path);

            if (string.IsNullOrEmpty(path))
            {
                TJLog.LogError($"{LogTag} 无法创建图片资产");
                return;
            }

            targetImageAsset = TJGeneratorsAssetReference.FromPath(path);
            titleContent = new GUIContent(
                string.Format(TJGeneratorsL10n.L("TJGenerators 图片 - {0}"), Path.GetFileNameWithoutExtension(path))
            );

            if (!string.IsNullOrEmpty(targetImageAsset.guid))
                imageOpenWindows[targetImageAsset.guid] = this;

            Repaint();
        }

        /// <summary>
        /// 创建空白图片资产（根据扩展名创建 JPG/PNG）。
        /// </summary>
        public static string CreateBlankImage(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
            {
                path = Path.ChangeExtension(path, ".jpg");
                ext = ".jpg";
            }

            string absolutePath = PathUtils.ToAbsoluteAssetPath(path);

            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var blank =
                ext == ".png"
                    ? new Texture2D(4, 4, TextureFormat.RGBA32, false)
                    : new Texture2D(4, 4, TextureFormat.RGB24, false);
            var pixels = new Color[16];
            // 与「生成精灵」占位一致：PNG 全透明；JPG 无 alpha 时用与历史缩略图占位相近的深灰，避免一开始整片发白。
            Color fill = ext == ".png" ? Color.clear : new Color(0.2f, 0.2f, 0.2f);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = fill;
            blank.SetPixels(pixels);
            blank.Apply();

            if (ext == ".png")
            {
                File.WriteAllBytes(absolutePath, blank.EncodeToPNG());
            }
            else
            {
                File.WriteAllBytes(absolutePath, blank.EncodeToJPG(75));
            }
            DestroyImmediate(blank);

            // 导入并设置类型
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.SaveAndReimport();
            }

            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(path));
            return path;
        }
    }
}
#endif
