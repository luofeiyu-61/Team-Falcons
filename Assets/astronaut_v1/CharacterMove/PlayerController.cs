using Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float groundAcceleration = 55f;
    [SerializeField] private float groundDeceleration = 70f;
    [SerializeField] private float airAcceleration = 30f;
    [SerializeField] private float airDeceleration = 8f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float jumpCutMultiplier = 0.5f;
    [SerializeField] private float fallGravityMultiplier = 1.6f;
    [SerializeField] private float maxFallSpeed = 16f;

    [Header("Jump Assist")]
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.18f;
    [SerializeField] private LayerMask groundLayer = 1 << 6;

    [Header("Facing")]
    [SerializeField] private bool faceRightWhenMovingRight = true;

    [Header("Virtual Camera")]
    public CinemachineVirtualCamera virtualCamera1;
    public CinemachineVirtualCamera virtualCamera2;

    private const float MoveDeadZone = 0.01f;

    private Rigidbody2D rb;
    private MainCharacter controls;
    private Vector2 moveInput;
    private bool isGrounded;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private float defaultGravityScale;
    private Animator animator;
    private SpriteRenderer sr;
    private bool isDead;
    private bool isFacingRight = true;
    private bool jumpCutQueued;
    private ContactFilter2D groundContactFilter;
    private readonly Collider2D[] groundHits = new Collider2D[4];

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        controls = new MainCharacter();
        animator = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
        defaultGravityScale = rb.gravityScale;

        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        ConfigureGroundContactFilter();
    }

    private void OnEnable()
    {
        GameEventBus.Subscribe<PlayerDiedEvent>(PlayerRespawned);
        GameEventBus.Subscribe<PlayerRespawnedEvent>(PlayerRespawned);
        controls.player.Enable();
        controls.player.Jump.performed += OnJumpPerformed;
        controls.player.Jump.canceled += OnJumpCanceled;
    }

    private void OnDisable()
    {
        GameEventBus.Unsubscribe<PlayerDiedEvent>(PlayerRespawned);
        GameEventBus.Unsubscribe<PlayerRespawnedEvent>(PlayerRespawned);
        controls.player.Jump.performed -= OnJumpPerformed;
        controls.player.Jump.canceled -= OnJumpCanceled;
        controls.player.Disable();
    }

    private void Start()
    {
        Invoke(nameof(SwitchCamera), 1.5f);
    }

    private void Update()
    {
        if (isDead)
            return;

        moveInput = controls.player.Move.ReadValue<Vector2>();
        jumpBufferCounter -= Time.deltaTime;
        UpdateFacing(moveInput.x);
    }

    private void FixedUpdate()
    {
        if (isDead)
            return;

        UpdateGroundedState();
        UpdateCoyoteCounter();
        TryConsumeBufferedJump();
        ApplyJumpCut();
        ApplyHorizontalMovement(moveInput.x);
        ApplyFallGravity();
        UpdateAnimator(moveInput.x);
    }

    private void UpdateGroundedState()
    {
        if (groundCheck == null)
        {
            isGrounded = false;
            return;
        }

        isGrounded = Physics2D.OverlapCircle(
            groundCheck.position,
            groundCheckRadius,
            groundContactFilter,
            groundHits) > 0;
    }

    private void UpdateCoyoteCounter()
    {
        if (isGrounded)
            coyoteCounter = coyoteTime;
        else
            coyoteCounter -= Time.fixedDeltaTime;
    }

    private void TryConsumeBufferedJump()
    {
        if (jumpBufferCounter <= 0f || coyoteCounter <= 0f)
            return;

        rb.velocity = new Vector2(rb.velocity.x, jumpForce);
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        isGrounded = false;
    }

    private void ApplyJumpCut()
    {
        if (!jumpCutQueued)
            return;

        if (rb.velocity.y > 0f)
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * jumpCutMultiplier);

        jumpCutQueued = false;
    }

    private void ApplyHorizontalMovement(float horizontal)
    {
        bool hasInput = Mathf.Abs(horizontal) > MoveDeadZone;
        float targetSpeed = hasInput ? horizontal * moveSpeed : 0f;
        float acceleration = GetHorizontalAcceleration(hasInput);
        float newVelocityX = Mathf.MoveTowards(
            rb.velocity.x,
            targetSpeed,
            acceleration * Time.fixedDeltaTime);

        rb.velocity = new Vector2(newVelocityX, rb.velocity.y);
    }

    private float GetHorizontalAcceleration(bool hasInput)
    {
        if (hasInput)
            return isGrounded ? groundAcceleration : airAcceleration;

        return isGrounded ? groundDeceleration : airDeceleration;
    }

    private void ApplyFallGravity()
    {
        rb.gravityScale = rb.velocity.y < -0.01f
            ? defaultGravityScale * fallGravityMultiplier
            : defaultGravityScale;

        if (rb.velocity.y < -maxFallSpeed)
            rb.velocity = new Vector2(rb.velocity.x, -maxFallSpeed);
    }

    private void UpdateAnimator(float horizontal)
    {
        if (animator == null)
            return;

        float walkSpeed = Mathf.Abs(horizontal) > MoveDeadZone
            ? Mathf.Abs(rb.velocity.x)
            : 0f;

        animator.SetFloat("Walk", walkSpeed);
    }

    private void LateUpdate()
    {
        ApplyFacing();
    }

    private void UpdateFacing(float horizontal)
    {
        if (horizontal > MoveDeadZone)
        {
            isFacingRight = true;
        }
        else if (horizontal < -MoveDeadZone)
        {
            isFacingRight = false;
        }

        ApplyFacing();
    }

    private void ApplyFacing()
    {
        if (sr == null)
            return;

        sr.flipX = faceRightWhenMovingRight ? !isFacingRight : isFacingRight;
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (isDead)
            return;

        if (animator != null)
            animator.SetTrigger("Jump");

        jumpBufferCounter = jumpBufferTime;
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        if (rb.velocity.y > 0f)
            jumpCutQueued = true;
    }

    private void PlayerRespawned(PlayerDiedEvent gameEvent)
    {
        isDead = true;
        rb.gravityScale = defaultGravityScale;

        if (animator != null)
            animator.SetTrigger("dead");
    }

    private void PlayerRespawned(PlayerRespawnedEvent gameEvent)
    {
        isDead = false;
        jumpCutQueued = false;
        jumpBufferCounter = 0f;
        coyoteCounter = 0f;
        rb.gravityScale = defaultGravityScale;

        if (animator != null)
        {
            animator.ResetTrigger("dead");
            animator.Play("breath", 0, 0f);
        }
    }

    private void SwitchCamera()
    {
        if (virtualCamera1 != null && virtualCamera2 != null)
        {
            virtualCamera1.Priority = 12;
            virtualCamera2.Priority = 10;
        }
    }

    private void ConfigureGroundContactFilter()
    {
        groundContactFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = groundLayer,
            useTriggers = false
        };
    }

    private void OnValidate()
    {
        if (groundAcceleration < 0f) groundAcceleration = 0f;
        if (groundDeceleration < 0f) groundDeceleration = 0f;
        if (airAcceleration < 0f) airAcceleration = 0f;
        if (airDeceleration < 0f) airDeceleration = 0f;
        if (fallGravityMultiplier < 1f) fallGravityMultiplier = 1f;
        if (maxFallSpeed < 0f) maxFallSpeed = 0f;
        if (groundCheckRadius < 0f) groundCheckRadius = 0f;

        ConfigureGroundContactFilter();
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.color = isGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
