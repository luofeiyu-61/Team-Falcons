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

    [Header("Step Assist")]
    [SerializeField] private float stepHeight = 0.1f;
    [SerializeField] private float stepProbeDistance = 0.1f;

    [Header("Facing")]
    [SerializeField] private bool faceRightWhenMovingRight = true;

    [Header("Virtual Camera")]
    public CinemachineVirtualCamera virtualCamera1;
    public CinemachineVirtualCamera virtualCamera2;

    private const float MoveDeadZone = 0.01f;
    private const int StepProbeCount = 3;
    private const float StepSearchDepth = 0.15f;
    private const float StallSpeedLoss = 0.3f;

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
    private Collider2D playerCollider;
    private float commandedVx;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
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
        TryStepOverSmallObstacle(moveInput.x);
        ApplyFallGravity();
        UpdateAnimator(moveInput.x);
        commandedVx = rb.velocity.x;
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

    private void TryStepOverSmallObstacle(float horizontal)
    {
        if (!isGrounded || playerCollider == null || Mathf.Abs(horizontal) <= MoveDeadZone)
            return;

        // React only when a contact ate the speed we commanded last frame (running into a
        // ledge), not while still accelerating normally toward the target speed.
        float direction = Mathf.Sign(horizontal);
        if (rb.velocity.x * direction >= commandedVx * direction - StallSpeedLoss)
            return;

        Bounds bounds = playerCollider.bounds;
        float footY = bounds.min.y;
        float edgeX = bounds.center.x + direction * bounds.extents.x;
        float castLength = bounds.extents.x + stepProbeDistance;

        RaycastHit2D wallHit = Physics2D.Raycast(
            new Vector2(bounds.center.x, footY + stepHeight),
            Vector2.right * direction,
            castLength,
            groundLayer);
        if (wallHit)
            return;

        // Probe several points ahead of the leading edge and keep the highest surface,
        // so thin ledges and seams right at the edge are not skipped by a single ray.
        float rise = 0f;
        bool foundSurface = false;
        for (int i = 0; i < StepProbeCount; i++)
        {
            float probeX = edgeX + direction * (stepProbeDistance * i / (StepProbeCount - 1));
            RaycastHit2D surfaceHit = Physics2D.Raycast(
                new Vector2(probeX, footY + stepHeight),
                Vector2.down,
                stepHeight + StepSearchDepth,
                groundLayer);
            if (!surfaceHit)
                continue;

            foundSurface = true;
            rise = Mathf.Max(rise, surfaceHit.point.y - footY);
        }

        if (!foundSurface)
            return;

        // A surface at foot level (rise ~= 0) is a flat seam/corner catch: lift just enough
        // to unhook the collider bottom instead of trying to climb a real step.
        float lift = rise > 0.001f ? rise + 0.01f : 0.01f;
        if (rise > 0.001f)
        {
            Vector2 raisedCenter = new Vector2(bounds.center.x, footY + lift + bounds.extents.y);
            Vector2 raisedSize = (Vector2)bounds.size - new Vector2(0.02f, 0.02f);
            if (Physics2D.OverlapBox(raisedCenter, raisedSize, 0f, groundContactFilter, groundHits) > 0)
                return;
        }

        // Restore full target speed so the step reads as one continuous movement instead
        // of a stop-and-restart.
        rb.position += Vector2.up * lift;
        rb.velocity = new Vector2(horizontal * moveSpeed, 0f);
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
        commandedVx = 0f;
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
        if (stepHeight < 0f) stepHeight = 0f;
        if (stepProbeDistance < 0f) stepProbeDistance = 0f;

        ConfigureGroundContactFilter();
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Collider2D col = playerCollider != null ? playerCollider : GetComponent<Collider2D>();
        if (col == null)
            return;

        Bounds bounds = col.bounds;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(
            new Vector3(bounds.min.x - 0.1f, bounds.min.y + stepHeight, 0f),
            new Vector3(bounds.max.x + 0.1f, bounds.min.y + stepHeight, 0f));
    }
}
