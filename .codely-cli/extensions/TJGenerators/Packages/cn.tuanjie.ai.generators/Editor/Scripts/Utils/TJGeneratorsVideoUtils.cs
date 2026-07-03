#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

namespace TJGenerators.Utils
{
    /// <summary>
    /// 视频资产工具：创建合法 MP4 占位（纯黑单帧）并导入为 VideoClip。
    /// </summary>
    public static class TJGeneratorsVideoUtils
    {
        private const string TemplateFileName = "TJGeneratorsBlankVideo.mp4";

        /// <summary>
        /// 在指定路径写入黑场占位 MP4 并导入。生成完成后会被实际视频覆盖。
        /// </summary>
        public static string CreateBlankVideoClip(string path)
        {
            path = Path.ChangeExtension(path, ".mp4");
            string absolutePath = PathUtils.ToAbsoluteAssetPath(path);
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory))
                PathUtils.EnsureAssetFolder(directory);

            File.WriteAllBytes(absolutePath, GetTemplateBytes());
            ImportPlaceholder(path);
            return path;
        }

        /// <summary>
        /// 占位 MP4 是否为空或缺少 ftyp 头（旧版空文件占位）。
        /// </summary>
        public static bool NeedsPlaceholderRepair(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            string absolutePath = PathUtils.ToAbsoluteAssetPath(assetPath);
            if (!File.Exists(absolutePath))
                return true;

            try
            {
                var info = new FileInfo(absolutePath);
                if (info.Length < 32)
                    return true;

                using var stream = File.OpenRead(absolutePath);
                var header = new byte[12];
                if (stream.Read(header, 0, header.Length) < 8)
                    return true;

                // MP4: size(4) + 'ftyp'(4)
                return header[4] != (byte)'f'
                    || header[5] != (byte)'t'
                    || header[6] != (byte)'y'
                    || header[7] != (byte)'p';
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 原地修复损坏/空的占位 MP4，保留 GUID。
        /// </summary>
        public static void RepairPlaceholderVideo(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            string absolutePath = PathUtils.ToAbsoluteAssetPath(assetPath);
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory))
                PathUtils.EnsureAssetFolder(directory);

            File.WriteAllBytes(absolutePath, GetTemplateBytes());
            ImportPlaceholder(assetPath);
        }

        private static void ImportPlaceholder(string assetPath)
        {
            PathUtils.ImportAssetAfterDiskWrite(assetPath);
            TJGeneratorsGenerationLabel.EnableLabel(TJGeneratorsAssetReference.FromPath(assetPath));
        }

        private static byte[] GetTemplateBytes()
        {
            string templatePath = ResolveTemplateAssetPath();
            if (!string.IsNullOrEmpty(templatePath))
            {
                string absolute = PathUtils.ToAbsoluteAssetPath(templatePath);
                if (File.Exists(absolute))
                    return File.ReadAllBytes(absolute);
            }

            return Convert.FromBase64String(ShortestBlackMp4Base64);
        }

        private static string ResolveTemplateAssetPath()
        {
            string packageRoot = PathUtils.TryGetTjGeneratorsPackageRoot();
            if (!string.IsNullOrEmpty(packageRoot))
            {
                string packageRelative = Path.Combine(
                        packageRoot,
                        "Editor",
                        "Resources",
                        TemplateFileName)
                    .Replace('\\', '/');
                if (File.Exists(packageRelative))
                    return PathUtils.TryGetAssetsRelativePathFromAbsolute(packageRelative)
                        ?? ToPackagesRelativePath(packageRoot, packageRelative);
            }

            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(TemplateFileName));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(TemplateFileName, StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }

        private static string ToPackagesRelativePath(string packageRoot, string absolutePath)
        {
            absolutePath = Path.GetFullPath(absolutePath).Replace('\\', '/');
            packageRoot = Path.GetFullPath(packageRoot).Replace('\\', '/');
            if (!absolutePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            string tail = absolutePath.Substring(packageRoot.Length).TrimStart('/');
            return "Packages/cn.tuanjie.ai.generators/" + tail;
        }

        // 64x64 纯黑 H.264 单帧 MP4（约 1.5 KB），由 imageio 生成
        private const string ShortestBlackMp4Base64 =
            "AAAAIGZ0eXBpc29tAAACAGlzb21pc28yYXZjMW1wNDEAAAAIZnJlZQAAAuxtZGF0AAACrgYF//+q3EXpvebZSLeWLNgg2SPu73gyNjQgLSBjb3JlIDE2NCByMzE5MiBjMjRlMDZjIC0gSC4yNjQvTVBFRy00IEFWQyBjb2RlYyAtIENvcHlsZWZ0IDIwMDMtMjAyNCAtIGh0dHA6Ly93d3cudmlkZW9sYW4ub3JnL3gyNjQuaHRtbCAtIG9wdGlvbnM6IGNhYmFjPTEgcmVmPTMgZGVibG9jaz0xOjA6MCBhbmFseXNlPTB4MzoweDExMyBtZT1oZXggc3VibWU9NyBwc3k9MSBwc3lfcmQ9MS4wMDowLjAwIG1peGVkX3JlZj0xIG1lX3JhbmdlPTE2IGNocm9tYV9tZT0xIHRyZWxsaXM9MSA4eDhkY3Q9MSBjcW09MCBkZWFkem9uZT0yMSwxMSBmYXN0X3Bza2lwPTEgY2hyb21hX3FwX29mZnNldD0tMiB0aHJlYWRzPTIgbG9va2FoZWFkX3RocmVhZHM9MSBzbGljZWRfdGhyZWFkcz0wIG5yPTAgZGVjaW1hdGU9MSBpbnRlcmxhY2VkPTAgYmx1cmF5X2NvbXBhdD0wIGNvbnN0cmFpbmVkX2ludHJhPTAgYmZyYW1lcz0zIGJfcHlyYW1pZD0yIGJfYWRhcHQ9MSBiX2JpYXM9MCBkaXJlY3Q9MSB3ZWlnaHRiPTEgb3Blbl9nb3A9MCB3ZWlnaHRwPTIga2V5aW50PTI1MCBrZXlpbnRfbWluPTEwIHNjZW5lY3V0PTQwIGludHJhX3JlZnJlc2g9MCByY19sb29rYWhlYWQ9NDAgcmM9Y3JmIG1idHJlZT0xIGNyZj0yMy4wIHFjb21wPTAuNjAgcXBtaW49MCBxcG1heD02OSBxcHN0ZXA9NCBpcF9yYXRpbz0xLjQwIGFxPTE6MS4wMACAAAAAIGWIhAA7//73Tr8Cm1TCKgOSVwrqg7oK2KdPKm0Gjfu5AAAACkGaIWxDf/6nj4gAAAMgbW9vdgAAAGxtdmhkAAAAAAAAAAAAAAAAAAAD6AAAAMgAAQAAAQAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAkt0cmFrAAAAXHRraGQAAAADAAAAAAAAAAAAAAABAAAAAAAAAMgAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAEAAAABAAAAAAAAkZWR0cwAAABxlbHN0AAAAAAAAAAEAAADIAAAAAAABAAAAAAHDbWRpYQAAACBtZGhkAAAAAAAAAAAAAAAAAAAoAAAACABVxAAAAAAALWhkbHIAAAAAAAAAAHZpZGUAAAAAAAAAAAAAAABWaWRlb0hhbmRsZXIAAAABbm1pbmYAAAAUdm1oZAAAAAEAAAAAAAAAAAAAACRkaW5mAAAAHGRyZWYAAAAAAAAAAQAAAAx1cmwgAAAAAQAAAS5zdGJsAAAArnN0c2QAAAAAAAAAAQAAAJ5hdmMxAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAEAAQABIAAAASAAAAAAAAAABFUxhdmM2MS4xOS4xMDAgbGlieDI2NAAAAAAAAAAAAAAAGP//AAAANGF2Y0MBZAAK/+EAF2dkAAqs2UQmhAAAAwAEAAADAFA8SJZYAQAGaOvjyyLA/fj4AAAAABRidHJ0AAAAAAAAc6AAAHOgAAAAGHN0dHMAAAAAAAAAAQAAAAIAAAQAAAAAFHN0c3MAAAAAAAAAAQAAAAEAAAAcc3RzYwAAAAAAAAABAAAAAQAAAAIAAAABAAAAHHN0c3oAAAAAAAAAAAAAAAIAAALWAAAADgAAABRzdGNvAAAAAAAAAAEAAAAwAAAAYXVkdGEAAABZbWV0YQAAAAAAAAAhaGRscgAAAAAAAAAAbWRpcmFwcGwAAAAAAAAAAAAAAAAsaWxzdAAAACSpdG9vAAAAHGRhdGEAAAABAAAAAExhdmY2MS43LjEwMA==";
    }
}
#endif
