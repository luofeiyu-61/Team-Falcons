using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneObjectManager : MonoSingleton<SceneObjectManager>
{
    public SceneType currentSceneType = SceneType.Menu;
    public string previousSceneName = "";

    protected override void SafeAwake()
    {
        SceneManager.sceneLoaded += HandleSceneLoad;
    }

    private void HandleSceneLoad(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive)
            return;
        
        if (scene.name.Contains("Menu"))
        {
            foreach (var go in gameObject.scene.GetRootGameObjects()) // 所有DontDestroyOnLoad
            {
                if (go.CompareTag("InGame"))
                {
                    Destroy(go);
                }
            }
        }
        else if (previousSceneName.Contains("Menu"))
        {
            foreach (var go in gameObject.scene.GetRootGameObjects())
            {
                if (go.CompareTag("Menu"))
                {
                    Destroy(go);
                }
            }
            SceneManager.LoadScene("GameInitializeScene", LoadSceneMode.Additive);
        }
        currentSceneType = scene.name.Contains("Menu") ? SceneType.Menu : SceneType.InGame;
        previousSceneName = scene.name;
    }
}

public enum SceneType
{
    Menu,
    InGame,
    DontDestroyOnLoad // 永远不可能在Event被调用，只作为标记
}