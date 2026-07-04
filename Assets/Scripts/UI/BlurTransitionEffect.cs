using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class BlurTransitionEffect : MonoBehaviour
{
    [Header("模糊动画")]
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private float maxBlurRadius = 8f;
    [SerializeField] private bool playOnStart = false;

    private Material materialInstance;
    private Coroutine activeRoutine;

    private static readonly int BlurAmountId = Shader.PropertyToID("_BlurAmount");
    private static readonly int MaxBlurRadiusId = Shader.PropertyToID("_MaxBlurRadius");

    private void Awake()
    {
        // 优先从 UI Image 获取材质，其次从 Renderer 获取
        Image image = GetComponent<Image>();
        if (image != null)
        {
            // 实例化材质，避免修改共享资源
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
            Debug.LogWarning($"[{nameof(BlurTransitionEffect)}] 未找到材质，请确保物体上有 Image 或 Renderer 组件。");
            return;
        }

        if (!materialInstance.HasProperty(BlurAmountId))
        {
            Debug.LogWarning($"[{nameof(BlurTransitionEffect)}] 材质未使用 BlurShader，缺少 _BlurAmount 属性。");
            return;
        }

        materialInstance.SetFloat(MaxBlurRadiusId, maxBlurRadius);
        materialInstance.SetFloat(BlurAmountId, 0f);
    }

    private void Start()
    {
        if (playOnStart)
            PlayForward();
    }

    /// <summary>
    /// 从清晰渐变到完全模糊（blurAmount: 0 → 1）
    /// </summary>
    public void PlayForward()
    {
        Play(0f, 1f);
    }

    /// <summary>
    /// 从完全模糊渐变回清晰（blurAmount: 1 → 0）
    /// </summary>
    public void PlayReverse()
    {
        Play(1f, 0f);
    }

    /// <summary>
    /// 直接设置模糊量（0 = 清晰，1 = 完全模糊）
    /// </summary>
    public void SetProgress(float progress)
    {
        if (materialInstance == null || !materialInstance.HasProperty(BlurAmountId))
            return;

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        materialInstance.SetFloat(BlurAmountId, progress);
    }

    /// <summary>
    /// 在 duration 时间内将模糊量从 fromBlur 渐变到 toBlur
    /// </summary>
    public void Play(float fromBlur, float toBlur)
    {
        if (materialInstance == null || !materialInstance.HasProperty(BlurAmountId))
            return;

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(Animate(fromBlur, toBlur));
    }

    private IEnumerator Animate(float from, float to)
    {
        materialInstance.SetFloat(BlurAmountId, from);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            materialInstance.SetFloat(BlurAmountId, Mathf.Lerp(from, to, t));
            yield return null;
        }

        materialInstance.SetFloat(BlurAmountId, to);
        activeRoutine = null;
    }
}
