using UnityEngine;

namespace UI.Menu
{
    public class LevelList : MonoBehaviour
    {
        [SerializeField] private BackgroundController backgroundController;
        [SerializeField] private GameObject levelButtonPrefab;
        public int levelCount;
        public RectTransform center;
        public float radius;
        
        private bool initialized;
        
        private void Awake()
        {
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
        
        public void ShowLevelList()
        {
            gameObject.SetActive(true);
            if (initialized)
                return;
            
            // Semi-circle distribution of buttons
            float step = Mathf.PI / (levelCount - 1);
            for (int i = 0; i < levelCount; i++)
            {
                float angle = Mathf.PI + step * i;
                Vector3 position = new Vector3(
                    center.position.x + radius * Mathf.Cos(angle),
                    center.position.y + radius * Mathf.Sin(angle),
                    center.position.z
                );
                var levelButton = Instantiate(levelButtonPrefab, position, Quaternion.identity);
                levelButton.GetComponent<LevelButton>().Setup(i + 1, backgroundController);
                levelButton.transform.SetParent(transform, true);
            }
            
            if (!initialized)
                initialized = true;
        }

        public void HideLevelList()
        {
            gameObject.SetActive(false);
        }
    }
}