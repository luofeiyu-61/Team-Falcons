using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

public static class GameEventBus
{
    private static readonly Dictionary<Type, Delegate> eventTable =
        new Dictionary<Type, Delegate>();

    // 防止切场景或关闭 Domain Reload 后，旧订阅残留
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        eventTable.Clear();
    }

    // 订阅事件
    public static void Subscribe<T>(Action<T> listener)
        where T : IGameEvent
    {
        if (listener == null)
        {
            return;
        }

        Type eventType = typeof(T);

        if (eventTable.TryGetValue(eventType, out Delegate oldListeners))
        {
            eventTable[eventType] = Delegate.Combine(oldListeners, listener);
        }
        else
        {
            eventTable.Add(eventType, listener);
        }
    }

    // 取消订阅事件
    public static void Unsubscribe<T>(Action<T> listener)
        where T : IGameEvent
    {
        if (listener == null)
        {
            return;
        }

        Type eventType = typeof(T);

        if (!eventTable.TryGetValue(eventType, out Delegate oldListeners))
        {
            return;
        }

        Delegate newListeners = Delegate.Remove(oldListeners, listener);

        if (newListeners == null)
        {
            eventTable.Remove(eventType);
        }
        else
        {
            eventTable[eventType] = newListeners;
        }
    }

    // 发布事件
    public static void Publish<T>(T gameEvent)
        where T : IGameEvent
    {
        Type eventType = typeof(T);

        if (!eventTable.TryGetValue(eventType, out Delegate listeners))
        {
            return;
        }

        Delegate[] invocationList = listeners.GetInvocationList();

        foreach (Delegate listener in invocationList)
        {
            try
            {
                ((Action<T>)listener).Invoke(gameEvent);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}