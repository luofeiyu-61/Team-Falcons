using UnityEngine;

public class PlayerController2D : MonoBehaviour
{
    [Header("移动")]
    [SerializeField] private float maxMoveSpeed = 7f;
    [SerializeField] private float acceleration = 55f;
    [SerializeField] private float deceleration = 70f;

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
        float targetSpeed = horizontalInput * maxMoveSpeed;

        float rate = Mathf.Abs(targetSpeed) > 0.01f
            ? acceleration
            : deceleration;

        float newX = Mathf.MoveTowards(
            rb.velocity.x,
            targetSpeed,
            rate * Time.fixedDeltaTime
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