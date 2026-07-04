using UnityEngine;

public class Laser : MonoBehaviour
{
    [Header("检测设置")]
    [SerializeField] private string buttonTag = "Button";
    [SerializeField] private LayerMask playerLayer = 1 << 3; // 默认 Player 层

    private float direction;
    private float speed;
    private float lifetime;

    /// 由 LaserTurret 调用，初始化激光参数。
    public void Initialize(
        float dir,
        float spd,
        float life,
        LayerMask layer
    )
    {
        direction = dir;
        speed = spd;
        lifetime = life;
        playerLayer = layer;
    }

    private void Update()
    {
        // 水平方向匀速移动
        transform.position += new Vector3(
            direction * speed * Time.deltaTime,
            0f,
            0f
        );
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        int layer = other.gameObject.layer;

        // 碰到玩家：发送死亡事件，然后销毁激光
        if ((playerLayer.value & (1 << layer)) != 0)
        {
            Vector2 deathPosition = other.attachedRigidbody != null
                ? other.attachedRigidbody.position
                : (Vector2)other.transform.position;

            GameEventBus.Publish(new PlayerDiedEvent(deathPosition));

            Destroy(gameObject);
            return;
        }

        // 碰到 ShootButton：激活按钮，然后销毁激光
        if (other.CompareTag(buttonTag))
        {
            ShootButton button = other.GetComponent<ShootButton>();
            if (button == null)
                button = other.GetComponentInParent<ShootButton>();

            if (button != null)
                button.Active();

            Destroy(gameObject);
            return;
        }
    }
}
