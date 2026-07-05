using System.Collections.Generic;
using UI.InGame;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

public class AnchorManager : MonoBehaviour
{
    [Header("基础引用")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Anchor attractAnchorPrefab;
    [SerializeField] private Anchor repelAnchorPrefab;

    [Header("资源限制")]
    [SerializeField] private int placementCost = 1;
    [SerializeField] private int maxActiveAnchors = 1;
    [SerializeField] private bool refundWhenRemoved = false;

    private int maxAttractCharges = 4;
    private int maxRepelCharges = 4;

    [Header("放置限制")]
    [SerializeField] private LayerMask anchorLayer;
    [SerializeField] private LayerMask blockedLayer;
    [SerializeField] private float placementClearRadius = 0.25f;

    private AnchorMode selectedMode = AnchorMode.Attract;
    private AnchorMode SelectedMode
    {
        get => selectedMode;
        set
        {
            selectedMode = value;
            BatterySlots.HandleInputSelection(selectedMode);
        }
    }

    private int attractCharges;
    private int repelCharges;
    private bool attractUnlocked = false;
    private bool repelUnlocked = false;

    public BatterySlots BatterySlots { private get; set; }

    private readonly List<Anchor> activeAnchors = new();

    public int AttractCharges
    {
        get => attractCharges;
        private set
        {
            attractCharges = value;
            BatterySlots.BlueCount = attractCharges;
        }
    }
    public int RepelCharges
    {
        get => repelCharges;
        private set
        {
            repelCharges = value;
            BatterySlots.RedCount = repelCharges;
        }
    }
    
    public int RemainingCharges => SelectedMode == AnchorMode.Attract
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
        // 同步 UI 最大显示值与持有上限
        BatterySlots.MaxCount = Mathf.Max(maxAttractCharges, maxRepelCharges);

        // 初始额度为零，需要拾取道具才能获得
        AttractCharges = 0;
        RepelCharges = 0;
    }

    // 道具拾取：切换模式并增加对应额度（不超过上限）
    private void HandleAnchorModeChanged(AnchorModeChangedEvent gameEvent)
    {
        if (gameEvent.Mode == AnchorMode.Attract)
        {
            attractUnlocked = true;
            AttractCharges = Mathf.Min(AttractCharges + gameEvent.ChargeAmount, maxAttractCharges);
        }
        else
        {
            repelUnlocked = true;
            RepelCharges = Mathf.Min(RepelCharges + gameEvent.ChargeAmount, maxRepelCharges);
        }

        SelectedMode = gameEvent.Mode;
    }

    // 获取当前模式可用额度
    private int GetRemainingCharges()
    {
        return SelectedMode == AnchorMode.Attract
            ? AttractCharges
            : RepelCharges;
    }

    // 扣除当前模式额度
    private void SpendCharge()
    {
        if (SelectedMode == AnchorMode.Attract)
            AttractCharges -= placementCost;
        else
            RepelCharges -= placementCost;
    }

    // 根据当前模式获取对应 Prefab
    private Anchor GetActivePrefab()
    {
        return SelectedMode == AnchorMode.Attract
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

        // E 键切换模式
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (SelectedMode == AnchorMode.Attract && repelUnlocked)
                SelectedMode = AnchorMode.Repel;
            else if (SelectedMode == AnchorMode.Repel && attractUnlocked)
                SelectedMode = AnchorMode.Attract;
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

        newAnchor.SetMode(SelectedMode);

        activeAnchors.Add(newAnchor);
        SpendCharge();
    }

    private void TryRemoveAnchor()
    {
        AnchorMode removedMode = AnchorMode.Attract;

        // 右键一次销毁所有已放置的 Anchor
        foreach (Anchor anchor in activeAnchors)
        {
            if (anchor != null)
            {
                removedMode = anchor.GetMode();

                if (refundWhenRemoved)
                {
                    if (removedMode == AnchorMode.Attract)
                        AttractCharges += placementCost;
                    else
                        RepelCharges += placementCost;
                }

                Destroy(anchor.gameObject);
            }
        }

        activeAnchors.Clear();

        // 撤销锚点后自动切枪：当前模式没余额时才切到另一边
        if (SelectedMode == AnchorMode.Attract && AttractCharges == 0 && repelUnlocked && RepelCharges > 0)
            SelectedMode = AnchorMode.Repel;
        else if (SelectedMode == AnchorMode.Repel && RepelCharges == 0 && attractUnlocked && AttractCharges > 0)
            SelectedMode = AnchorMode.Attract;
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

    /// 当前是否存在活跃的斥力锚点
    public bool HasActiveRepel()
    {
        foreach (Anchor anchor in activeAnchors)
        {
            if (anchor != null && anchor.GetMode() == AnchorMode.Repel)
                return true;
        }
        return false;
    }

    // 可绑定 UI 按钮
    public void SelectAttract()
    {
        SelectedMode = AnchorMode.Attract;
    }

    public void SelectRepel()
    {
        if (!repelUnlocked)
            return;

        SelectedMode = AnchorMode.Repel;
    }
}