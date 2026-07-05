using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace UI.InGame
{
    public class MenuButton : MonoBehaviour
    {
        public void OnMenuButtonClicked()
        {
            Deselect();
            SceneManager.LoadScene("Menu");
        }

        private void Deselect()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}