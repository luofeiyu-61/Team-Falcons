using UnityEngine;

public class Laser : MonoBehaviour
{
    [Header("检测设置")]
    [SerializeField] private LayerMask buttonLayer;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((buttonLayer.value & (1 << other.gameObject.layer)) == 0)
            return;

        ShootButton button = other.GetComponent<ShootButton>();
        if (button == null)
            button = other.GetComponentInParent<ShootButton>();

        if (button != null)
            button.Active();
    }
}
