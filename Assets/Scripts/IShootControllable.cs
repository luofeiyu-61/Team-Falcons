using UnityEngine;

// 可被 ShootButton 控制的物体实现此接口
public interface IShootControllable
{
    void OnShootActivate();
    void OnShootDeactivate();

    /// 玩家踩住按钮时为 true，离开时为 false。用于阻断 holdTime 自动回位。
    bool BeingHeld { get; set; }
}
