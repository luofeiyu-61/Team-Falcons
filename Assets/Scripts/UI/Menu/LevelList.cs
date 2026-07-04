using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Menu
{
    public class LevelList : MonoBehaviour
    {
        [SerializeField] private BackgroundController backgroundController;
        [SerializeField] private GameObject levelButtonPrefab;
        public int levelCount;
        public RectTransform center;
        public float radius;
        
        private List<Button> buttons;
        private bool initialized;
        
        private void Awake()
        {
            buttons = new List<Button>();
            // Kill all children created in Scene window
            for (int i = 0; i < transform.childCount; i++)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
        }

        private void Start()
        {
            
        }

        public void ToggleLevelList()
        {
            if (gameObject.activeInHierarchy && initialized)
            {
                HideLevelList();
            }
            else
            {
                ShowLevelList();
            }
        }

        private void ShowLevelList()
        {
            gameObject.SetActive(true);
            if (initialized)
                return;
            
            // Semi-circle distribution of buttons（使用局部坐标，兼容 Screen Space - Camera 模式）
            float step = Mathf.PI / (levelCount - 1);
            for (int i = 0; i < levelCount; i++)
            {
                float angle = Mathf.PI + step * i;
                Vector3 localPos = new Vector3(
                    center.localPosition.x + radius * Mathf.Cos(angle),
                    center.localPosition.y + radius * Mathf.Sin(angle),
                    center.localPosition.z
                );
                var levelButton = Instantiate(levelButtonPrefab, transform, false);
                levelButton.transform.localPosition = localPos;
                levelButton.GetComponent<LevelButton>().Setup(i + 1, backgroundController);
                buttons.Add(levelButton.GetComponent<Button>());
            }
            
            if (!initialized)
                initialized = true;
        }

        private void HideLevelList()
        {
            gameObject.SetActive(false);
        }
        
        public void SetButtonsInteractable(bool interactable)
        {
            foreach (var button in buttons)
            {
                button.interactable = interactable;
            }
        }
    }
}