using UnityEngine;

public class Laser : MonoBehaviour
{
    private float direction;
    private float speed;
    private float lifetime;
    private LayerMask playerLayer;

    /// <summary>
    /// 由 LaserTurret 调用，初始化激光参数。
    /// </summary>
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
        // 只检测 Player 层
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        // 玩家被击中：发送死亡事件，由 LevelManager 处理重生
        Vector2 deathPosition = other.attachedRigidbody != null
            ? other.attachedRigidbody.position
            : (Vector2)other.transform.position;

        GameEventBus.Publish(new PlayerDiedEvent(deathPosition));

        Destroy(gameObject);
    }
}
