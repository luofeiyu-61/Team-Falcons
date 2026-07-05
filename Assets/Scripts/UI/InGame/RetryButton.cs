using UnityEngine;
using UnityEngine.EventSystems;

namespace UI.InGame
{
    public class RetryButton : MonoBehaviour
    {
        public void OnRetryButtonClicked()
        {
            Deselect();
            LevelController.RestartCurrentLevel();
        }

        private void Deselect()
        {
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}