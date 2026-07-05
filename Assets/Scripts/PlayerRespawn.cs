using UnityEngine;

public class PlayerRespawn : MonoBehaviour
{
    [Header("Player Rigidbody")]
    [SerializeField] private Rigidbody2D playerRb;

    [Header("Control Scripts")]
    [SerializeField] private MonoBehaviour[] controlScripts;

    public bool IsDead { get; private set; }

    private void Awake()
    {
        if (playerRb == null)
            playerRb = GetComponent<Rigidbody2D>();
    }

    public void EnterDeadState()
    {
        IsDead = true;
        SetControlEnabled(false);

        if (playerRb == null)
            return;

        playerRb.velocity = Vector2.zero;
        playerRb.angularVelocity = 0f;
    }

    public void RespawnAt(Vector2 position)
    {
        IsDead = false;
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

    public void FreezePlayer()
    {
        IsDead = true;
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
                script.enabled = enabled;
        }
    }
}
