using System;
using UnityEditor;
using UnityEngine;
using TJGenerators.AssetSearch;
using TJGenerators.Config;
using TJGenerators.Utils;

namespace TJGenerators
{
    /// <summary>
    /// TJGenerators 菜单项入口（薄层，业务逻辑见 <see cref="TJGeneratorsAssetCreation"/> 等）。
    /// </summary>
    public static class TJGeneratorsMenuItems
    {
        #region Menu Items

        // 优先级规则：生成/通用 2000+，工具/开发 3050+；英文整段 +100
        // 代码组织：每个功能的中文与英文菜单项相邻，便于维护

        #region AI - Generate (2000–2008 / 2100–2108)

        [MenuItem("AI/生成/生成3D模型", false, 2000)]
        public static void CreateAIGeneratedMesh() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreatePrefabAssetWithCallback($"{TJGeneratorsAssetCreation.DefaultNewAssetName}.prefab", enableLabel: true, sceneParentForInstance: null); }
        [MenuItem("AI/生成/生成3D模型", true)]
        public static bool Validate_CreateAIGeneratedMesh() => true;
        [MenuItem("AI/Generate/3D Model", false, 2100)]
        public static void CreateAIGeneratedMesh_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreatePrefabAssetWithCallback($"{TJGeneratorsAssetCreation.DefaultNewAssetName}.prefab", enableLabel: true, sceneParentForInstance: null); }
        [MenuItem("AI/Generate/3D Model", true)]
        public static bool Validate_CreateAIGeneratedMesh_En() => true;

        [MenuItem("AI/生成/生成天空盒", false, 2001)]
        public static void CreateAIGeneratedSkybox() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateSkyboxAssetWithCallback("New Skybox.png"); }
        [MenuItem("AI/生成/生成天空盒", true)]
        public static bool Validate_CreateAIGeneratedSkybox() => true;
        [MenuItem("AI/Generate/Skybox", false, 2101)]
        public static void CreateAIGeneratedSkybox_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateSkyboxAssetWithCallback("New Skybox.png"); }
        [MenuItem("AI/Generate/Skybox", true)]
        public static bool Validate_CreateAIGeneratedSkybox_En() => true;

        [MenuItem("AI/生成/生成精灵", false, 2002)]
        public static void CreateAIGeneratedSprite() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateSpriteAssetWithCallback("New Sprite.png"); }
        [MenuItem("AI/生成/生成精灵", true)]
        public static bool Validate_CreateAIGeneratedSprite() => true;
        [MenuItem("AI/Generate/2D Sprite", false, 2102)]
        public static void CreateAIGeneratedSprite_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateSpriteAssetWithCallback("New Sprite.png"); }
        [MenuItem("AI/Generate/2D Sprite", true)]
        public static bool Validate_CreateAIGeneratedSprite_En() => true;

        [MenuItem("AI/生成/生成表面材质", false, 2003)]
        public static void CreateAIGeneratedMaterial() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateMaterialAssetWithCallback("New Material.mat"); }
        [MenuItem("AI/生成/生成表面材质", true)]
        public static bool Validate_CreateAIGeneratedMaterial() => true;
        [MenuItem("AI/Generate/Material", false, 2103)]
        public static void CreateAIGeneratedMaterial_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateMaterialAssetWithCallback("New Material.mat"); }
        [MenuItem("AI/Generate/Material", true)]
        public static bool Validate_CreateAIGeneratedMaterial_En() => true;

        [MenuItem("AI/生成/生成音频", false, 2004)]
        public static void CreateAIGeneratedMusic() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateAudioClipAssetWithCallback("New AudioClip.wav"); }
        [MenuItem("AI/生成/生成音频", true)]
        public static bool Validate_CreateAIGeneratedMusic() => true;
        [MenuItem("AI/Generate/Audio", false, 2104)]
        public static void CreateAIGeneratedMusic_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateAudioClipAssetWithCallback("New AudioClip.wav"); }
        [MenuItem("AI/Generate/Audio", true)]
        public static bool Validate_CreateAIGeneratedMusic_En() => true;

        [MenuItem("AI/生成/生成2D序列帧动画", false, 2005)]
        public static void CreateAIGeneratedSpriteSequence() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateAnimationClipAssetWithCallback("New Sprite Sequence.anim"); }
        [MenuItem("AI/生成/生成2D序列帧动画", true)]
        public static bool Validate_CreateAIGeneratedSpriteSequence() => true;
        [MenuItem("AI/Generate/2D Sequence", false, 2105)]
        public static void CreateAIGeneratedSpriteSequence_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateAnimationClipAssetWithCallback("New Sprite Sequence.anim"); }
        [MenuItem("AI/Generate/2D Sequence", true)]
        public static bool Validate_CreateAIGeneratedSpriteSequence_En() => true;

        [MenuItem("AI/生成/生成图片", false, 2006)]
        public static void OpenImageGenerationWindow() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateImageAssetWithCallback("New Image.jpg"); }
        [MenuItem("AI/生成/生成图片", true)]
        public static bool Validate_OpenImageGenerationWindow() => true;
        [MenuItem("AI/Generate/Image", false, 2106)]
        public static void OpenImageGenerationWindow_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateImageAssetWithCallback("New Image.jpg"); }
        [MenuItem("AI/Generate/Image", true)]
        public static bool Validate_OpenImageGenerationWindow_En() => true;

        [MenuItem("AI/生成/生成序列帧（Frontier）", false, 2007)]
        public static void OpenFrontierSequenceImageWindow() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateImageAssetWithCallback("New Image.jpg", openAsFrontierSequence: true); }
        [MenuItem("AI/生成/生成序列帧（Frontier）", true)]
        public static bool Validate_OpenFrontierSequenceImageWindow() => true;
        [MenuItem("AI/Generate/Sequence (Frontier)", false, 2107)]
        public static void OpenFrontierSequenceImageWindow_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateImageAssetWithCallback("New Image.jpg", openAsFrontierSequence: true); }
        [MenuItem("AI/Generate/Sequence (Frontier)", true)]
        public static bool Validate_OpenFrontierSequenceImageWindow_En() => true;

        [MenuItem("AI/生成/生成视频", false, 2008)]
        public static void CreateAIGeneratedVideo() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsAssetCreation.CreateVideoAssetWithCallback("New Video.mp4"); }
        [MenuItem("AI/生成/生成视频", true)]
        public static bool Validate_CreateAIGeneratedVideo() => true;
        [MenuItem("AI/Generate/Video", false, 2108)]
        public static void CreateAIGeneratedVideo_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateVideoAssetWithCallback("New Video.mp4"); }
        [MenuItem("AI/Generate/Video", true)]
        public static bool Validate_CreateAIGeneratedVideo_En() => true;

        #endregion

        #region AI - Common (2010–2012 / 2110–2112)

        [MenuItem("AI/搜索资产库", false, 2010)]
        public static void OpenCodelyAssetLibrarySearch() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); AssetLibrarySearchWindow.Open(); }
        [MenuItem("AI/搜索资产库", true)]
        public static bool Validate_OpenCodelyAssetLibrarySearch() => true;
        [MenuItem("AI/Asset Library", false, 2110)]
        public static void OpenCodelyAssetLibrarySearch_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); AssetLibrarySearchWindow.Open(); }
        [MenuItem("AI/Asset Library", true)]
        public static bool Validate_OpenCodelyAssetLibrarySearch_En() => true;

        [MenuItem("AI/搜索生成的资产", false, 2011)]
        public static void SearchAIGeneratedAssets()
        {
            EditorUtility.FocusProjectWindow();

            var projectBrowserType = typeof(Editor).Assembly.GetType("UnityEditor.ProjectBrowser");
            if (projectBrowserType != null)
            {
                var window = EditorWindow.GetWindow(projectBrowserType);
                if (window != null)
                {
                    var setSearchMethod = projectBrowserType.GetMethod("SetSearch",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                        null,
                        new[] { typeof(string) },
                        null);
                    setSearchMethod?.Invoke(window, new object[] { $"l:{TJGeneratorsGenerationLabel.Label}" });
                }
            }
        }
        [MenuItem("AI/搜索生成的资产", true)]
        public static bool Validate_SearchAIGeneratedAssets() => true;
        [MenuItem("AI/Search AI-Generated Assets", false, 2111)]
        public static void SearchAIGeneratedAssets_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); SearchAIGeneratedAssets(); }
        [MenuItem("AI/Search AI-Generated Assets", true)]
        public static bool Validate_SearchAIGeneratedAssets_En() => true;

        [MenuItem("AI/✦ 玩转 AI 生成", false, 2012)]
        public static void OpenTJGeneratorsDocumentation() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsDocs.OpenDocumentation(); }
        [MenuItem("AI/✦ 玩转 AI 生成", true)]
        public static bool Validate_OpenTJGeneratorsDocumentation() => true;
        [MenuItem("AI/✦ Explore AI Generation", false, 2112)]
        public static void OpenTJGeneratorsDocumentation_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsDocs.OpenDocumentation(); }
        [MenuItem("AI/✦ Explore AI Generation", true)]
        public static bool Validate_OpenTJGeneratorsDocumentation_En() => true;

        #endregion

        #region AI - Tools (3050 / 3150)

        [MenuItem("AI/工具/图片切割", false, 3050)]
        public static void OpenImageSliceWindow() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.Chinese); TJGeneratorsImageSliceWindow.ShowWindow(); }
        [MenuItem("AI/工具/图片切割", true)]
        public static bool Validate_OpenImageSliceWindow() => true;
        [MenuItem("AI/Tools/Image Slice", false, 3150)]
        public static void OpenImageSliceWindow_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsImageSliceWindow.ShowWindow(); }
        [MenuItem("AI/Tools/Image Slice", true)]
        public static bool Validate_OpenImageSliceWindow_En() => true;

        #endregion

#if TJGENERATORS_DEBUG
        #region AI - Dev (3055–3059 / 3155–3159)

        [MenuItem("AI/开发/运行生成测试", false, 3055)]
        public static void OpenGenerationTestRunnerWindow() { TJGeneratorsGenerationTestRunner.Open(); }
        [MenuItem("AI/Dev/Run Generation Test", false, 3155)]
        public static void OpenGenerationTestRunnerWindow_En() { TJGeneratorsGenerationTestRunner.Open(); }
        [MenuItem("AI/Dev/Run Generation Test", true)]
        public static bool Validate_OpenGenerationTestRunnerWindow_En() => true;

        [MenuItem("AI/开发/打印 Access Token", false, 3056)]
        public static void PrintAccessToken()
        {
            string token = Unity.UniAsset.Manager.Editor.InternalBridge.UnityConnectSession.instance.GetAccessToken();
            if (string.IsNullOrEmpty(token))
            {
                TJLog.LogWarning("[TJGenerators] Access Token 为空，请确认编辑器已登录 Unity 账号");
                ErrorDialogUtils.ShowErrorDialog("Access Token", TJGeneratorsL10n.L("Token 为空，请先登录 Unity 账号。"), "[TJGeneratorsMenuItems]");
            }
            else
            {
                TJLog.Log($"[TJGenerators] Access Token: {token}");
                EditorGUIUtility.systemCopyBuffer = token;
                TJLog.Log($"[TJGenerators] Token 已复制到剪贴板。\n{token}");
            }
        }
        [MenuItem("AI/Dev/Print Access Token", false, 3156)]
        public static void PrintAccessToken_En() { PrintAccessToken(); }
        [MenuItem("AI/Dev/Print Access Token", true)]
        public static bool Validate_PrintAccessToken_En() => true;

        [MenuItem("AI/开发/清除配置缓存并重新加载", false, 3057)]
        public static void ClearConfigCache()
        {
            ConfigManager.ClearCache();
            TJLog.Log(
                "[TJGenerators] 所有配置缓存已清除。请关闭并重新打开 TJGenerators 窗口以加载最新配置。"
                    + "\n本地配置文件：Editor/Config/GeneratorConfig.json"
            );
        }
        [MenuItem("AI/Dev/Clear Config Cache & Reload", false, 3157)]
        public static void ClearConfigCache_En() { ClearConfigCache(); }
        [MenuItem("AI/Dev/Clear Config Cache & Reload", true)]
        public static bool Validate_ClearConfigCache_En() => true;

        [MenuItem("AI/开发/生成纹理走势模板图", false, 3058)]
        public static void OpenMaterialTemplateGeneratorWindow() { TJGeneratorsMaterialTemplateGenerator.ShowWindow(); }
        [MenuItem("AI/Dev/Generate Texture Pattern Templates", false, 3158)]
        public static void OpenMaterialTemplateGeneratorWindow_En() { TJGeneratorsMaterialTemplateGenerator.ShowWindow(); }
        [MenuItem("AI/Dev/Generate Texture Pattern Templates", true)]
        public static bool Validate_OpenMaterialTemplateGeneratorWindow_En() => true;

        [MenuItem("AI/开发/一键清空所有历史记录", false, 3059)]
        public static void ClearAllGenerationHistory()
        {
            if (!EditorUtility.DisplayDialog(
                    TJGeneratorsL10n.L("清空历史记录"),
                    TJGeneratorsL10n.L("确定要清空所有 TJGenerators 生成历史记录吗？此操作不可撤销。"),
                    TJGeneratorsL10n.L("清空"),
                    TJGeneratorsL10n.L("取消")))
            {
                return;
            }
            TJGeneratorsHistoryManager.ClearAllHistory();
        }
        [MenuItem("AI/开发/清除所有历史记录", true)]
        public static bool Validate_ClearAllGenerationHistory() => true;
        [MenuItem("AI/Dev/Clear All History", false, 3159)]
        public static void ClearAllGenerationHistory_En() { ClearAllGenerationHistory(); }
        [MenuItem("AI/Dev/Clear All History", true)]
        public static bool Validate_ClearAllGenerationHistory_En() => true;

        #endregion
#endif

        #region Context - Assets/Create/3D

        [MenuItem("Assets/Create/3D/生成3D模型", false, -199)]
        public static void CreateTJGeneratorsMesh() { TJGeneratorsAssetCreation.CreatePrefabAssetWithCallback($"{TJGeneratorsAssetCreation.DefaultNewAssetName}.prefab", enableLabel: true, sceneParentForInstance: null); }
        [MenuItem("Assets/Create/3D/3D Model", false, -199)]
        public static void CreateTJGeneratorsMesh_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreatePrefabAssetWithCallback($"{TJGeneratorsAssetCreation.DefaultNewAssetName}.prefab", enableLabel: true, sceneParentForInstance: null); }
        [MenuItem("Assets/Create/3D/3D Model", true)]
        public static bool Validate_CreateTJGeneratorsMesh_En() => true;

        [MenuItem("Assets/Create/3D/生成天空盒", false, -198)]
        public static void CreateTJGeneratorsSkybox() { TJGeneratorsAssetCreation.CreateSkyboxAssetWithCallback("New Skybox.png"); }
        [MenuItem("Assets/Create/3D/Skybox", false, -198)]
        public static void CreateTJGeneratorsSkybox_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateSkyboxAssetWithCallback("New Skybox.png"); }
        [MenuItem("Assets/Create/3D/Skybox", true)]
        public static bool Validate_CreateTJGeneratorsSkybox_En() => true;

        [MenuItem("Assets/Create/3D/生成表面材质", false, -197)]
        public static void CreateTJGeneratorsMaterial() { TJGeneratorsAssetCreation.CreateMaterialAssetWithCallback("New Material.mat"); }
        [MenuItem("Assets/Create/3D/Material", false, -197)]
        public static void CreateTJGeneratorsMaterial_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateMaterialAssetWithCallback("New Material.mat"); }
        [MenuItem("Assets/Create/3D/Material", true)]
        public static bool Validate_CreateTJGeneratorsMaterial_En() => true;

        #endregion

        #region Context - GameObject/3D Object

        [MenuItem("GameObject/3D Object/生成3D模型", false, -1)]
        public static void CreateInSceneAndNameMesh() { TJGeneratorsAssetCreation.CreatePrefabAssetWithCallback($"{TJGeneratorsAssetCreation.DefaultNewAssetName}.prefab", enableLabel: false, sceneParentForInstance: Selection.activeGameObject); }
        [MenuItem("GameObject/3D Object/3D Model", false, -1)]
        public static void CreateInSceneAndNameMesh_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreatePrefabAssetWithCallback($"{TJGeneratorsAssetCreation.DefaultNewAssetName}.prefab", enableLabel: false, sceneParentForInstance: Selection.activeGameObject); }
        [MenuItem("GameObject/3D Object/3D Model", true)]
        public static bool Validate_CreateInSceneAndNameMesh_En() => true;

        [MenuItem("GameObject/3D Object/生成天空盒", false, 0)]
        public static void CreateInSceneAndNameSkybox() { TJGeneratorsAssetCreation.CreateSkyboxAssetWithCallback("New Skybox.png"); }
        [MenuItem("GameObject/3D Object/Skybox", false, 0)]
        public static void CreateInSceneAndNameSkybox_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateSkyboxAssetWithCallback("New Skybox.png"); }
        [MenuItem("GameObject/3D Object/Skybox", true)]
        public static bool Validate_CreateInSceneAndNameSkybox_En() => true;

        [MenuItem("GameObject/3D Object/生成表面材质", false, 1)]
        public static void CreateInSceneAndNameMaterial() { TJGeneratorsAssetCreation.CreateMaterialAssetWithCallback("New Material.mat"); }
        [MenuItem("GameObject/3D Object/Material", false, 1)]
        public static void CreateInSceneAndNameMaterial_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateMaterialAssetWithCallback("New Material.mat"); }
        [MenuItem("GameObject/3D Object/Material", true)]
        public static bool Validate_CreateInSceneAndNameMaterial_En() => true;

        #endregion

        #region Context - Assets/Create/2D

        [MenuItem("Assets/Create/2D/生成2D精灵", false, -199)]
        public static void CreateTJGeneratorsSprite() { TJGeneratorsAssetCreation.CreateSpriteAssetWithCallback("New Sprite.png"); }
        [MenuItem("Assets/Create/2D/2D Sprite", false, -199)]
        public static void CreateTJGeneratorsSprite_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateSpriteAssetWithCallback("New Sprite.png"); }
        [MenuItem("Assets/Create/2D/2D Sprite", true)]
        public static bool Validate_CreateTJGeneratorsSprite_En() => true;

        [MenuItem("Assets/Create/2D/生成图片", false, -196)]
        public static void CreateTJGeneratorsImage() { TJGeneratorsAssetCreation.CreateImageAssetWithCallback("New Image.jpg"); }
        [MenuItem("Assets/Create/2D/Image", false, -196)]
        public static void CreateTJGeneratorsImage_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateImageAssetWithCallback("New Image.jpg"); }
        [MenuItem("Assets/Create/2D/Image", true)]
        public static bool Validate_CreateTJGeneratorsImage_En() => true;

        [MenuItem("Assets/Create/2D/生成2D序列帧动画", false, -198)]
        public static void CreateTJGeneratorsSpriteSequence() { TJGeneratorsAssetCreation.CreateAnimationClipAssetWithCallback("New Sprite Sequence.anim"); }
        [MenuItem("Assets/Create/2D/2D Sequence", false, -198)]
        public static void CreateTJGeneratorsSpriteSequence_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateAnimationClipAssetWithCallback("New Sprite Sequence.anim"); }
        [MenuItem("Assets/Create/2D/2D Sequence", true)]
        public static bool Validate_CreateTJGeneratorsSpriteSequence_En() => true;

        #endregion

        #region Context - GameObject/2D Object

        [MenuItem("GameObject/2D Object/生成2D精灵", false, -1)]
        public static void CreateInSceneAndNameSprite() { TJGeneratorsAssetCreation.CreateSpriteAssetWithCallback("New Sprite.png"); }
        [MenuItem("GameObject/2D Object/2D Sprite", false, -1)]
        public static void CreateInSceneAndNameSprite_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateSpriteAssetWithCallback("New Sprite.png"); }
        [MenuItem("GameObject/2D Object/2D Sprite", true)]
        public static bool Validate_CreateInSceneAndNameSprite_En() => true;

        #endregion

        #region Context - Assets/Create/Audio

        [MenuItem("Assets/Create/Audio/生成音频", false, -199)]
        public static void CreateTJGeneratorsAudioClip() { TJGeneratorsAssetCreation.CreateAudioClipAssetWithCallback("New AudioClip.wav"); }
        [MenuItem("Assets/Create/Audio/Audio", false, -199)]
        public static void CreateTJGeneratorsAudioClip_En() { TJGeneratorsL10n.SetLanguage(TJGeneratorsL10n.Language.English); TJGeneratorsAssetCreation.CreateAudioClipAssetWithCallback("New AudioClip.wav"); }
        [MenuItem("Assets/Create/Audio/Audio", true)]
        public static bool Validate_CreateTJGeneratorsAudioClip_En() => true;

        #endregion

        #endregion
    }
}
