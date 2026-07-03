using UnityEngine;

public class PlayerRespawn : MonoBehaviour
{
    [Header("玩家刚体")]
    [SerializeField] private Rigidbody2D playerRb;

    [Header("死亡时需要禁用的控制脚本")]
    [SerializeField] private MonoBehaviour[] controlScripts;

    private void Awake()
    {
        if (playerRb == null)
        {
            playerRb = GetComponent<Rigidbody2D>();
        }
    }

    // 玩家死亡后：停止运动、禁止操作
    public void EnterDeadState()
    {
        SetControlEnabled(false);

        if (playerRb != null)
        {
            playerRb.velocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
            playerRb.simulated = false;
        }
    }

    // 关卡控制器调用：让玩家从指定位置重生
    public void RespawnAt(Vector2 position)
    {
        transform.position = position;

        if (playerRb != null)
        {
            playerRb.simulated = true;
            playerRb.position = position;
            playerRb.velocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
        }

        SetControlEnabled(true);
    }

    // 通关后冻结玩家
    public void FreezePlayer()
    {
        SetControlEnabled(false);

        if (playerRb != null)
        {
            playerRb.velocity = Vector2.zero;
            playerRb.angularVelocity = 0f;
            playerRb.simulated = false;
        }
    }

    private void SetControlEnabled(bool enabled)
    {
        foreach (MonoBehaviour script in controlScripts)
        {
            if (script != null)
            {
                script.enabled = enabled;
            }
        }
    }
}