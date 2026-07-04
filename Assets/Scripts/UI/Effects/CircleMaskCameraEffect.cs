using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CircleMaskCameraEffect : MonoBehaviour
{
    [Header("圆形遮罩动画")]
    [SerializeField] private Shader maskShader;
    public float duration = 1.5f;
    [SerializeField] private float edgeSoftness = 0.01f;
    [SerializeField] private Color maskColor = Color.black;
    [SerializeField] private bool playOnStart = false;

    private Material materialInstance;
    private Coroutine activeRoutine;

    private static readonly int MaskProgressId = Shader.PropertyToID("_MaskProgress");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int MaskColorId = Shader.PropertyToID("_MaskColor");

    private void Awake()
    {
        if (maskShader == null)
            maskShader = Shader.Find("UI/CircleMaskShader");

        if (maskShader == null)
        {
            Debug.LogWarning($"[{nameof(CircleMaskCameraEffect)}] 未找到 CircleMaskShader，请在 Inspector 中手动指定。");
            return;
        }

        materialInstance = new Material(maskShader);
        materialInstance.SetFloat(EdgeSoftnessId, edgeSoftness);
        materialInstance.SetColor(MaskColorId, maskColor);
        materialInstance.SetFloat(MaskProgressId, 0f);
    }

    private void Start()
    {
        if (playOnStart)
            PlayForward();
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (materialInstance == null)
        {
            Graphics.Blit(src, dest);
            return;
        }

        Graphics.Blit(src, dest, materialInstance);
    }

    private void OnDestroy()
    {
        if (materialInstance != null)
            DestroyImmediate(materialInstance);
    }

    /// <summary>
    /// 从全屏可见收拢到中心（遮罩进度: 0 → 1）
    /// </summary>
    public void PlayForward()
    {
        Play(0f, 1f);
    }

    /// <summary>
    /// 从中心扩散到全屏（遮罩进度: 1 → 0）
    /// </summary>
    public void PlayReverse()
    {
        Play(1f, 0f);
    }

    /// <summary>
    /// 直接设置遮罩进度（0 = 全屏可见，1 = 完全收拢）
    /// </summary>
    public void SetProgress(float progress)
    {
        if (materialInstance == null)
            return;

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        materialInstance.SetFloat(MaskProgressId, progress);
    }

    /// <summary>
    /// 在 duration 时间内将遮罩进度从 fromProgress 渐变到 toProgress
    /// </summary>
    public void Play(float fromProgress, float toProgress)
    {
        if (materialInstance == null)
            return;

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(Animate(fromProgress, toProgress));
    }

    private IEnumerator Animate(float from, float to)
    {
        materialInstance.SetFloat(MaskProgressId, from);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            materialInstance.SetFloat(MaskProgressId, Mathf.Lerp(from, to, t));
            yield return null;
        }

        materialInstance.SetFloat(MaskProgressId, to);
        activeRoutine = null;
    }
}
