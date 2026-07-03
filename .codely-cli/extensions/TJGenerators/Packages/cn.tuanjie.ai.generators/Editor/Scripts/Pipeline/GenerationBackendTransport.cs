#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using TJGenerators;
using TJGenerators.Generators;
using TJGenerators.Utils;
using Unity.UniAsset.Manager.Editor.InternalBridge;

namespace TJGenerators.Pipeline
{
    /// <summary>
    /// 创建生成管线使用的后端传输实现（真实 HTTP）。
    /// </summary>
    internal static class GenerationBackendTransportFactory
    {
        /// <param name="fromMethod">请求来源标识（"ui" / "agent"），通过 fromMethod 头上报。</param>
        public static IGenerationBackendTransport Create(string fromMethod = GenerationRequestOrigin.Agent)
            => new ProductionBackendTransport(fromMethod);
    }

    /// <summary>
    /// 生成管线与团结后端之间的 HTTP 协程式传输抽象。
    /// </summary>
    internal interface IGenerationBackendTransport
    {
        IEnumerator CreateTask(string url, byte[] postData, Action<TJTaskResponse> onSuccess, Action<string> onError);
        IEnumerator CreateTaskMultipart(string url, MultipartRequestData multipartData, Action<TJTaskResponse> onSuccess, Action<string> onError);
        IEnumerator PollStatus(string taskId, string url, Action<TJTaskStatusResponse> onSuccess, Action<string> onError);
        IEnumerator DownloadBytes(string url, Action<byte[]> onSuccess, Action<string> onError);
    }

    internal sealed class ProductionBackendTransport : IGenerationBackendTransport
    {
        private readonly string _fromMethod;
        private readonly string _packageVersion;

        public ProductionBackendTransport(string fromMethod = GenerationRequestOrigin.Agent)
        {
            _fromMethod = string.IsNullOrEmpty(fromMethod) ? GenerationRequestOrigin.Agent : fromMethod;
            _packageVersion = GenerationRequestOrigin.GetPackageVersion();
        }

        public IEnumerator CreateTask(string url, byte[] postData, Action<TJTaskResponse> onSuccess, Action<string> onError)
        {
            using (UnityWebRequest uwr = new UnityWebRequest(url, "POST"))
            {
                uwr.uploadHandler = new UploadHandlerRaw(postData);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");

                string token = UnityConnectSession.instance.GetAccessToken();
                uwr.SetRequestHeader("Authorization", $"Bearer {token}");
                uwr.SetRequestHeader("source", "codely");
                uwr.SetRequestHeader(GenerationRequestOrigin.HeaderName, _fromMethod);
                if (!string.IsNullOrEmpty(_packageVersion))
                    uwr.SetRequestHeader(GenerationRequestOrigin.PackageVersionHeaderName, _packageVersion);

#if TJGENERATORS_DEBUG
                string requestBody = System.Text.Encoding.UTF8.GetString(postData);
                string logBody = System.Text.RegularExpressions.Regex.Replace(
                    requestBody,
                    @"""imageBase64""\s*:\s*(?:""[^""]*""|\[[^\]]*\])",
                    "\"imageBase64\":\"(omitted)\"");
                TJLog.Log($"[Transport] POST {url}\n[Transport] 请求体: {logBody}");
#endif

                yield return uwr.SendWebRequest();

                float timeout = 60f;
                float timeElapsed = 0f;
                float interval = 0.5f;

                while (uwr.result == UnityWebRequest.Result.InProgress && timeElapsed < timeout)
                {
                    double startWait = EditorApplication.timeSinceStartup;
                    while (EditorApplication.timeSinceStartup - startWait < interval)
                        yield return null;
                    timeElapsed += interval;
                }

                if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.ProtocolError)
                {
                    onError?.Invoke(ErrorDialogUtils.GetFriendlyErrorMessage(uwr));
                    yield break;
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(ErrorDialogUtils.GetFriendlyErrorMessage(uwr));
                    yield break;
                }

                try
                {
                    string jsonResponse = uwr.downloadHandler.text;
#if TJGENERATORS_DEBUG
                    TJLog.Log($"[Transport] POST {url} 响应: {jsonResponse}");
#else
                    TJLog.Log($"[GenerationPipeline] 响应: {jsonResponse}");
#endif
                    TJTaskResponse response = JsonUtility.FromJson<TJTaskResponse>(jsonResponse);
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    onError?.Invoke(string.Format(TJGeneratorsL10n.L("解析响应失败: {0}"), e.Message));
                }
            }
        }

        public IEnumerator CreateTaskMultipart(string url, MultipartRequestData multipartData, Action<TJTaskResponse> onSuccess, Action<string> onError)
        {
            string boundary = "----WebKitFormBoundary" + System.DateTime.Now.Ticks.ToString("x");
            byte[] boundaryBytes = System.Text.Encoding.ASCII.GetBytes("\r\n--" + boundary + "\r\n");
            byte[] endBoundaryBytes = System.Text.Encoding.ASCII.GetBytes("\r\n--" + boundary + "--\r\n");

            using (var memoryStream = new System.IO.MemoryStream())
            {
                if (!string.IsNullOrEmpty(multipartData.FilePath) && System.IO.File.Exists(multipartData.FilePath))
                {
                    string fileHeader =
                        $"Content-Disposition: form-data; name=\"{multipartData.FileFieldName}\"; filename=\"{multipartData.FileName}\"\r\nContent-Type: application/octet-stream\r\n\r\n";
                    byte[] fileHeaderBytes = System.Text.Encoding.UTF8.GetBytes(fileHeader);
                    memoryStream.Write(boundaryBytes, 0, boundaryBytes.Length);
                    memoryStream.Write(fileHeaderBytes, 0, fileHeaderBytes.Length);

                    byte[] fileBytes = System.IO.File.ReadAllBytes(multipartData.FilePath);
                    memoryStream.Write(fileBytes, 0, fileBytes.Length);
                }

                if (multipartData.AdditionalFields != null)
                {
                    foreach (var field in multipartData.AdditionalFields)
                    {
                        string fieldHeader = $"Content-Disposition: form-data; name=\"{field.Key}\"\r\n\r\n{field.Value}";
                        byte[] fieldBytes = System.Text.Encoding.UTF8.GetBytes(fieldHeader);
                        memoryStream.Write(boundaryBytes, 0, boundaryBytes.Length);
                        memoryStream.Write(fieldBytes, 0, fieldBytes.Length);
                    }
                }

                memoryStream.Write(endBoundaryBytes, 0, endBoundaryBytes.Length);

                byte[] postData = memoryStream.ToArray();

                using (UnityWebRequest uwr = new UnityWebRequest(url, "POST"))
                {
                    uwr.uploadHandler = new UploadHandlerRaw(postData);
                    uwr.downloadHandler = new DownloadHandlerBuffer();
                    uwr.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");

                    string token = UnityConnectSession.instance.GetAccessToken();
                    uwr.SetRequestHeader("Authorization", $"Bearer {token}");
                    uwr.SetRequestHeader("source", "codely");
                    uwr.SetRequestHeader(GenerationRequestOrigin.HeaderName, _fromMethod);
                    if (!string.IsNullOrEmpty(_packageVersion))
                        uwr.SetRequestHeader(GenerationRequestOrigin.PackageVersionHeaderName, _packageVersion);

                    TJLog.Log($"[Transport] POST Multipart {url}");

                    yield return uwr.SendWebRequest();

                    float timeout = 60f;
                    float timeElapsed = 0f;
                    float interval = 0.5f;

                    while (uwr.result == UnityWebRequest.Result.InProgress && timeElapsed < timeout)
                    {
                        double startWait = EditorApplication.timeSinceStartup;
                        while (EditorApplication.timeSinceStartup - startWait < interval)
                            yield return null;
                        timeElapsed += interval;
                    }

                    if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.ProtocolError)
                    {
                        onError?.Invoke(ErrorDialogUtils.GetFriendlyErrorMessage(uwr));
                        yield break;
                    }

                    if (uwr.result != UnityWebRequest.Result.Success)
                    {
                        onError?.Invoke(ErrorDialogUtils.GetFriendlyErrorMessage(uwr));
                        yield break;
                    }

                    try
                    {
                        string jsonResponse = uwr.downloadHandler.text;
                        TJLog.Log($"[GenerationPipeline] Multipart响应: {jsonResponse}");
                        TJTaskResponse response = JsonUtility.FromJson<TJTaskResponse>(jsonResponse);
                        onSuccess?.Invoke(response);
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke(string.Format(TJGeneratorsL10n.L("解析响应失败: {0}"), e.Message));
                    }
                }
            }
        }

        public IEnumerator PollStatus(string taskId, string url, Action<TJTaskStatusResponse> onSuccess, Action<string> onError)
        {
            string token = null;
            try
            {
                token = UnityConnectSession.instance.GetAccessToken();
            }
            catch (Exception ex)
            {
                onError?.Invoke(string.Format(TJGeneratorsL10n.L("获取认证token失败: {0}"), ex.Message));
                yield break;
            }
            if (string.IsNullOrEmpty(token))
            {
                onError?.Invoke(TJGeneratorsL10n.L("认证token为空，请确保已登录Unity"));
                yield break;
            }

            UnityWebRequest uwr = UnityWebRequest.Get(url);
            uwr.downloadHandler = new DownloadHandlerBuffer();
            uwr.SetRequestHeader("Authorization", $"Bearer {token}");
            uwr.SetRequestHeader("source", "codely");
            uwr.SetRequestHeader(GenerationRequestOrigin.HeaderName, _fromMethod);
            if (!string.IsNullOrEmpty(_packageVersion))
                uwr.SetRequestHeader(GenerationRequestOrigin.PackageVersionHeaderName, _packageVersion);

#if TJGENERATORS_DEBUG
            TJLog.Log($"[Transport] GET {url}");
#endif

            yield return uwr.SendWebRequest();

            float requestTimeout = 30f;
            float requestElapsed = 0f;
            while (!uwr.isDone && requestElapsed < requestTimeout)
            {
                requestElapsed += Time.deltaTime;
                yield return null;
            }

            if (!uwr.isDone)
            {
                uwr.Abort();
                uwr.Dispose();
                onError?.Invoke(TJGeneratorsL10n.L("请求超时，任务可能仍在后端运行。重新打开窗口可继续等待。"));
                yield break;
            }

            if (uwr.result == UnityWebRequest.Result.ConnectionError || uwr.result == UnityWebRequest.Result.ProtocolError)
            {
                string msg = ErrorDialogUtils.GetFriendlyErrorMessage(uwr);
                uwr.Dispose();
                onError?.Invoke(msg);
                yield break;
            }

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                string msg = ErrorDialogUtils.GetFriendlyErrorMessage(uwr);
                uwr.Dispose();
                onError?.Invoke(msg);
                yield break;
            }

            string jsonResponse = uwr.downloadHandler.text;
            uwr.Dispose();
            try
            {
#if TJGENERATORS_DEBUG
                TJLog.Log($"[Transport] GET {url} 响应: {jsonResponse}");
#else
                TJLog.Log($"[GenerationPipeline] 状态响应: {jsonResponse}");
#endif
                var response = JsonUtility.FromJson<TJTaskStatusResponse>(jsonResponse);
                TaskStatusOutputUrlHelper.PatchImageUrlsFromTaskJson(jsonResponse, response);
                onSuccess?.Invoke(response);
            }
            catch (Exception e)
            {
                onError?.Invoke(string.Format(TJGeneratorsL10n.L("解析响应失败: {0}"), e.Message));
            }
        }

        public IEnumerator DownloadBytes(string url, Action<byte[]> onSuccess, Action<string> onError)
        {
            using (UnityWebRequest uwr = UnityWebRequest.Get(url))
            {
                uwr.downloadHandler = new DownloadHandlerBuffer();
                yield return uwr.SendWebRequest();

                float timeout = 120f;
                float timeElapsed = 0f;
                float interval = 0.5f;

                while (uwr.result == UnityWebRequest.Result.InProgress && timeElapsed < timeout)
                {
                    double startWait = EditorApplication.timeSinceStartup;
                    while (EditorApplication.timeSinceStartup - startWait < interval)
                        yield return null;
                    timeElapsed += interval;
                }

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(ErrorDialogUtils.GetFriendlyErrorMessage(uwr, TJGeneratorsL10n.L("下载失败")));
                    yield break;
                }

                onSuccess?.Invoke(uwr.downloadHandler.data);
            }
        }
    }
}
#endif
