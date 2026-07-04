using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.InGame
{
    public class BulletSlots : MonoBehaviour
    {
        [SerializeField] private RectMask2D blueMask;
        [SerializeField] private TextMeshProUGUI blueText;
        [SerializeField] private RectMask2D yellowMask;
        [SerializeField] private TextMeshProUGUI yellowText;
        [SerializeField] private GameObject blueObject;
        [SerializeField] private GameObject yellowObject;

        public const int MaxCount = 5;

        private int blueCount;
        public int BlueCount
        {
            get => blueCount;
            set
            {
                blueCount = value;
                blueText.text = blueCount.ToString();
                blueMask.padding = new Vector4(0, 0, (MaxCount - blueCount) * blueMask.rectTransform.rect.width / MaxCount, 0);
                if (blueCount <= 0)
                {
                    blueCount = 0;
                    blueObject.SetActive(false);
                }
                else if (!blueObject.activeSelf)
                {
                    blueObject.SetActive(true);
                }
            }
        }

        private int yellowCount;
        public int YellowCount
        {
            get => yellowCount;
            set
            {
                yellowCount = value;
                yellowText.text = yellowCount.ToString();
                yellowMask.padding = new Vector4(0, 0, (MaxCount - yellowCount) * blueMask.rectTransform.rect.width / MaxCount, 0);
                if (yellowCount <= 0)
                {
                    yellowCount = 0;
                    yellowObject.SetActive(false);
                }
                else if (!yellowObject.activeSelf)
                {
                    yellowObject.SetActive(true);
                }
            }
        }

        private void Start()
        {
            BlueCount = 0;
            YellowCount = 0;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.I))
            {
                BlueCount++;
            }
            else if (Input.GetKeyDown(KeyCode.O))
            {
                BlueCount--;
            }
            else if (Input.GetKeyDown(KeyCode.J))
            {
                YellowCount++;
            }
            else if (Input.GetKeyDown(KeyCode.K))
            {
                YellowCount--;
            }
        }
    }
}