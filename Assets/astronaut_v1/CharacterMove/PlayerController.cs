using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("�ƶ�����")]
    [SerializeField] private float moveSpeed = 5f;
    [Header("��Ծ����")]
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float jumpCutMultiplier = 0.5f;     // ��ǰ��ֹ��Ծϵ��
    [Header("�ָ��Ż�")]
    [SerializeField] private float coyoteTime = 0.1f;            // ����ʱ��
    [SerializeField] private float jumpBufferTime = 0.1f;        // ��Ծ����ʱ��
    private Rigidbody2D rb;
    private MainCharacter controls;
    private Vector2 moveInput;
    private bool isGrounded;
    private float coyoteCounter;
    private float jumpBufferCounter;
    private Animator animator;
    private SpriteRenderer sr;
    private bool isDead = false;
    [SerializeField] private bool faceRightWhenMovingRight = true;
    private bool isFacingRight = true;
    private const float MoveDeadZone = 0.01f;

    [Header("地面检测")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.18f;
    [SerializeField] private LayerMask groundLayer = 1 << 6; // 默认 Ground 层
    [Header("Virtual Camera")]
    public CinemachineVirtualCamera virtualCamera1;
    public CinemachineVirtualCamera virtualCamera2;
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        controls = new MainCharacter();
        animator = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();
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

    void Start()
    {
        Invoke("SwitchCamera", 1.5f);

    }
    private void Update()
    {
        if (isDead) return;
        moveInput = controls.player.Move.ReadValue<Vector2>();

        isGrounded = groundCheck != null
            ? Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer)
            : false;

        if (isGrounded)
            coyoteCounter = coyoteTime;
        else
            coyoteCounter -= Time.deltaTime;

        jumpBufferCounter -= Time.deltaTime;

        if (jumpBufferCounter > 0f && coyoteCounter > 0f)
        {
 
            rb.velocity = new Vector2(rb.velocity.x, jumpForce);

            jumpBufferCounter = 0f;
            coyoteCounter = 0f;
            isGrounded = false;  
        }

        UpdateFacing(moveInput.x);
    }
    private AnchorManager anchorManager;
    private float lastPlayerVelocityX;

    private void FixedUpdate()
    {
        if (isDead) return;

        if (anchorManager == null)
            anchorManager = FindObjectOfType<AnchorManager>();

        float horizontal = moveInput.x;
        bool repelActive = anchorManager != null && anchorManager.HasActiveRepel();

        float targetVelX = horizontal * moveSpeed;

        // 空中且斥力激活：保留外部水平力（锚点推力），避免被玩家输入覆盖
        // 地面时：直接设置速度，让物理自然处理接触，防止弹跳
        if (repelActive && !isGrounded)
        {
            float externalX = rb.velocity.x - lastPlayerVelocityX;
            rb.velocity = new Vector2(targetVelX + externalX, rb.velocity.y);
        }
        else
        {
            rb.velocity = new Vector2(targetVelX, rb.velocity.y);
        }
        lastPlayerVelocityX = targetVelX;
        animator.SetFloat("Walk", Mathf.Abs(horizontal) > MoveDeadZone ? moveSpeed : 0f);
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
        sr.flipX = faceRightWhenMovingRight ? !isFacingRight : isFacingRight;
    }

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (isDead) return;
        animator.SetTrigger("Jump");
        jumpBufferCounter = jumpBufferTime;
        Debug.Log("good jump");
    }

    private void OnJumpCanceled(InputAction.CallbackContext ctx)
    {
        if (rb.velocity.y > 0f)
        {
            rb.velocity = new Vector2(rb.velocity.x, rb.velocity.y * jumpCutMultiplier);
        }
    }
    private void PlayerRespawned(PlayerDiedEvent gameEvent)
    {
        isDead = true;
        animator.SetTrigger("dead");
    }

    private void PlayerRespawned(PlayerRespawnedEvent gameEvent)
    {
        isDead = false;
        animator.ResetTrigger("dead");
        animator.Play("breath", 0, 0f);
    }

    private void SwitchCamera()
    {
        if (virtualCamera1 != null && virtualCamera2 != null)
        {
            virtualCamera1.Priority = 12;
            virtualCamera2.Priority = 10;
        }
    }
}
