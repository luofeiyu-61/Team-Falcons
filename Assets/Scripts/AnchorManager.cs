using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class AnchorManager : MonoBehaviour
{
    [Header("基础引用")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Anchor attractAnchorPrefab;
    [SerializeField] private Anchor repelAnchorPrefab;

    [Header("资源限制")]
    [SerializeField] private int placementCost = 1;
    [SerializeField] private int maxActiveAnchors = 3;
    [SerializeField] private bool refundWhenRemoved = false;

    [Header("放置限制")]
    [SerializeField] private LayerMask anchorLayer;
    [SerializeField] private LayerMask blockedLayer;
    [SerializeField] private float placementClearRadius = 0.25f;
    [SerializeField] private float removeRadius = 1.5f;

    [Header("锚点模式")]
    [SerializeField] private AnchorMode selectedMode = AnchorMode.Attract;

    private int attractCharges;
    private int repelCharges;
    private bool repelUnlocked = false;

    private readonly List<Anchor> activeAnchors = new();

    public int AttractCharges => attractCharges;
    public int RepelCharges => repelCharges;
    public int RemainingCharges => selectedMode == AnchorMode.Attract
        ? attractCharges
        : repelCharges;
    public int ActiveAnchorCount => activeAnchors.Count;

    private void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
    }

    private void OnEnable()
    {
        GameEventBus.Subscribe<AnchorModeChangedEvent>(HandleAnchorModeChanged);
    }

    private void OnDisable()
    {
        GameEventBus.Unsubscribe<AnchorModeChangedEvent>(HandleAnchorModeChanged);
    }

    private void Start()
    {
        // 初始额度为零，需要拾取道具才能获得
        attractCharges = 0;
        repelCharges = 0;
    }

    // 道具拾取：切换模式并增加对应额度
    private void HandleAnchorModeChanged(AnchorModeChangedEvent gameEvent)
    {
        if (gameEvent.Mode == AnchorMode.Repel)
            repelUnlocked = true;

        selectedMode = gameEvent.Mode;

        if (gameEvent.Mode == AnchorMode.Attract)
            attractCharges += gameEvent.ChargeAmount;
        else
            repelCharges += gameEvent.ChargeAmount;
    }

    // 获取当前模式可用额度
    private int GetRemainingCharges()
    {
        return selectedMode == AnchorMode.Attract
            ? attractCharges
            : repelCharges;
    }

    // 扣除当前模式额度
    private void SpendCharge()
    {
        if (selectedMode == AnchorMode.Attract)
            attractCharges -= placementCost;
        else
            repelCharges -= placementCost;
    }

    // 根据当前模式获取对应 Prefab
    private Anchor GetActivePrefab()
    {
        return selectedMode == AnchorMode.Attract
            ? attractAnchorPrefab
            : repelAnchorPrefab;
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
        Anchor activePrefab = GetActivePrefab();

        if (activePrefab == null)
            return;

        if (GetRemainingCharges() < placementCost)
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
            activePrefab,
            mouseWorldPosition,
            Quaternion.identity
        );

        newAnchor.SetMode(selectedMode);

        activeAnchors.Add(newAnchor);
        SpendCharge();
    }

    private void TryRemoveAnchor()
    {
        Vector2 mouseWorldPosition = GetMouseWorldPosition();

        // 在 removeRadius 范围内找最近的 Anchor
        Anchor closest = null;
        float closestDist = removeRadius;

        foreach (Anchor anchor in activeAnchors)
        {
            if (anchor == null)
                continue;

            float dist = Vector2.Distance(
                mouseWorldPosition,
                anchor.transform.position
            );

            if (dist < closestDist)
            {
                closestDist = dist;
                closest = anchor;
            }
        }

        if (closest == null)
            return;

        activeAnchors.Remove(closest);

        if (refundWhenRemoved)
        {
            if (closest.GetMode() == AnchorMode.Attract)
                attractCharges += placementCost;
            else
                repelCharges += placementCost;
        }

        Destroy(closest.gameObject);
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