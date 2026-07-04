using UnityEngine;

public class PistonRod : MonoBehaviour
{
    [Header("伸缩设置")]
    [SerializeField] private float extendSpeed = 2f;
    [SerializeField] private float maxLength = 3f;
    [SerializeField] private float minLength = 0.5f;

    [Header("杆宽度")]
    [SerializeField] private float rodWidth = 0.4f;

    [Header("头引用")]
    [SerializeField] private Transform head;
    [Header("头大小")]
    [SerializeField] private float headSize = 0.8f;
    [SerializeField] private float headThickness = 0.3f;

    private void Update()
    {
        // 用正弦波驱动伸缩
        float phase = (Mathf.Sin(Time.time * extendSpeed) + 1f) * 0.5f;
        float currentLength = Mathf.Lerp(minLength, maxLength, phase);

        // 杆：以顶部为锚点，向下拉伸
        transform.localScale = new Vector3(rodWidth, currentLength, 1f);
        transform.localPosition = new Vector3(0f, -currentLength * 0.5f, 0f);

        // 头：跟随杆底端
        if (head != null)
        {
            float headY = -currentLength - headThickness * 0.5f;
            head.localPosition = new Vector3(0f, headY, 0f);
            head.localScale = new Vector3(headSize, headThickness, 1f);
        }
    }
}
