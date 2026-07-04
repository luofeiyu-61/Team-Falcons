using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneObjectManager : MonoBehaviour
{
    private SceneType currentSceneType = SceneType.Menu;
    private string previousSceneName = "";
    public static SceneObjectManager Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            SceneManager.sceneLoaded += HandleSceneLoad;
        }
        else
            Destroy(this);
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