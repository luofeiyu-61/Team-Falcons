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
        GameEventBus.Subscribe<PlayerDiedEvent>(PlayerDied);
        GameEventBus.Subscribe<PlayerRespawnedEvent>(PlayerRespawned);
        controls.player.Enable();
        controls.player.Jump.performed += OnJumpPerformed;
        controls.player.Jump.canceled += OnJumpCanceled;
    }
    private void OnDisable()
    {
        GameEventBus.Unsubscribe<PlayerDiedEvent>(PlayerDied);
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

        if (rb.velocity.x > 0)
        {
            sr.flipX = false;
        }
        else if (rb.velocity.x < 0)
        {
            sr.flipX = true;
        }
    }
    private void FixedUpdate()
    {
        if (isDead) return;

        float horizontal = moveInput.x;
        rb.velocity = new Vector2(horizontal * moveSpeed, rb.velocity.y);
        if (rb.velocity.x != 0) { 
            animator.SetFloat("Walk", moveSpeed);
        }
        else
        {
            animator.SetFloat("Walk", 0);
        }
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
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
    }
    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = false;
    }
    
    private void PlayerDied(PlayerDiedEvent gameEvent)
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
        virtualCamera1.Priority = 12;
        virtualCamera2.Priority = 10;
    }
}
