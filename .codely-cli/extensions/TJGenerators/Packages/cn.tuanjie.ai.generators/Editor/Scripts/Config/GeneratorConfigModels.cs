using System;
using System.Collections.Generic;
using UnityEngine;

namespace TJGenerators.Config
{
    /// <summary>
    /// 服务端配置根对象
    /// </summary>
    [Serializable]
    public class RemoteConfig
    {
        public string version;
        public string apiBaseUrl;
        /// <summary>Codely 资产搜索后端根地址（不含路径）</summary>
        public string codelyBaseUrl;
        /// <summary>仅在 TJGENERATORS_DEBUG 模式下生效，覆盖 apiBaseUrl，用于本地调试（如 http://localhost:7777/api/editor/）</summary>
        public string debugApiBaseUrl;
        public PollConfig pollConfig;
        public GlobalEndpointsConfig globalEndpoints;
        public RequestHeadersConfig requestHeaders;
        /// <summary>
        /// 显式默认模型 ID（可选）。当配置中指定时，用作新安装/无用户偏好的首选模型。
        /// </summary>
        public string defaultModelId;
        /// <summary>3D 模型生成器列表（主窗口）</summary>
        public List<GeneratorConfig> generators;
        /// <summary>通用文生图/图生图 </summary>
        public List<GeneratorConfig> imageGenerators;
        /// <summary>AI 参考图生成（AIReferenceImageWindow）</summary>
        public List<ImageGeneratorConfig> referenceImageGenerators;
        public List<GeneratorConfig> skyboxGenerators;
        /// <summary>Sprite 生成器列表（Sprite 窗口）</summary>
        public List<GeneratorConfig> spriteGenerators;
        /// <summary>2D 序列帧（动作）生成器列表（序列帧窗口）</summary>
        public List<GeneratorConfig> spriteSequenceGenerators;
        /// <summary>Material 生成器列表（Material 窗口）</summary>
        public List<GeneratorConfig> materialGenerators;
        /// <summary>文生音频生成器列表（文生音频窗口）</summary>
        public List<GeneratorConfig> musicGenerators;
        /// <summary>视频生成器列表（视频窗口）</summary>
        public List<GeneratorConfig> videoGenerators;
        /// <summary>Sprite 窗口共享的类型选择器配置，当单个生成器未配置时回退使用</summary>
        public TypeSelectorConfig spriteTypeSelector;
        /// <summary>Sprite 窗口共享的风格选择器配置，当单个生成器未配置时回退使用</summary>
        public StyleSelectorConfig spriteStyleSelector;
    }

    /// <summary>
    /// 全局端点配置
    /// </summary>
    [Serializable]
    public class GlobalEndpointsConfig
    {
        public string userInfo = "user/me";
        public string pollStatus = "task/{taskId}/id-status";
    }

    [Serializable]
    public class PollConfig
    {
        public int maxRetries = 360;
        public float intervalSeconds = 8f;
        public float requestTimeoutSeconds = 30f;
        public float downloadTimeoutSeconds = 300f;
        public float apiTimeoutSeconds = 60f;
    }

    /// <summary>
    /// 请求头配置
    /// </summary>
    [Serializable]
    public class RequestHeadersConfig
    {
        public string source = "codely";
    }

    /// <summary>
    /// 单个生成器的配置
    /// </summary>
    [Serializable]
    public class GeneratorConfig
    {
        public string id;
        public string displayName;
        public bool enabled = true;
        public string outputType = "model";  // "model" | "texture" | "cubemap" | "sprite" | "sprite_sequence" | "material" | "audio" | "image" | "video"
        public string audioFormat = "wav";   // "wav" | "mp3" | "mp4" … — only relevant when outputType == "audio"
        public List<EndpointConfig> endpoints;
        public List<ParameterConfig> parameters;
        public PostProcessingConfig postProcessing;
        public ResponseMappingConfig responseMapping;
        public UILayoutConfig uiLayout;  // UI布局配置
        public ModelSelectorConfig modelSelector;
        public TypeSelectorConfig typeSelector;      // 类型选择器配置（Sprite用）
        public StyleSelectorConfig styleSelector;    // 风格选择器配置（Sprite用）
        public MaterialTemplateSelectorConfig materialPresetSelector;  // 材质预设选择器配置（Material用）
        public MaterialTemplateSelectorConfig texturePatternSelector;  // 纹理走势选择器配置（Material用）
        public MaterialTemplateSelectorConfig materialStyleSelector;  // 风格状态选择器配置（Material用）
        public MaterialTemplateSelectorConfig promptTemplateSelector;
        public string imageBase64FieldName; // 有的模型期望配置为"image"，而不是"imageBase64"
        public bool imageBase64AsArray = false; // 是否将imageBase64作为数组发送
        public bool imageBase64WithPrefix = false; // 是否添加 data:image/xxx;base64, 前缀（Meshy 需要）
        public string imageUrlsFieldName; // 图生图时参考图 URL 数组的字段名，如 "imageUrls"
        public string textInputFieldName; // 文本输入字段名，默认为"prompt"，混元Motion等使用"inputText"
        public MultiViewFieldNamesConfig multiViewFieldNames; // 多视图字段名映射（用于混元等需要分别字段的API）
        /// <summary>写死在请求体中的字段（不在面板展示），与 referenceImageGenerators.request.fixedFields 同结构。</summary>
        public List<ImageGenFixedField> fixedFields;

        public string GetEndpoint(string key)
        {
            if (endpoints == null) return null;
            var ep = endpoints.Find(e => e.key == key);
            return ep?.value;
        }
    }

    [Serializable]
    public class EndpointConfig
    {
        public string key;
        public string value;
    }

    /// <summary>
    /// 多视图字段名映射配置（用于混元等需要分别字段的API）
    /// </summary>
    [Serializable]
    public class MultiViewFieldNamesConfig
    {
        public string front;   // 正视图字段名，如 "frontImage"
        public string back;    // 后视图字段名，如 "backImage"
        public string left;    // 左视图字段名，如 "leftImage"
        public string right;   // 右视图字段名，如 "rightImage"
    }

    /// <summary>
    /// 后处理配置
    /// </summary>
    [Serializable]
    public class PostProcessingConfig
    {
        public float modelScale = 1f;
        /// <summary>为 true 时，FBX 后处理将 ModelImporter 的 Rig 设为 Humanoid。</summary>
        public bool isHumanoid = false;
        /// <summary>
        /// 为 true 时，在「动画在同一主 FBX 内、且无单独 animation/walk/run URL」的流程末尾，
        /// 从主 FBX 取出剪辑并创建仅含 default 状态 + 自循环过渡的 AnimatorController（适用于混元 Motion 等）。
        /// </summary>
        public bool singleClipLoopAnimatorController = false;
        /// <summary>为 true 时在 3D 窗口显示「后处理 / 添加动作」面板（UniRig + 混元 Motion）。</summary>
        public bool enableMotion = false;
        public bool applyScaleToVertices = false;
        public Vector3Config rotation;
        public ImportSettingsConfig importSettings;
    }

    [Serializable]
    public class Vector3Config
    {
        public float x = 0;
        public float y = 0;
        public float z = 0;
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [Serializable]
    public class ImportSettingsConfig
    {
        public string materialImportMode;
        public string animationType;
        public bool importBlendShapes = true;
        public bool importVisibility = true;
        public bool importCameras = false;
        public bool importLights = false;
    }

    /// <summary>
    /// API响应字段映射
    /// </summary>
    [Serializable]
    public class ResponseMappingConfig
    {
        public string downloadUrlPath;
        public string downloadUrlPathMultiview;
        public string previewUrlPath;
        public string convertDownloadUrlPath;  // 转换后的下载URL路径
        public string renderedImagePath;  // 渲染贴图URL路径（用于FBX主贴图）
        public string taskIdPath;
        public string progressPath;
        public string statusPath;
        
        // 动画相关字段（用于Meshy动画模型）
        public string animationUrlPath;  // 动画模型URL路径，如 "result.animation_fbx_url"
        public string walkingAnimationUrlPath;  // 行走动画URL路径，如 "result.basic_animations.walking_fbx_url"
        public string runningAnimationUrlPath;  // 奔跑动画URL路径，如 "result.basic_animations.running_fbx_url"
    }

    /// <summary>
    /// UI布局配置
    /// </summary>
    [Serializable]
    public class UILayoutConfig
    {
        public bool showTextInput = true;
        public bool showImageUpload = true;
        public bool showMultiView = false;
        public bool showObjSelector = false;  // 是否显示OBJ文件选择器（用于混元智能减面）
        public bool showFileUpload = false;   // 是否显示文件上传组件（用于UniRig等）
        public bool advancedFoldout = true;   // 高级设置是否默认折叠
        public string textInputLabel = "文本提示词";
        public string textInputPlaceholder = "在此处输入文本提示...";
        public string imageUploadLabel = "参考图片";
        public string multiViewLabel = "多视图生成（可选）";
        public string multiViewHint = "上传多角度图片生成3D模型，正面必需，至少2张图片";  // 多视图提示
        public string advancedLabel = "高级设置";
        public List<string> primaryParameterIds;  // 在主区域（折叠区外）显示的参数ID
        public string objSelectorLabel = "选择要减面的OBJ文件";  // OBJ选择器标签
        public string objSelectorHint = "提示：此功能用于对OBJ模型进行智能减面处理。列表中只显示可处理的模型（需要先生成OBJ格式的模型）。";  // OBJ选择器提示
        public string fileUploadLabel = "上传模型文件";  // 文件上传标签
        public string fileUploadHint = "支持 FBX、OBJ 格式的3D模型文件";  // 文件上传提示
        /// <summary>参考图上传上限；0 表示未配置（回退包内默认配置），1 为单图，大于 1 为多图。</summary>
        public int maxReferenceImages = 0;
        /// <summary>多视图模式最少需要上传的图片数</summary>
        public int multiViewMinRequired = 2;
    }

    [Serializable]
    public class ModelSelectorConfig
    {
        public string name;
        public string description;
        public List<string> functionTags;
        public List<string> vendorTags;
        public string iconPath;
        public bool pinned = false;
    }

    /// <summary>
    /// AI模型信息
    /// </summary>
    public class AIModelInfo
    {
        public string Id;
        public string Name;
        public string Description;
        public string[] FunctionTags;
        public string[] VendorTags;
        public Texture2D Icon;
        public bool IsPinned;
        public DateTime LastUsed;
        public int ConfigOrder;
    }

    [Serializable]
    internal class TJGeneratorsModelPreferenceCollection
    {
        public List<TJGeneratorsModelPreferenceItem> items = new List<TJGeneratorsModelPreferenceItem>();
    }

    [Serializable]
    internal class TJGeneratorsModelPreferenceItem
    {
        public string id;
        public bool isPinned;
        public long lastUsedTicks;
    }

    /// <summary>
    /// 可视化选择器选项配置（用于类型/风格/模型等选择）
    /// </summary>
    [Serializable]
    public class VisualSelectorOptionConfig
    {
        public string id;           // 唯一标识符，如 "weapon_melee"
        public string name;         // 显示名称，如 "近战武器"
        public string description;  // 描述文字
        public string iconPath;     // 本地图片路径（可选，相对于 Packages 或 Assets）
        public string iconUrl;      // 远程图片 URL（可选）
        public string category;     // 分类标签（用于筛选）
        public string[] tags;       // 自定义标签
        public string prompt;       // 生成提示词（可选）
        public int order;           // 显示顺序（越小越靠前）
        public bool pinned = false; // 是否置顶
    }

    /// <summary>
    /// 类型选择器配置
    /// </summary>
    [Serializable]
    public class TypeSelectorConfig
    {
        public bool enabled = true;
        public string title = "资产类型";
        public string description = "选择要生成的游戏资产类型";
        public List<VisualSelectorOptionConfig> options;
        public VisualSelectorOptionConfig defaultOption;  // 可为null表示无默认选择
    }

    /// <summary>
    /// 风格选择器配置
    /// </summary>
    [Serializable]
    public class StyleSelectorConfig
    {
        public bool enabled = true;
        public string title = "艺术风格";
        public string description = "选择美术风格";
        public List<VisualSelectorOptionConfig> options;
        public VisualSelectorOptionConfig defaultOption;  // 可为null表示无默认选择
    }

    /// <summary>
    /// 材质模板选择器配置
    /// </summary>
    [Serializable]
    public class MaterialTemplateSelectorConfig
    {
        public bool enabled = true;
        public string title = "材质模板";
        public string description = "选择材质纹理模板";
        public List<MaterialTemplateOptionConfig> options;
    }

    /// <summary>
    /// 材质模板选项配置
    /// </summary>
    [Serializable]
    public class MaterialTemplateOptionConfig
    {
        public string id;           // 唯一标识符，如 "smooth_metal"
        public string name;         // 显示名称，如 "光滑金属"
        public string description;  // 描述文字
        public string category;     // 分类标签（金属、木材、石材等）
        public string prompt;       // 生成提示词
        public string iconPath;     // 本地图片路径（可选）
        public int order;           // 显示顺序（越小越靠前）
    }

    /// <summary>
    /// 参数配置
    /// </summary>
    [Serializable]
    public class ParameterConfig
    {
        public string id;
        public string type;
        public string label;
        public string tooltip;
        public List<OptionConfig> options;
        public string defaultValue;
        public float min;
        public float max;
        public string dependsOn;
        public string dependsValue;
        public string apiFieldName;  // 默认字段名
        public string apiFieldNameImage;  // 图片模式字段名（可选）
        public string apiFieldNameMultiview;  // 多视图模式字段名（可选）
        public string valueType;
        public bool allowCustom;  // 为 true 时下拉框支持切换到自定义文本输入模式

        /// <summary>
        /// 根据输入模式获取API字段名
        /// </summary>
        public string GetApiFieldName(string inputMode)
        {
            switch (inputMode)
            {
                case "image":
                    return !string.IsNullOrEmpty(apiFieldNameImage) ? apiFieldNameImage : apiFieldName ?? id;
                case "multiview":
                    return !string.IsNullOrEmpty(apiFieldNameMultiview) ? apiFieldNameMultiview : apiFieldName ?? id;
                default:
                    return apiFieldName ?? id;
            }
        }
    }

    [Serializable]
    public class OptionConfig
    {
        public string value;
        public string label;
        public string description;
    }

    // ========== AI 参考图生成配置模型 ==========

    /// <summary>
    /// 图片生成器配置
    /// </summary>
    [Serializable]
    public class ImageGeneratorConfig
    {
        public string id;
        public string displayName;
        public bool enabled = true;
        public string endpoint;
        public ImageGenRequestConfig request;
        public ImageGenResponseConfig response;
        public ImageGenPromptsConfig systemPrompts;
    }

    [Serializable]
    public class ImageGenRequestConfig
    {
        public string promptField = "prompt";
        public List<ImageGenFixedField> fixedFields;
        /// <summary>参照图使用可访问的 URL 时写入的 JSON 字段名（火山 SeeDream 为 imageUrls，不是 imagesUrl）。</summary>
        public string referenceImagesField = "imagesUrl";
        /// <summary>
        /// 若配置（非空），多视图链式生成时优先将已下载的本地 PNG 以 base64 数组写入该字段（与 huoshan_seedream的 images 一致），
        /// 避免仅支持站内 URL 的接口收不到外链参考图。
        /// </summary>
        public string referenceImagesBase64Field;
    }

    [Serializable]
    public class ImageGenFixedField
    {
        public string key;
        public string value;
        public string type = "string";  // "string", "bool", "int", "float"
    }

    [Serializable]
    public class ImageGenResponseConfig
    {
        public string statusField = "status";
        public List<string> successValues;
        public string imageUrlPath;    // dot-separated path, e.g. "output.data.image_urls[0]"
        public string errorField = "error";
    }

    [Serializable]
    public class ImageGenPromptsConfig
    {
        public string single;
        public string multiViewFront;
        public string multiViewLeft;
        public string multiViewBack;
        public string multiViewRight;
    }
}
