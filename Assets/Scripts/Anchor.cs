using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    private readonly HashSet<Rigidbody2D> processedBodies = new();

    private LineRenderer radiusLine;

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

    private void CreateRadiusLine()
    {
        GameObject lineObj = new GameObject("RadiusVisual");
        lineObj.transform.SetParent(transform, false);

        radiusLine = lineObj.AddComponent<LineRenderer>();
        radiusLine.useWorldSpace = true;
        radiusLine.loop = true;
        radiusLine.widthMultiplier = 0.05f;
        radiusLine.sortingOrder = 10;

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
            ? Color.red    // 正红色
            : Color.blue;  // 正蓝色
        radiusLine.endColor = radiusLine.startColor;
    }

    private void CreateBlackHole()
    {
        GameObject holeObj = new GameObject("BlackHole");
        holeObj.transform.SetParent(transform, false);

        // 黑色小圆
        SpriteRenderer sr = holeObj.AddComponent<SpriteRenderer>();
        sr.sprite = GetComponent<SpriteRenderer>() != null
            ? GetComponent<SpriteRenderer>().sprite
            : null;
        sr.color = Color.black;
        holeObj.transform.localScale = new Vector3(0.3f, 0.3f, 1f);

        // Trigger 检测
        CircleCollider2D col = holeObj.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 2f;

        // 挂载 BlackHole 脚本
        holeObj.AddComponent<BlackHole>();
    }

    private void FixedUpdate()
    {
        Collider2D[] targets = Physics2D.OverlapCircleAll(
            transform.position,
            effectRadius,
            targetLayer
        );

        processedBodies.Clear();

        foreach (Collider2D target in targets)
        {
            Rigidbody2D body = target.attachedRigidbody;

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
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position,
            effectRadius
        );
    }
}
