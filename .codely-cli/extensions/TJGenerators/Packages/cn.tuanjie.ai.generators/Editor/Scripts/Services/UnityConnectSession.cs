using UnityEditor;
using System.IO;
using System.Reflection;
using Codely.Newtonsoft.Json.Linq;
using TJGenerators.AssetSearch;

namespace Unity.UniAsset.Manager.Editor.InternalBridge
{
    public class UnityConnectSession
    {
        static UnityConnectSession _instance = new UnityConnectSession();
        private const string CodelyCliDir = ".codely-cli";
        private const string OAuthCredsFile = "oauth_creds.json";
        
        public static UnityConnectSession instance
        {
            get => _instance;
        }
        
        public string GetAccessToken()
        {
            // 优先使用已换票成功的 Codely token 缓存（覆盖 Unity token 过期但非空的场景）
            string cachedToken = CodelyTokenProvider.CachedAccessToken;
            if (!string.IsNullOrEmpty(cachedToken))
                return cachedToken;

            // 使用反射获取UnityConnect的访问令牌
            string rawToken = GetRawAccessToken();

            // Fallback: Unity Connect token 不可用时，读取 ~/.codely-cli/oauth_creds.json 中的 JWT token
            if (!string.IsNullOrEmpty(rawToken))
                return rawToken;

            string fallbackToken = ReadFallbackToken();
            if (!string.IsNullOrEmpty(fallbackToken))
            {
#if TJGENERATORS_DEBUG
                UnityEngine.Debug.Log($"[UnityConnectSession] Using fallback JWT token from ~/.codely-cli/oauth_creds.json, token: {fallbackToken}");
#endif
                return fallbackToken;
            }

            return "";
        }

        /// <summary>
        /// 获取原始的 Unity Connect access token，不含 fallback 逻辑。
        /// 仅供 CodelyTokenProvider 换票判断使用，其他场景请用 GetAccessToken()。
        /// </summary>
        public string GetRawAccessToken()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var getAccessTokenMethod = unityConnectType.GetMethod("GetAccessToken", BindingFlags.Public | BindingFlags.Instance);
                    if (getAccessTokenMethod != null)
                    {
                        string token = (string)getAccessTokenMethod.Invoke(unityConnectInstance, null);
                        if (!string.IsNullOrEmpty(token))
                            return token;
                    }
                }
            }
            return "";
        }

        public string GetUserId()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var getUserIdMethod = unityConnectType.GetMethod("GetUserId", BindingFlags.Public | BindingFlags.Instance);
                    if (getUserIdMethod != null)
                    {
                        return (string)getUserIdMethod.Invoke(unityConnectInstance, null);
                    }
                }
            }
            return "";
        }

        public string GetEnvironment()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var getEnvironmentMethod = unityConnectType.GetMethod("GetEnvironment", BindingFlags.Public | BindingFlags.Instance);
                    if (getEnvironmentMethod != null)
                    {
                        return (string)getEnvironmentMethod.Invoke(unityConnectInstance, null);
                    }
                }
            }
            return "";
        }

        public void ShowLogin()
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var showLoginMethod = unityConnectType.GetMethod("ShowLogin", BindingFlags.Public | BindingFlags.Instance);
                    showLoginMethod?.Invoke(unityConnectInstance, null);
                }
            }
        }
        
        public static void OpenAuthorizedURLInWebBrowser(string url)
        {
            var unityConnectType = System.Type.GetType("UnityEditor.Connect.UnityConnect,UnityEditor");
            if (unityConnectType != null)
            {
                var instanceProperty = unityConnectType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    var unityConnectInstance = instanceProperty.GetValue(null);
                    var openURLMethod = unityConnectType.GetMethod("OpenAuthorizedURLInWebBrowser", BindingFlags.Public | BindingFlags.Instance);
                    openURLMethod?.Invoke(unityConnectInstance, new object[] { url });
                }
            }
        }

        /// <summary>
        /// 从用户主目录下 ~/.codely-cli/oauth_creds.json 读取 fallback JWT token。
        /// 兼容 Windows 与 macOS：优先 UserProfile，fallback 读 HOME 环境变量。
        /// </summary>
        internal static string ReadFallbackToken()
        {
            try
            {
                string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(homeDir))
                    homeDir = System.Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(homeDir))
                    return null;

                string credsPath = Path.Combine(homeDir, CodelyCliDir, OAuthCredsFile);
                if (!File.Exists(credsPath))
                    return null;

                string json = File.ReadAllText(credsPath);
                JObject data = JObject.Parse(json);
                string token = data["access_token"]?.ToString();
                return string.IsNullOrEmpty(token) ? null : token;
            }
            catch
            {
                return null;
            }
        }
    }
} 