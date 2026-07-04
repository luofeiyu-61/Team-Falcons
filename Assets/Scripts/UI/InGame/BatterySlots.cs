using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.InGame
{
    public class BatterySlots : MonoBehaviour
    {
        [SerializeField] private RectMask2D blueMask;
        // [SerializeField] private TextMeshProUGUI blueText;
        [SerializeField] private RectMask2D redMask;
        // [SerializeField] private TextMeshProUGUI redText;
        private RectTransform blueRect;
        private RectTransform redRect;
        [SerializeField] private GameObject blueObject;
        [SerializeField] private GameObject redObject;
        
        [SerializeField] private float batteryWidth;

        public int MaxCount = 4;

        private int blueCount;
        public int BlueCount
        {
            get => blueCount;
            set
            {
                blueCount = value;
                // blueText.text = blueCount.ToString();
                blueMask.padding = new Vector4(0, 0, (MaxCount - blueCount) * batteryWidth / MaxCount, 0);
                if (blueCount <= 0)
                {
                    blueCount = 0;
                    blueObject.SetActive(false);
                }
                else if (!blueObject.activeSelf)
                {
                    gameObject.SetActive(true);
                    blueObject.SetActive(true);
                }
            }
        }

        private int redCount;
        public int RedCount
        {
            get => redCount;
            set
            {
                redCount = value;
                // redText.text = redCount.ToString();
                redMask.padding = new Vector4(0, 0, (MaxCount - redCount) * batteryWidth / MaxCount, 0);
                if (redCount <= 0)
                {
                    redCount = 0;
                    redObject.SetActive(false);
                }
                else if (!redObject.activeSelf)
                {
                    gameObject.SetActive(true);
                    redObject.SetActive(true);
                }
            }
        }

        public bool BlueActive
        {
            get => blueObject.activeSelf;
            set
            {
                blueObject.SetActive(value);
                if (!redObject.activeSelf)
                {
                    gameObject.SetActive(false);
                }
            }
        }

        public bool RedActive
        {
            get => redObject.activeSelf;
            set
            {
                redObject.SetActive(value);
                if (!blueObject.activeSelf)
                {
                    gameObject.SetActive(false);
                }
            }
        }
        
        private void Awake()
        {
            blueRect  = blueMask.rectTransform;
            redRect = redMask.rectTransform;
        }
        
        private void Start()
        {
            BlueCount = 0;
            RedCount = 0;
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

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
                RedCount++;
            }
            else if (Input.GetKeyDown(KeyCode.K))
            {
                RedCount--;
            }
        }
    }
}