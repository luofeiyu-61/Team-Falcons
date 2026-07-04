using UnityEngine;
using UnityEngine.SceneManagement;

namespace UI.InGame
{
    public class MenuButton : MonoBehaviour
    {
        public void OnMenuButtonClicked()
        {
            SceneManager.LoadScene("Menu");
        }
    }
}