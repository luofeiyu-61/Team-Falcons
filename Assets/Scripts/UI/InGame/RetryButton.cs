using UnityEngine;

namespace UI.InGame
{
    public class RetryButton : MonoBehaviour
    {
        public void OnRetryButtonClicked()
        {
            LevelController.RestartCurrentLevel();
        }
    }
}