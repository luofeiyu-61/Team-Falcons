using UnityEngine;

public class PickupItem : MonoBehaviour
{
    [Header("道具类型")]
    [SerializeField] private AnchorMode pickupMode = AnchorMode.Attract;

    [Header("增加额度")]
    [SerializeField] private int chargeAmount = 1;

    [Header("检测设置")]
    [SerializeField] private LayerMask playerLayer;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        GameEventBus.Publish(
            new AnchorModeChangedEvent(pickupMode, chargeAmount)
        );

        Destroy(gameObject);
    }
}
