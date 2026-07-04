using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum LevelState
{
    Playing,
    Respawning,
    Completed
}

public class LevelManager : MonoBehaviour
{
    [Header("关卡信息")]
    [SerializeField] private int levelIndex = 1;

    [Header("玩家")]
    [SerializeField] private PlayerRespawn playerRespawn;

    [Header("出生点")]
    [SerializeField] private Transform initialSpawnPoint;

    [Header("重生设置")]
    [SerializeField] private float respawnDelay = 1f;

    private const int PlayerLayer = 3;

    public LevelState CurrentState { get; private set; }

    private Vector2 initialPlayerPosition;
    private Vector2 currentRespawnPosition;
    private float levelStartTime;

    private void Awake()
    {
        // 通过 Player 层级查找场景中的主角
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        GameObject playerObject = null;
        int playerCount = 0;

        foreach (GameObject obj in allObjects)
        {
            if (obj.layer == PlayerLayer)
            {
                playerCount++;
                if (playerCount == 1)
                {
                    playerObject = obj;
                }
            }
        }

        if (playerCount > 1)
        {
            Debug.LogError($"场景内存在 {playerCount} 个主角，只允许有一个！");
            enabled = false;
            return;
        }

        if (playerObject == null)
        {
            Debug.LogError("场景内未找到 Player 层级的主角对象。");
            enabled = false;
            return;
        }

        // 记录初始位置并放到出生点上
        initialPlayerPosition = playerObject.transform.position;

        if (initialSpawnPoint != null)
        {
            currentRespawnPosition = initialSpawnPoint.position;
        }
        else
        {
            currentRespawnPosition = initialPlayerPosition;
        }

        playerObject.transform.position = currentRespawnPosition;

        // 绑定 PlayerRespawn
        if (playerRespawn == null)
        {
            playerRespawn = playerObject.GetComponent<PlayerRespawn>();
        }

        if (playerRespawn == null)
        {
            Debug.LogError("主角对象上未找到 PlayerRespawn 组件。");
            enabled = false;
            return;
        }

        CurrentState = LevelState.Playing;
    }

    private void Start()
    {
        levelStartTime = Time.unscaledTime;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartCurrentLevel();
        }
    }

    private void OnEnable()
    {
        GameEventBus.Subscribe<PlayerDiedEvent>(HandlePlayerDied);
        GameEventBus.Subscribe<CheckpointReachedEvent>(HandleCheckpointReached);
        GameEventBus.Subscribe<ExitReachedEvent>(HandleExitReached);
    }

    private void OnDisable()
    {
        GameEventBus.Unsubscribe<PlayerDiedEvent>(HandlePlayerDied);
        GameEventBus.Unsubscribe<CheckpointReachedEvent>(HandleCheckpointReached);
        GameEventBus.Unsubscribe<ExitReachedEvent>(HandleExitReached);
    }

    // 收到玩家死亡事件
    private void HandlePlayerDied(PlayerDiedEvent gameEvent)
    {
        if (CurrentState != LevelState.Playing)
        {
            return;
        }

        StartCoroutine(RespawnRoutine());
    }

    // 收到检查点事件
    private void HandleCheckpointReached(CheckpointReachedEvent gameEvent)
    {
        if (CurrentState != LevelState.Playing)
        {
            return;
        }

        currentRespawnPosition = gameEvent.RespawnPosition;

        Debug.Log($"已更新复活点：{gameEvent.CheckpointId}");
    }

    // 收到出口事件
    private void HandleExitReached(ExitReachedEvent gameEvent)
    {
        if (CurrentState != LevelState.Playing)
        {
            return;
        }

        CompleteLevel();
    }

    private IEnumerator RespawnRoutine()
    {
        CurrentState = LevelState.Respawning;

        playerRespawn.EnterDeadState();

        yield return new WaitForSecondsRealtime(respawnDelay);

        // 重载场景：所有状态归零，Awake 中用 savedCheckpoint 复位玩家
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);
    }

    public void CompleteLevel()
    {
        if (CurrentState != LevelState.Playing)
        {
            return;
        }

        CurrentState = LevelState.Completed;

        playerRespawn.FreezePlayer();

        GameEventBus.Publish(
            new LevelCompletedEvent(levelIndex)
        );

        Debug.Log($"第 {levelIndex} 关通关");

        LevelController.LoadNextLevel();
    }

    public void RestartCurrentLevel()
    {
        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();

        SceneManager.LoadScene(currentScene.buildIndex);
    }
}