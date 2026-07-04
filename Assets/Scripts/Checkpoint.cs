using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer;

    [Header("检查点ID")]
    [SerializeField] private string checkpointId = "Exit";

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        Vector2 position = other.attachedRigidbody != null
            ? other.attachedRigidbody.position
            : (Vector2)other.transform.position;

        GameEventBus.Publish(new ExitReachedEvent(checkpointId));
    }
}
