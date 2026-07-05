using UnityEngine;

public class BlackHole : MonoBehaviour
{
    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer = 1 << 3; // 默认 Player 层 (index 3)

    private bool hasKilledPlayer;

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleTrigger(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        HandleTrigger(other);
    }

    private void HandleTrigger(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            if (hasKilledPlayer)
                return;

            hasKilledPlayer = true;

            // 主角接触黑点：发送死亡事件，由 LevelManager 处理重生
            Vector2 deathPosition = other.attachedRigidbody != null
                ? other.attachedRigidbody.position
                : (Vector2)other.transform.position;

            GameEventBus.Publish(new PlayerDiedEvent(deathPosition));
        }
        else if (other.CompareTag("Gravitable"))
        {
            // 可吸附物体进入：直接销毁，不触发关卡重开
            Destroy(other.gameObject);
        }
    }
}
