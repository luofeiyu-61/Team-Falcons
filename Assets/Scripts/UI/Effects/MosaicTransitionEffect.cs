using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MosaicTransitionEffect : MonoBehaviour
{
    [Header("马赛克动画")]
    [SerializeField] private float duration = 1.5f;
    [SerializeField] private float maxBlockSize = 64f;
    [SerializeField] private bool playOnStart = false;

    private Material materialInstance;
    private Coroutine activeRoutine;

    private static readonly int ProgressId = Shader.PropertyToID("_MosaicProgress");
    private static readonly int BlockSizeId = Shader.PropertyToID("_MaxBlockSize");

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
            Debug.LogWarning($"[{nameof(MosaicTransitionEffect)}] 未找到材质，请确保物体上有 Image 或 Renderer 组件。");
            return;
        }

        if (!materialInstance.HasProperty(ProgressId))
        {
            Debug.LogWarning($"[{nameof(MosaicTransitionEffect)}] 材质未使用 MosaicShader，缺少 _MosaicProgress 属性。");
            return;
        }

        materialInstance.SetFloat(BlockSizeId, maxBlockSize);
        materialInstance.SetFloat(ProgressId, 0f);
    }

    private void Start()
    {
        if (playOnStart)
            PlayForward();
    }

    /// <summary>
    /// 从原图渐变到完全马赛克（progress: 0 → 1）
    /// </summary>
    public void PlayForward()
    {
        Play(0f, 1f);
    }

    /// <summary>
    /// 从完全马赛克渐变回原图（progress: 1 → 0）
    /// </summary>
    public void PlayReverse()
    {
        Play(1f, 0f);
    }

    /// <summary>
    /// 直接设置马赛克进度（0 = 原图，1 = 完全马赛克）
    /// </summary>
    public void SetProgress(float progress)
    {
        if (materialInstance == null || !materialInstance.HasProperty(ProgressId))
            return;

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        materialInstance.SetFloat(ProgressId, progress);
    }

    /// <summary>
    /// 在 duration 时间内将马赛克进度从 fromProgress 渐变到 toProgress
    /// </summary>
    public void Play(float fromProgress, float toProgress)
    {
        if (materialInstance == null || !materialInstance.HasProperty(ProgressId))
            return;

        if (activeRoutine != null)
            StopCoroutine(activeRoutine);

        activeRoutine = StartCoroutine(Animate(fromProgress, toProgress));
    }

    private IEnumerator Animate(float from, float to)
    {
        materialInstance.SetFloat(ProgressId, from);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            materialInstance.SetFloat(ProgressId, Mathf.Lerp(from, to, t));
            yield return null;
        }

        materialInstance.SetFloat(ProgressId, to);
        activeRoutine = null;
    }
}
