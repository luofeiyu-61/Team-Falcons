using UnityEngine;
using UnityEngine.SceneManagement;

public class EntryController : MonoBehaviour
{
    private void Awake()
    {
        SceneManager.LoadScene("Menu", LoadSceneMode.Single);
    }
}