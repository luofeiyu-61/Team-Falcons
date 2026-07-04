using UnityEngine;

public class DropPlatformController : MonoBehaviour, IShootControllable
{
    [Header("下落设置")]
    [SerializeField] private float dropDistance = 3f;
    [SerializeField] private float dropSpeed = 5f;
    [SerializeField] private float returnSpeed = 3f;
    [SerializeField] private float holdTime = 2f;

    private Vector2 originalPosition;
    private Vector2 targetPosition;
    private bool isDropped = false;
    private float lastActivateTime;

    private void Awake()
    {
        originalPosition = transform.position;
        targetPosition = originalPosition + Vector2.down * dropDistance;
    }

    public void OnShootActivate()
    {
        isDropped = true;
        lastActivateTime = Time.time;
    }

    private void Update()
    {
        // 超过 holdTime 没有再次调用，回到原位
        if (isDropped && Time.time - lastActivateTime > holdTime)
        {
            isDropped = false;
        }

        Vector2 destination = isDropped ? targetPosition : originalPosition;
        transform.position = Vector2.MoveTowards(
            transform.position,
            destination,
            (isDropped ? dropSpeed : returnSpeed) * Time.deltaTime
        );
    }
}
