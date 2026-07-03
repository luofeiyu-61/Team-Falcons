using UnityEngine;

public class VortexRotator : MonoBehaviour
{
    [Header("效果设置")]
    [SerializeField] private float cycleDuration = 2f;
    [SerializeField] private float pauseDuration = 1f;
    [SerializeField] private float radius = 2f;

    private SpriteRenderer spriteRenderer;
    private Material material;
    private float elapsed;

    private static readonly int PhaseID = Shader.PropertyToID("_Phase");

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // 创建白色方形 Sprite 作为渲染网格（pixelsPerUnit=1 使 1texel=1世界单位）
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        spriteRenderer.sprite = Sprite.Create(
            tex,
            new Rect(0, 0, 4, 4),
            new Vector2(0.5f, 0.5f),
            1f
        );

        // 使用 SpriteRenderer 上已分配的材质实例
        material = spriteRenderer.material;
    }

    private void Update()
    {
        if (material == null) return;

        elapsed += Time.deltaTime;

        float totalCycle = cycleDuration * 2f + pauseDuration;
        float timeInCycle = elapsed % totalCycle;

        float phase;
        if (timeInCycle < cycleDuration)
        {
            // 第一次收缩
            phase = timeInCycle / cycleDuration;
        }
        else if (timeInCycle < cycleDuration * 2f)
        {
            // 第二次收缩
            phase = (timeInCycle - cycleDuration) / cycleDuration;
        }
        else
        {
            // 停顿期，光环不可见
            phase = -1f;
        }

        material.SetFloat(PhaseID, phase);
    }

    private void OnValidate()
    {
        transform.localScale = new Vector3(radius, radius, 1f);
    }
}
