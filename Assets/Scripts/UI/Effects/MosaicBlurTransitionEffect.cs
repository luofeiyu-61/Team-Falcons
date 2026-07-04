using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class MosaicBlurTransitionEffect : MonoBehaviour
{
    [Header("模糊动画")]
    [SerializeField] private float blurDuration = 1.5f;
    [SerializeField] private float maxBlurRadius = 8f;

    [Header("马赛克动画")]
    [SerializeField] private float mosaicDuration = 1.5f;
    [SerializeField] private float maxBlockSize = 64f;

    [Header("序列播放（先模糊再马赛克）")]
    [SerializeField] private bool playSequenceOnStart = false;
    [SerializeField] private float mosaicStartDelay = 0f;

    private Material materialInstance;
    private Coroutine blurRoutine;
    private Coroutine mosaicRoutine;
    private Coroutine sequenceRoutine;

    private static readonly int BlurAmountId = Shader.PropertyToID("_BlurAmount");
    private static readonly int MosaicProgressId = Shader.PropertyToID("_MosaicProgress");
    private static readonly int MaxBlurRadiusId = Shader.PropertyToID("_MaxBlurRadius");
    private static readonly int MaxBlockSizeId = Shader.PropertyToID("_MaxBlockSize");
    
    private void Awake()
    {
        Image image = GetComponent<Image>();
        if (image != null)
        {
            materialInstance = Instantiate(image.material);
            image.material = materialInstance;
        }

        if (materialInstance == null)
        {
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null)
                materialInstance = renderer.material;
        }

        if (materialInstance == null)
        {
            Debug.LogWarning($"[{nameof(MosaicBlurTransitionEffect)}] 未找到材质，请确保物体上有 Image 或 Renderer 组件。");
            return;
        }

        if (!materialInstance.HasProperty(BlurAmountId) || !materialInstance.HasProperty(MosaicProgressId))
        {
            Debug.LogWarning($"[{nameof(MosaicBlurTransitionEffect)}] 材质未使用 MosaicBlurShader，缺少 _BlurAmount 或 _MosaicProgress 属性。");
            return;
        }

        materialInstance.SetFloat(MaxBlurRadiusId, maxBlurRadius);
        materialInstance.SetFloat(MaxBlockSizeId, maxBlockSize);
        materialInstance.SetFloat(BlurAmountId, 0f);
        materialInstance.SetFloat(MosaicProgressId, 0f);
    }

    private void Start()
    {
        if (playSequenceOnStart)
            PlaySequence();
    }

    // ═══════════════════════════════════════════
    // 模糊控制
    // ═══════════════════════════════════════════

    /// <summary>清晰 → 完全模糊</summary>
    public void PlayBlurForward(UnityAction<(bool blur, bool reverse)> callback = null)
    {
        PlayBlur(0f, 1f, callback);
    }

    /// <summary>完全模糊 → 清晰</summary>
    public void PlayBlurReverse(UnityAction<(bool blur, bool reverse)> callback = null)
    {
        PlayBlur(1f, 0f, callback);
    }

    /// <summary>直接设置模糊量（0 = 清晰，1 = 完全模糊）</summary>
    public void SetBlurProgress(float progress)
    {
        if (materialInstance == null || !materialInstance.HasProperty(BlurAmountId))
            return;

        if (blurRoutine != null)
        {
            StopCoroutine(blurRoutine);
            blurRoutine = null;
        }

        materialInstance.SetFloat(BlurAmountId, progress);
    }

    /// <summary>在 blurDuration 内将模糊量从 fromBlur 渐变到 toBlur</summary>
    public void PlayBlur(float fromBlur, float toBlur, UnityAction<(bool blur, bool reverse)> callback = null)
    {
        if (materialInstance == null || !materialInstance.HasProperty(BlurAmountId))
            return;

        if (blurRoutine != null)
            StopCoroutine(blurRoutine);

        blurRoutine = StartCoroutine(Animate(BlurAmountId, fromBlur, toBlur, blurDuration, callback));
    }

    // ═══════════════════════════════════════════
    // 马赛克控制
    // ═══════════════════════════════════════════

    /// <summary>原图 → 完全马赛克</summary>
    public void PlayMosaicForward(UnityAction<(bool blur, bool reverse)> callback = null)
    {
        PlayMosaic(0f, 1f, callback);
    }

    /// <summary>完全马赛克 → 原图</summary>
    public void PlayMosaicReverse(UnityAction<(bool blur, bool reverse)> callback = null)
    {
        PlayMosaic(1f, 0f, callback);
    }

    /// <summary>直接设置马赛克进度（0 = 原图，1 = 完全马赛克）</summary>
    public void SetMosaicProgress(float progress)
    {
        if (materialInstance == null || !materialInstance.HasProperty(MosaicProgressId))
            return;

        if (mosaicRoutine != null)
        {
            StopCoroutine(mosaicRoutine);
            mosaicRoutine = null;
        }

        materialInstance.SetFloat(MosaicProgressId, progress);
    }

    /// <summary>在 mosaicDuration 内将马赛克进度从 fromProgress 渐变到 toProgress</summary>
    public void PlayMosaic(float fromProgress, float toProgress, UnityAction<(bool blur, bool reverse)> callback = null)
    {
        if (materialInstance == null || !materialInstance.HasProperty(MosaicProgressId))
            return;

        if (mosaicRoutine != null)
            StopCoroutine(mosaicRoutine);

        mosaicRoutine = StartCoroutine(Animate(MosaicProgressId, fromProgress, toProgress, mosaicDuration, callback));
    }

    // ═══════════════════════════════════════════
    // 序列播放：先模糊再马赛克
    // ═══════════════════════════════════════════

    /// <summary>
    /// 先播放模糊动画（0→1），完成后等待 mosaicStartDelay，再播放马赛克动画（0→1）。
    /// 可通过协程返回值等待完成。
    /// </summary>
    public Coroutine PlaySequence()
    {
        return PlaySequence(0f, 1f, mosaicStartDelay, 0f, 1f);
    }

    /// <summary>
    /// 自定义序列：先模糊 fromBlur→toBlur，等待 delay，再马赛克 fromMosaic→toMosaic。
    /// </summary>
    public Coroutine PlaySequence(
        float fromBlur, float toBlur,
        float mosaicDelay,
        float fromMosaic, float toMosaic)
    {
        if (sequenceRoutine != null)
            StopCoroutine(sequenceRoutine);

        sequenceRoutine = StartCoroutine(SequenceRoutine(fromBlur, toBlur, mosaicDelay, fromMosaic, toMosaic));
        return sequenceRoutine;
    }

    private IEnumerator SequenceRoutine(
        float fromBlur, float toBlur,
        float mosaicDelay,
        float fromMosaic, float toMosaic)
    {
        // 阶段1：模糊
        PlayBlur(fromBlur, toBlur);
        if (blurRoutine != null)
            yield return blurRoutine;

        // 阶段2：延迟
        if (mosaicDelay > 0f)
            yield return new WaitForSeconds(mosaicDelay);

        // 阶段3：马赛克
        PlayMosaic(fromMosaic, toMosaic);
        if (mosaicRoutine != null)
            yield return mosaicRoutine;

        sequenceRoutine = null;
    }

    // ═══════════════════════════════════════════
    // 重置
    // ═══════════════════════════════════════════

    /// <summary>停止所有动画并重置为原图</summary>
    public void Reset()
    {
        StopAll();
        SetBlurProgress(0f);
        SetMosaicProgress(0f);
    }

    /// <summary>停止所有动画</summary>
    public void StopAll()
    {
        if (blurRoutine != null)
        {
            StopCoroutine(blurRoutine);
            blurRoutine = null;
        }
        if (mosaicRoutine != null)
        {
            StopCoroutine(mosaicRoutine);
            mosaicRoutine = null;
        }
        if (sequenceRoutine != null)
        {
            StopCoroutine(sequenceRoutine);
            sequenceRoutine = null;
        }
    }

    private IEnumerator Animate(int propertyId, float from, float to, float duration, UnityAction<(bool blur, bool reverse)> callback)
    {
        materialInstance.SetFloat(propertyId, from);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            materialInstance.SetFloat(propertyId, Mathf.Lerp(from, to, t));
            yield return null;
        }

        materialInstance.SetFloat(propertyId, to);
        callback?.Invoke((propertyId == BlurAmountId, from > to));
    }
}
