using UnityEngine;

public enum LaserDirection
{
    Left,   // 向左
    Right   // 向右
}

public class LaserTurret : MonoBehaviour
{
    [Header("激光设置")]
    [SerializeField] private Transform firePoint;       // 激光发射点（空物体）
    [SerializeField] private Texture2D laserTexture;    // 激光贴图
    [SerializeField] private float fireInterval = 2f;    // 发射间隔（秒）
    [SerializeField] private float laserSpeed = 8f;      // 激光飞行速度
    [SerializeField] private float laserLifetime = 5f;   // 激光存活时间（秒）
    [SerializeField] private LaserDirection fireDirection = LaserDirection.Right; // 发射方向

    [Header("目标")]
    [SerializeField] private LayerMask playerLayer = 1 << 3; // Player 层

    private float fireTimer;
    private Sprite laserSprite;

    private void Start()
    {
        // 从 Texture2D 运行时创建 Sprite
        if (laserTexture != null)
        {
            laserSprite = Sprite.Create(
                laserTexture,
                new Rect(0, 0, laserTexture.width, laserTexture.height),
                new Vector2(0.5f, 0.5f),
                100f
            );
        }

        fireTimer = fireInterval;
    }

    private void Update()
    {
        fireTimer -= Time.deltaTime;

        if (fireTimer <= 0f)
        {
            FireLaser();
            fireTimer = fireInterval;
        }
    }

    private void FireLaser()
    {
        if (firePoint == null || laserSprite == null)
            return;

        // 使用手动设置的发射方向
        float direction = fireDirection == LaserDirection.Right ? 1f : -1f;

        // 创建激光物体
        GameObject laser = new GameObject("Laser");
        laser.transform.position = firePoint.position;
        laser.layer = LayerMask.NameToLayer("Default");
        laser.tag = "Gravitable";
        laser.transform.localScale = new Vector3(0.1f, 1f, 1f);

        // 精灵渲染器
        SpriteRenderer sr = laser.AddComponent<SpriteRenderer>();
        sr.sprite = laserSprite;
        sr.flipX = direction < 0f;

        // Kinematic 刚体，确保 Trigger 检测正常
        Rigidbody2D rb = laser.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;

        // 触发碰撞体
        BoxCollider2D col = laser.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        // 挂载激光行为脚本
        Laser laserScript = laser.AddComponent<Laser>();
        laserScript.Initialize(direction, laserSpeed, laserLifetime, playerLayer);

        // 超时自动销毁
        Destroy(laser, laserLifetime);
    }
}
