using TMPro;
using UnityEngine;

namespace UI.Menu
{
    public class LevelButton : MonoBehaviour
    {
        private BackgroundController backgroundController;
        public int level;
        public TextMeshProUGUI levelText;
        
        private void Start()
        {
            levelText.text = level.ToString();
        }

        public void Setup(int lvl, BackgroundController bgController)
        {
            level = lvl;
            backgroundController = bgController;
        }

        public void OnLevelButtonClicked()
        {
            backgroundController.SelectLevel(level);
        }
    }
}