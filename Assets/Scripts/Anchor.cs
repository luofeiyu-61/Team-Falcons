using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum AnchorMode
{
    Attract, // 引力
    Repel    // 斥力
}

public class Anchor : MonoBehaviour
{
    [Header("锚点效果")]
    [SerializeField] private AnchorMode mode = AnchorMode.Attract;
    [Min(0f)]
    [SerializeField] private float effectRadius = 4f;
    [FormerlySerializedAs("forceStrength")]
    [Min(0f)]
    [SerializeField] private float gravitationalConstant = 55f;
    [Min(0f)]
    [SerializeField] private float anchorMass = 1f;
    [Min(0.01f)]
    [SerializeField] private float minDistance = 0.2f;
    [SerializeField] private LayerMask targetLayer;

    [Header("黑洞中心")]
    [SerializeField, Min(0f)] private float blackHoleWorldScale = 0.18f;
    [SerializeField, Min(0f)] private float blackHoleTriggerRadius = 0.5f;

    [SerializeField] private float maxTargetSpeed = 6f;

    private readonly HashSet<Rigidbody2D> processedBodies = new();

    private const int BlackHoleSpriteSize = 64;
    private static Sprite blackHoleSprite;

    private LineRenderer radiusLine;
    private Transform blackHoleTransform;

    public void SetMode(AnchorMode newMode)
    {
        mode = newMode;
        UpdateRadiusColor();
    }

    public AnchorMode GetMode()
    {
        return mode;
    }

    private void Start()
    {
        CreateRadiusLine();
        CreateBlackHole();
    }

    private void LateUpdate()
    {
        ApplyBlackHoleWorldScale();
    }

    private void CreateRadiusLine()
    {
        GameObject lineObj = new GameObject("RadiusVisual");
        lineObj.transform.SetParent(transform, false);

        radiusLine = lineObj.AddComponent<LineRenderer>();
        radiusLine.useWorldSpace = true;
        radiusLine.loop = true;
        radiusLine.widthMultiplier = 0.05f;
        radiusLine.sortingOrder = short.MaxValue;
        radiusLine.numCapVertices = 4;
        radiusLine.numCornerVertices = 4;

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material radiusMaterial = new Material(shader);
        radiusMaterial.renderQueue = 5000;
        radiusMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        radiusMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        radiusMaterial.SetInt("_Cull", (int)CullMode.Off);
        radiusMaterial.SetInt("_ZWrite", 0);
        radiusMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
        radiusLine.material = radiusMaterial;

        // 64 个点拼成圆（世界坐标，不受父物体缩放影响）
        int segments = 64;
        radiusLine.positionCount = segments;
        Vector3[] points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            points[i] = transform.position + new Vector3(
                Mathf.Cos(angle) * effectRadius,
                Mathf.Sin(angle) * effectRadius,
                0f
            );
        }
        radiusLine.SetPositions(points);

        UpdateRadiusColor();
    }

    private void UpdateRadiusColor()
    {
        if (radiusLine == null)
            return;

        radiusLine.startColor = mode == AnchorMode.Attract
            ? Color.blue    // 正红色
            : Color.red;  // 正蓝色
        radiusLine.endColor = radiusLine.startColor;
    }

    private void CreateBlackHole()
    {
        // 只有 Attract 模式才创建致命中心
        if (mode != AnchorMode.Attract)
            return;

        GameObject holeObj = new GameObject("BlackHole");
        blackHoleTransform = holeObj.transform;
        blackHoleTransform.SetParent(transform, false);
        blackHoleTransform.localPosition = Vector3.zero;
        blackHoleTransform.localRotation = Quaternion.identity;
        ApplyBlackHoleWorldScale();

        // 黑色圆点
        SpriteRenderer sr = holeObj.AddComponent<SpriteRenderer>();
        sr.sprite = GetBlackHoleSprite();
        sr.color = Color.black;
        sr.sortingOrder = short.MaxValue;

        // Trigger 检测
        CircleCollider2D col = holeObj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = blackHoleTriggerRadius;

        // 挂载 BlackHole 脚本
        holeObj.AddComponent<BlackHole>();
    }

    private void ApplyBlackHoleWorldScale()
    {
        if (blackHoleTransform == null)
            return;

        Vector3 parentScale = transform.lossyScale;
        blackHoleTransform.localScale = new Vector3(
            GetCompensatedLocalScale(blackHoleWorldScale, parentScale.x),
            GetCompensatedLocalScale(blackHoleWorldScale, parentScale.y),
            1f
        );
    }

    private static float GetCompensatedLocalScale(float targetWorldScale, float parentScale)
    {
        if (Mathf.Approximately(parentScale, 0f))
            return targetWorldScale;

        return targetWorldScale / Mathf.Abs(parentScale);
    }

    private static Sprite GetBlackHoleSprite()
    {
        if (blackHoleSprite != null)
            return blackHoleSprite;

        Texture2D texture = new Texture2D(
            BlackHoleSpriteSize,
            BlackHoleSpriteSize,
            TextureFormat.RGBA32,
            false
        );
        texture.name = "BlackHoleCircle";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[BlackHoleSpriteSize * BlackHoleSpriteSize];
        Vector2 center = new Vector2(
            BlackHoleSpriteSize * 0.5f,
            BlackHoleSpriteSize * 0.5f
        );
        float radius = BlackHoleSpriteSize * 0.5f - 1f;

        for (int y = 0; y < BlackHoleSpriteSize; y++)
        {
            for (int x = 0; x < BlackHoleSpriteSize; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x + 0.5f, y + 0.5f),
                    center
                );
                float alpha = Mathf.Clamp01(radius - distance + 1f);
                pixels[y * BlackHoleSpriteSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        blackHoleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, BlackHoleSpriteSize, BlackHoleSpriteSize),
            new Vector2(0.5f, 0.5f),
            BlackHoleSpriteSize
        );
        blackHoleSprite.name = "BlackHoleCircle";

        return blackHoleSprite;
    }

    private void FixedUpdate()
    {
        Collider2D[] targets = Physics2D.OverlapCircleAll(
            transform.position,
            effectRadius
        );

        processedBodies.Clear();

        foreach (Collider2D target in targets)
        {
            // 只处理 targetLayer 层级或 Tag 为 Gravitable 的物体
            bool isTargetLayer = ((1 << target.gameObject.layer) & targetLayer) != 0;
            bool isGravitable = target.CompareTag("Gravitable");

            if (!isTargetLayer && !isGravitable)
                continue;

            Rigidbody2D body = target.attachedRigidbody;
            if (body != null && IsDeadPlayer(body))
                continue;

            if (body == null)
                continue;

            // 避免同一个角色多个 Collider 被重复施力
            if (!processedBodies.Add(body))
                continue;

            Vector2 toAnchor =
                (Vector2)transform.position - body.worldCenterOfMass;

            float distance = toAnchor.magnitude;

            // 距离过近时跳过施力，由 BlackHole Trigger 处理
            if (distance < 0.1f)
                continue;

            Vector2 direction = toAnchor.normalized;

            if (mode == AnchorMode.Repel)
                direction = -direction;

            float clampedDistance = Mathf.Max(distance, minDistance);
            float forceMagnitude =
                gravitationalConstant * anchorMass * body.mass /
                (clampedDistance * clampedDistance);

            body.AddForce(
                direction * forceMagnitude,
                ForceMode2D.Force
            );

            // 限制最大速度，防止穿墙
            if (body.velocity.magnitude > maxTargetSpeed)
                body.velocity = body.velocity.normalized * maxTargetSpeed;
        }
    }

    private static bool IsDeadPlayer(Rigidbody2D body)
    {
        PlayerRespawn playerRespawn = body.GetComponent<PlayerRespawn>();
        return playerRespawn != null && playerRespawn.IsDead;
    }

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        Color radiusColor = mode == AnchorMode.Attract ? Color.red : Color.blue;
        Color previousColor = Handles.color;
        CompareFunction previousZTest = Handles.zTest;

        Handles.color = radiusColor;
        Handles.zTest = CompareFunction.Always;
        Handles.DrawWireDisc(transform.position, Vector3.forward, effectRadius);

        Handles.color = previousColor;
        Handles.zTest = previousZTest;
#endif
    }
}
