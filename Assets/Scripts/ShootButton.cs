using System.Collections;
using UnityEngine;

public class ShootButton : MonoBehaviour
{
    [Header("控制目标")]
    [SerializeField] private MonoBehaviour controller;

    [Header("玩家触发")]
    [SerializeField] private LayerMask playerLayer = 1 << 3; // 默认 Player 层
    [SerializeField] private float leaveDelay = 0.5f;         // 玩家离开后延迟多久触发释放

    private int playersOnButton;
    private Coroutine leaveCoroutine;

    /// 激光命中时调用。
    public void Active()
    {
        if (controller is IShootControllable shootable)
        {
            shootable.OnShootActivate();
        }
    }

    /// 玩家离开后延迟调用，门升起。
    private void Deactive()
    {
        if (controller is IShootControllable shootable)
        {
            shootable.OnShootDeactivate();
        }
    }

    /// 玩家踩上按钮时触发一次，门保持降下直到玩家离开延迟结束。
    private void OnTriggerEnter2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) == 0) return;

        // 取消待执行的离开释放
        if (leaveCoroutine != null)
        {
            StopCoroutine(leaveCoroutine);
            leaveCoroutine = null;
        }

        if (playersOnButton == 0)
        {
            if (controller is IShootControllable s)
                s.BeingHeld = true;
            Active();
        }

        playersOnButton++;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if ((playerLayer.value & (1 << other.gameObject.layer)) != 0)
        {
            playersOnButton--;
            if (playersOnButton <= 0)
            {
                playersOnButton = 0;
                // BeingHeld 保持 true，等延迟结束再释放，避免 holdTime 计时器提前触发
                leaveCoroutine = StartCoroutine(DelayedDeactive());
            }
        }
    }

    private IEnumerator DelayedDeactive()
    {
        yield return new WaitForSeconds(leaveDelay);

        if (controller is IShootControllable s)
            s.BeingHeld = false;

        Deactive();
        leaveCoroutine = null;
    }
}
