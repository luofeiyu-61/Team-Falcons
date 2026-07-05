using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        [SerializeField] private GameObject arrow;
        
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
                    BlueActive = false;
                }
                else if (!blueObject.activeSelf)
                {
                    gameObject.SetActive(true);
                    BlueActive = true;
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
                    RedActive = false;
                }
                else if (!redObject.activeSelf)
                {
                    gameObject.SetActive(true);
                    RedActive = true;
                }
            }
        }

        public bool BlueActive
        {
            get => blueObject.activeSelf;
            set
            {
                blueObject.SetActive(value);
                if (!value && !redObject.activeSelf)
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
                if (!value && !blueObject.activeSelf)
                {
                    gameObject.SetActive(false);
                }
            }
        }
        
        private void Awake()
        {
            blueRect  = blueMask.rectTransform;
            redRect = redMask.rectTransform;

            SceneManager.sceneLoaded += BindAnchorManager;

        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= BindAnchorManager;
        }

        private void Start()
        {
            
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

        public void HandleInputSelection(AnchorMode mode)
        {
            StopAllCoroutines();
            // 强制立即重建布局，避免首次激活时读到布局前的错误位置
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);

            float targetY = mode == AnchorMode.Attract
                ? blueObject.transform.localPosition.y
                : redObject.transform.localPosition.y;
            StartCoroutine(MoveArrowCoroutine(targetY, 500f));
        }

        private IEnumerator MoveArrowCoroutine(float yDest, float speed)
        {
            Vector3 startPos = arrow.transform.localPosition;
            Vector3 endPos = new Vector3(startPos.x, yDest, startPos.z);
            float distance = Mathf.Abs(yDest - startPos.y);
            if (Mathf.Approximately(distance, 0f))
            {
                yield break;
            }   
            
            float duration = distance / speed;
            float elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                float t = Mathf.Clamp01(elapsedTime / duration);
                arrow.transform.localPosition = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }

            arrow.transform.localPosition = endPos;
        }

        private void BindAnchorManager(Scene scene, LoadSceneMode mode)
        {
            var gm = GameObject.Find("GameManager");
            if (!gm)
            {
                return;
            }
            gm.GetComponent<AnchorManager>().BatterySlots = this;
        }
    }
}