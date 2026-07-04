using UnityEngine;

// 可被 ShootButton 控制的物体实现此接口
public interface IShootControllable
{
    void OnShootActivate();
}
