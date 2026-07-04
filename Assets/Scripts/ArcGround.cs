using UnityEngine;

[RequireComponent(typeof(EdgeCollider2D))]
public class ArcGround : MonoBehaviour
{
    [Header("弧形设置")]
    [SerializeField] private float radius = 3f;
    [SerializeField, Range(0f, 360f)] private float arcAngle = 90f;
    [SerializeField, Range(0f, 360f)] private float startAngle = 0f;
    [SerializeField, Range(8, 128)] private int segments = 32;
    [SerializeField] private float thickness = 0.3f;

    private EdgeCollider2D edgeCollider;
    private LineRenderer lineRenderer;

    private void Awake()
    {
        edgeCollider = GetComponent<EdgeCollider2D>();
        SetupLineRenderer();
    }

    private void SetupLineRenderer()
    {
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();

        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = false;
        lineRenderer.startWidth = thickness;
        lineRenderer.endWidth = thickness;
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.sortingOrder = 5;

        // 用 legacy shader 保证 2D 可见
        Material mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        mat.color = Color.white;
        lineRenderer.material = mat;
    }

    private void Start()
    {
        GenerateArc();
    }

    public void GenerateArc()
    {
        if (edgeCollider == null) edgeCollider = GetComponent<EdgeCollider2D>();
        if (lineRenderer == null) SetupLineRenderer();

        Vector2[] points = new Vector2[segments + 1];
        float startRad = startAngle * Mathf.Deg2Rad;
        float endRad = (startAngle + arcAngle) * Mathf.Deg2Rad;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(startRad, endRad, t);
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        edgeCollider.points = points;

        lineRenderer.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++)
            lineRenderer.SetPosition(i, points[i]);
    }

    private void OnValidate()
    {
        if (edgeCollider == null) edgeCollider = GetComponent<EdgeCollider2D>();
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.startWidth = thickness;
            lineRenderer.endWidth = thickness;
        }
        GenerateArc();
    }
}
