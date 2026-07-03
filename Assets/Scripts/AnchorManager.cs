using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class AnchorManager : MonoBehaviour
{
    [Header("基础引用")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Anchor anchorPrefab;

    [Header("资源限制")]
    [SerializeField] private int startingCharges = 5;
    [SerializeField] private int placementCost = 1;
    [SerializeField] private int maxActiveAnchors = 3;
    [SerializeField] private bool refundWhenRemoved = false;

    [Header("放置限制")]
    [SerializeField] private LayerMask anchorLayer;
    [SerializeField] private LayerMask blockedLayer;
    [SerializeField] private float placementClearRadius = 0.25f;

    [Header("锚点模式")]
    [SerializeField] private AnchorMode selectedMode = AnchorMode.Attract;

    private int remainingCharges;
    private bool repelUnlocked = false;

    private readonly List<Anchor> activeAnchors = new();

    public int RemainingCharges => remainingCharges;
    public int ActiveAnchorCount => activeAnchors.Count;

    private void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
    }

    private void Start()
    {
        remainingCharges = startingCharges;
    }

    private void Update()
    {
        activeAnchors.RemoveAll(anchor => anchor == null);

        // 鼠标在 UI 上时，不触发创建和销毁
        if (EventSystem.current != null &&
            EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryPlaceAnchor();
        }

        if (Input.GetMouseButtonDown(1))
        {
            TryRemoveAnchor();
        }
    }

    private void TryPlaceAnchor()
    {
        if (anchorPrefab == null)
            return;

        if (remainingCharges < placementCost)
            return;

        if (maxActiveAnchors > 0 &&
            activeAnchors.Count >= maxActiveAnchors)
        {
            return;
        }

        Vector2 mouseWorldPosition = GetMouseWorldPosition();

        // 不能放在墙体、平台内部
        bool blocked = Physics2D.OverlapCircle(
            mouseWorldPosition,
            placementClearRadius,
            blockedLayer
        );

        if (blocked)
            return;

        Anchor newAnchor = Instantiate(
            anchorPrefab,
            mouseWorldPosition,
            Quaternion.identity
        );

        newAnchor.SetMode(selectedMode);

        activeAnchors.Add(newAnchor);
        remainingCharges -= placementCost;
    }

    private void TryRemoveAnchor()
    {
        Vector2 mouseWorldPosition = GetMouseWorldPosition();

        Collider2D hit = Physics2D.OverlapPoint(
            mouseWorldPosition,
            anchorLayer
        );

        if (hit == null)
            return;

        Anchor anchor = hit.GetComponentInParent<Anchor>();

        if (anchor == null)
            return;

        activeAnchors.Remove(anchor);

        if (refundWhenRemoved)
        {
            remainingCharges = Mathf.Min(
                remainingCharges + placementCost,
                startingCharges
            );
        }

        Destroy(anchor.gameObject);
    }

    private Vector2 GetMouseWorldPosition()
    {
        Ray ray = gameplayCamera.ScreenPointToRay(Input.mousePosition);

        Plane gamePlane = new Plane(
            Vector3.forward,
            Vector3.zero
        );

        gamePlane.Raycast(ray, out float distance);

        return ray.GetPoint(distance);
    }

    // 后续关卡、道具、剧情事件可调用
    public void UnlockRepel()
    {
        repelUnlocked = true;
    }

    // 可绑定 UI 按钮
    public void SelectAttract()
    {
        selectedMode = AnchorMode.Attract;
    }

    public void SelectRepel()
    {
        if (!repelUnlocked)
            return;

        selectedMode = AnchorMode.Repel;
    }
}