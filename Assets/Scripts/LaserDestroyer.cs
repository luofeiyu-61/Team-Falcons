using UnityEngine;

public class LaserDestroyer : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<Laser>() != null)
            Destroy(other.gameObject);
    }
}
