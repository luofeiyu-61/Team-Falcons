using UnityEngine;

public class Spike : MonoBehaviour
{
    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        Vector2 deathPosition = other.attachedRigidbody != null
            ? other.attachedRigidbody.position
            : (Vector2)other.transform.position;

        GameEventBus.Publish(new PlayerDiedEvent(deathPosition));
    }
}
