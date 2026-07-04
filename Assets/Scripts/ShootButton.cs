using UnityEngine;

public class ShootButton : MonoBehaviour
{
    [Header("控制目标")]
    [SerializeField] private MonoBehaviour controller;

    // Laser 检测到此按钮时调用
    public void Active()
    {
        if (controller is IShootControllable shootable)
        {
            shootable.OnShootActivate();
        }
    }
}
