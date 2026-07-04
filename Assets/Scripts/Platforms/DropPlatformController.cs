using UnityEngine;

public class DropPlatformController : MonoBehaviour, IShootControllable
{
    [Header("下落设置")]
    [SerializeField] private float dropDistance = 3f;
    [SerializeField] private float dropSpeed = 5f;
    [SerializeField] private float returnSpeed = 3f;
    [SerializeField] private float holdTime = 2f;

    private Vector2 originalPosition;
    private Vector2 targetPosition;
    private bool isDropped = false;
    private float lastActivateTime;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        originalPosition = transform.position;
        targetPosition = originalPosition + Vector2.down * dropDistance;
    }

    public bool BeingHeld { get; set; }

    public void OnShootActivate()
    {
        isDropped = true;
        lastActivateTime = Time.time;
    }

    /// 玩家离开按钮时调用，门立即升起。
    public void OnShootDeactivate()
    {
        isDropped = false;
    }

    private void Update()
    {
        // 超过 holdTime 且没有被玩家踩住，回到原位（激光触发用）
        if (isDropped && !BeingHeld && Time.time - lastActivateTime > holdTime)
        {
            isDropped = false;
        }
    }

    private void FixedUpdate()
    {
        Vector2 destination = isDropped ? targetPosition : originalPosition;
        rb.MovePosition(Vector2.MoveTowards(
            transform.position,
            destination,
            (isDropped ? dropSpeed : returnSpeed) * Time.fixedDeltaTime
        ));
    }
}
