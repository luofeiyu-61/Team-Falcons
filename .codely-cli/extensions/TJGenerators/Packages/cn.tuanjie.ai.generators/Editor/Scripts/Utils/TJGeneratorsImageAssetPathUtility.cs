#if UNITY_EDITOR
namespace TJGenerators.Utils
{
    /// <summary>
    /// 图片占位符路径工具：生成结果常为 .jpg，但占位文件可能是 .png，与 <see cref="UnityEditor.AssetDatabase.GenerateUniqueAssetPath"/>
    /// 只检查单一扩展名不同，本工具跨所有常见图片扩展名检查同名占用，避免创建与已有同基名文件冲突的占位符。
    /// </summary>
    public static class TJGeneratorsImageAssetPathUtility
    {
        private static readonly string[] k_ImageFileExtensions =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".tga",
            ".bmp",
            ".tif",
            ".tiff",
            ".exr",
            ".hdr",
            ".psd",
        };

        /// <summary>
        /// 在目标目录下生成唯一的图片占位路径：若同基名已存在任意常见图片扩展名，则递增序号（与 Unity「名称」「名称 1」「名称 2」规则一致）。
        /// 返回路径的扩展名与 <paramref name="preferredAssetPath"/> 一致。
        /// </summary>
        public static string GenerateUniqueImagePath(string preferredAssetPath)
        {
            if (string.IsNullOrEmpty(preferredAssetPath))
                preferredAssetPath = "Assets/New Image.jpg";

            return PathUtils.GenerateUniqueCrossExtensionPath(preferredAssetPath, k_ImageFileExtensions);
        }
    }
}
#endif
