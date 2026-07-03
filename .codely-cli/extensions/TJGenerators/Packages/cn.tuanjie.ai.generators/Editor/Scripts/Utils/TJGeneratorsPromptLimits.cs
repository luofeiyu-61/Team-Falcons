#if UNITY_EDITOR
using TJGenerators.Config;

namespace TJGenerators.Utils
{
    /// <summary>
    /// 集中管理各生成器的 prompt 最大字符数，与后端 binding max 对齐。
    /// 返回 0 表示不限制。
    /// </summary>
    public static class TJGeneratorsPromptLimits
    {
        /// <summary>
        /// 根据 generatorId 返回 prompt 最大字符数。
        /// </summary>
        public static int GetMaxLength(string generatorId) => generatorId switch
        {
            // fal.go backend binding max
            "frontier-game-design" => 4000,
            "frontier-effect"      => 2000,
            "sound-effect"         => 500,
            "minimax-tts"          => 10000,
            // client-only limits (backend has no binding max but documents a recommended cap)
            "tencent-generation"   => 1000,
            "tripo-p1"             => 1024,
            "meshy-animation"      => 600,
            _                      => 0
        };
    }
}
#endif
