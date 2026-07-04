using UnityEngine;

public class LaserTurret : MonoBehaviour
{
    [Header("激光设置")]
    [SerializeField] private Transform firePoint;       // 激光发射点（空物体），激光沿 firePoint.right 方向射出
    [SerializeField] private Texture2D laserTexture;    // 激光贴图
    [SerializeField] private float fireInterval = 2f;    // 发射间隔（秒）
    [SerializeField] private float laserSpeed = 8f;      // 激光飞行速度
    [SerializeField] private float laserLifetime = 5f;   // 激光存活时间（秒）

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

        // 激光沿 firePoint 的左方向（-X 轴）射出
        Vector2 fireDir = -firePoint.right;

        // 创建激光物体
        GameObject laser = new GameObject("Laser");
        laser.transform.position = firePoint.position;
        laser.transform.rotation = firePoint.rotation;
        laser.layer = LayerMask.NameToLayer("Default");
        laser.tag = "Gravitable";
        laser.transform.localScale = new Vector3(0.1f, 1f, 1f);

        // 精灵渲染器
        SpriteRenderer sr = laser.AddComponent<SpriteRenderer>();
        sr.sprite = laserSprite;

        // Dynamic 刚体，确保 Trigger 检测正常
        Rigidbody2D rb = laser.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;

        // 触发碰撞体
        BoxCollider2D col = laser.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        // 挂载激光行为脚本
        Laser laserScript = laser.AddComponent<Laser>();
        laserScript.Initialize(fireDir, laserSpeed, laserLifetime, playerLayer);

        // 超时自动销毁
        Destroy(laser, laserLifetime);
    }
}
