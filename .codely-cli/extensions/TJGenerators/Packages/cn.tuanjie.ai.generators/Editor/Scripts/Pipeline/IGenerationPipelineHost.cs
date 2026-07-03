#if UNITY_EDITOR
using TJGenerators.Generators;

namespace TJGenerators.Pipeline
{
    public interface IGenerationPipelineHost
    {
        TJGeneratorsAssetReference GetTargetAsset();
        void RefreshHistory();
        void ShowPreviewModel(string assetPath);
        void RefreshUserInfo();
        void Repaint();
        void StartGeneration(ModelGeneratorBase generator);
        void ShowDialog(string title, string message);
        
        /// <summary>
        /// 获取纹理/图片资产的保存路径（非3D模型资产使用）。
        /// 返回 null 表示该 Host 不处理纹理资产。
        /// </summary>
        string GetTextureSavePath(ModelGeneratorBase generator);
        
        /// <summary>
        /// 纹理/图片资产下载保存后的回调（用于设置 Import Settings、创建预览等）。
        /// </summary>
        void OnTextureSaved(string savePath, ModelGeneratorBase generator);

        /// <summary>
        /// 获取音频资产的保存路径（仅文生音频 Host 返回非空，扩展名随生成器配置，如文生音频常为 .mp4、音效常为 .mp3）。
        /// 返回 null 表示该 Host 不处理音频资产。
        /// </summary>
        string GetAudioSavePath(ModelGeneratorBase generator);

        /// <summary>
        /// 音频资产下载并导入后的回调（用于刷新历史、打标签等）。
        /// </summary>
        void OnAudioSaved(string savePath, ModelGeneratorBase generator);

        /// <summary>
        /// 获取视频资产的保存路径（仅视频 Host 返回非空，如 Assets/TJGenerators/History/Video_yyyyMMdd_HHmmss.mp4）。
        /// 返回 null 表示该 Host 不处理视频资产。
        /// </summary>
        string GetVideoSavePath(ModelGeneratorBase generator);

        /// <summary>
        /// 视频资产下载并导入后的回调（用于刷新历史、打标签等）。
        /// </summary>
        void OnVideoSaved(string savePath, ModelGeneratorBase generator);
    }
}
#endif
