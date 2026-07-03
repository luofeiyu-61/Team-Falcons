using UnityEngine;
using UnityEngine.SceneManagement;

public static class LevelController
{
    // 加载指定 Build Index 的关卡
    public static void LoadLevel(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.LogError($"关卡索引 {buildIndex} 不存在，请检查 Build Settings。");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(buildIndex);
    }

    // 按场景名称加载关卡
    public static void LoadLevel(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("场景名称不能为空。");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    // 重开当前关卡
    public static void RestartCurrentLevel()
    {
        Time.timeScale = 1f;

        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentIndex);
    }

    // 进入下一关
    public static void LoadNextLevel()
    {
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        int nextIndex = currentIndex + 1;

        if (nextIndex >= SceneManager.sceneCountInBuildSettings)
        {
            Debug.Log("已经是最后一关。");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(nextIndex);
    }

    // 返回上一关
    public static void LoadPreviousLevel()
    {
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        int previousIndex = currentIndex - 1;

        if (previousIndex < 0)
        {
            Debug.Log("当前已经是第一关。");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(previousIndex);
    }

    // 返回主菜单，默认主菜单放在 Build Index 0
    public static void LoadMainMenu()
    {
        LoadLevel(0);
    }
}