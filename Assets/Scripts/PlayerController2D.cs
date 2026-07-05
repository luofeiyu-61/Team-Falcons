using UnityEngine;

public class PlayerController2D : MonoBehaviour
{
    [Header("移动")]
    [SerializeField] private float maxMoveSpeed = 7f;
    [SerializeField] private float acceleration = 55f;

    [Header("跳跃")]
    [SerializeField] private float jumpSpeed = 12f;
    [SerializeField] private float jumpCutMultiplier = 0.5f;

    [Header("落地检测")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.18f;
    [SerializeField] private LayerMask groundLayer;

    [Header("手感优化")]
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    private Rigidbody2D rb;
    private float horizontalInput;
    private float coyoteCounter;
    private float jumpBufferCounter;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        // 防止高速时穿墙：开启连续碰撞检测
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
    }

    private void Update()
    {
        // A/D 左右移动
        horizontalInput = 0f;

        if (Input.GetKey(KeyCode.A))
            horizontalInput = -1f;

        if (Input.GetKey(KeyCode.D))
            horizontalInput = 1f;

        // 是否站在地面上
        bool isGrounded = Physics2D.OverlapCircle(
            groundCheck.position,
            groundCheckRadius,
            groundLayer
        );

        // 土狼时间：刚离开平台的一小段时间仍可跳跃
        if (isGrounded)
            coyoteCounter = coyoteTime;
        else
            coyoteCounter -= Time.deltaTime;

        // 跳跃输入缓存
        bool jumpPressed =
            Input.GetKeyDown(KeyCode.W) ||
            Input.GetKeyDown(KeyCode.Space);

        if (jumpPressed)
            jumpBufferCounter = jumpBufferTime;
        else
            jumpBufferCounter -= Time.deltaTime;

        // 执行跳跃
        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, jumpSpeed);

            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
        }

        // 松开跳跃键后，提前截断上升速度
        bool jumpReleased =
            Input.GetKeyUp(KeyCode.W) ||
            Input.GetKeyUp(KeyCode.Space);

        if (jumpReleased && rb.velocity.y > 0f)
        {
            rb.velocity = new Vector2(
                rb.velocity.x,
                rb.velocity.y * jumpCutMultiplier
            );
        }
    }

    private void FixedUpdate()
    {
        // 无水平输入时不覆写 X 速度，保留外部力（引力/斥力）累积的速度
        if (Mathf.Abs(horizontalInput) < 0.01f)
            return;

        float targetSpeed = horizontalInput * maxMoveSpeed;

        // 当前速度超过默认移动速度时不覆写，保留外部力效果
        if (Mathf.Abs(rb.velocity.x) > maxMoveSpeed)
            return;

        float newX = Mathf.MoveTowards(
            rb.velocity.x,
            targetSpeed,
            acceleration * Time.fixedDeltaTime
        );

        rb.velocity = new Vector2(newX, rb.velocity.y);
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;

        Gizmos.DrawWireSphere(
            groundCheck.position,
            groundCheckRadius
        );
    }
}