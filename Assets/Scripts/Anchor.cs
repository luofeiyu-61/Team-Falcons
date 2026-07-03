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

    public void SetMode(AnchorMode newMode)
    {
        mode = newMode;
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

            if (distance < 0.1f)
            {
                Destroy(body.gameObject);
                continue;
            }

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
