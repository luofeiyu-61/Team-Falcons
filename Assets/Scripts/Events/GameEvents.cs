using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 玩家死亡
public struct PlayerDiedEvent : IGameEvent
{
    public Vector2 DeathPosition;

    public PlayerDiedEvent(Vector2 deathPosition)
    {
        DeathPosition = deathPosition;
    }
}

// 锚点数量变化
public struct AnchorCountChangedEvent : IGameEvent
{
    public int CurrentCount;
    public int MaxCount;

    public int RemainingCount => MaxCount - CurrentCount;

    public AnchorCountChangedEvent(int currentCount, int maxCount)
    {
        CurrentCount = currentCount;
        MaxCount = maxCount;
    }
}

// 放置锚点
public struct AnchorPlacedEvent : IGameEvent
{
    public Vector2 Position;

    public AnchorPlacedEvent(Vector2 position)
    {
        Position = position;
    }
}

// 移除锚点
public struct AnchorRemovedEvent : IGameEvent
{
    public Vector2 Position;

    public AnchorRemovedEvent(Vector2 position)
    {
        Position = position;
    }
}

// 游戏暂停/继续
public struct PauseChangedEvent : IGameEvent
{
    public bool IsPaused;

    public PauseChangedEvent(bool isPaused)
    {
        IsPaused = isPaused;
    }
}

// 玩家通关
public struct LevelCompletedEvent : IGameEvent
{
    public int LevelIndex;

    public LevelCompletedEvent(int levelIndex)
    {
        LevelIndex = levelIndex;
    }
}

// 机关状态变化，例如门、按钮、激光
public struct PuzzleStateChangedEvent : IGameEvent
{
    public string ChannelId;
    public bool IsActive;

    public PuzzleStateChangedEvent(string channelId, bool isActive)
    {
        ChannelId = channelId;
        IsActive = isActive;
    }
}

// 玩家重生完成
public struct PlayerRespawnedEvent : IGameEvent
{
    public Vector2 RespawnPosition;

    public PlayerRespawnedEvent(Vector2 respawnPosition)
    {
        RespawnPosition = respawnPosition;
    }
}

// 到达检查点
public struct CheckpointReachedEvent : IGameEvent
{
    public string CheckpointId;
    public Vector2 RespawnPosition;

    public CheckpointReachedEvent(
        string checkpointId,
        Vector2 respawnPosition
    )
    {
        CheckpointId = checkpointId;
        RespawnPosition = respawnPosition;
    }
}

// 到达出口
public struct ExitReachedEvent : IGameEvent
{
    public string ExitId;

    public ExitReachedEvent(string exitId)
    {
        ExitId = exitId;
    }
}

// 锚点模式切换（道具拾取触发）
public struct AnchorModeChangedEvent : IGameEvent
{
    public AnchorMode Mode;

    public AnchorModeChangedEvent(AnchorMode mode)
    {
        Mode = mode;
    }
}