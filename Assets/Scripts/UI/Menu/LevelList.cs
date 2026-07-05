using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Menu
{
    public class LevelList : MonoBehaviour
    {
        [SerializeField] private BackgroundController backgroundController;
        [SerializeField] private GameObject levelButtonPrefab;
        [SerializeField] private List<float> levelLayout;
        public int levelCount; // start from 0
        public RectTransform center;

        [Header("入场动画")]
        [SerializeField] private float appearDelay = 0.1f;
        [SerializeField] private float appearDuration = 0.6f;
        [SerializeField] private float floatHeight = 60f;

        [Header("退场动画")]
        [SerializeField] private float disappearDelay = 0.08f;
        [SerializeField] private float disappearDuration = 0.4f;

        private readonly List<Button> buttons = new();
        private readonly Dictionary<int, float> levelIndexToRadius = new();
        private readonly Dictionary<int, (int indexInGroup, int groupSize)> levelIndexToGroup = new();
        private readonly List<RectTransform> buttonRects = new();
        private readonly List<Vector3> buttonTargetPositions = new();
        private bool initialized;
        private Coroutine animRoutine;

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        private static float EaseInBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return c3 * t * t * t - c1 * t * t;
        }

        private void Awake()
        {
            if (levelLayout.Count != levelCount)
            {
                Debug.LogError("Level layout element count does not match set level count.");
                return;
            }

            for (int i = 0; i < levelCount; i++)
            {
                levelIndexToRadius.Add(i, levelLayout[i]);
            }
            Dictionary<float, List<int>> groups = new();
            for (int i = 0; i < levelCount; i++)
            {
                var r = levelIndexToRadius[i];
                if (!groups.Keys.Any(x => Mathf.Approximately(x, r)))
                {
                    groups.Add(r, new List<int>());
                }
                groups.First(kvp => Mathf.Approximately(kvp.Key, r)).Value.Add(i);
            }
            foreach (var group in groups)
            {
                group.Value.Sort();
            }
            foreach (var group in groups)
            {
                for (var i = 0; i < group.Value.Count; i++)
                {
                    int levelIndex = group.Value[i];
                    levelIndexToGroup.Add(levelIndex, (i, group.Value.Count));
                }
            }
            
            buttons.Clear();
            buttonRects.Clear();
            buttonTargetPositions.Clear();
            // Kill all children created in Scene window
            for (int i = 0; i < transform.childCount; i++)
            {
                Destroy(transform.GetChild(i).gameObject);
            }
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

            if (!initialized)
            {
                CreateButtons();
                initialized = true;
            }

            if (animRoutine != null)
                StopCoroutine(animRoutine);
            animRoutine = StartCoroutine(PlayEntranceAnimation());
        }

        private void CreateButtons()
        {
            for (int i = 0; i < levelCount; i++)
            {
                (int indexInGroup, int groupSize) groupInfo = levelIndexToGroup[i];
                float step = groupInfo.groupSize == 1 ? 0 :
                    Mathf.PI * 4 / 3 / (groupInfo.groupSize - 1);
                float radius = levelIndexToRadius[i];
                float angle = step == 0 ? Mathf.PI * 3 / 2 :
                    Mathf.PI * 5 / 6 + step * groupInfo.indexInGroup;
                Vector3 localPos = new Vector3(
                    center.localPosition.x + radius * Mathf.Cos(angle),
                    center.localPosition.y + radius * Mathf.Sin(angle),
                    center.localPosition.z
                );
                var levelButton = Instantiate(levelButtonPrefab, transform, false);
                levelButton.transform.localPosition = localPos;
                levelButton.GetComponent<LevelButton>().Setup(i, backgroundController);
                buttons.Add(levelButton.GetComponent<Button>());
                buttonRects.Add(levelButton.GetComponent<RectTransform>());
                buttonTargetPositions.Add(localPos);
            }
        }

        private IEnumerator PlayEntranceAnimation()
        {
            SetButtonsInteractable(false);

            // 每个按钮独立动画，通过 appearDelay 实现依次出现
            var routines = new List<Coroutine>();
            for (int i = 0; i < buttonRects.Count; i++)
            {
                buttonRects[i].localScale = Vector3.zero;
                routines.Add(StartCoroutine(AnimateButton(buttonRects[i], buttonTargetPositions[i], i)));
            }

            foreach (var routine in routines)
                yield return routine;

            SetButtonsInteractable(true);
            animRoutine = null;
        }

        private IEnumerator AnimateButton(RectTransform button, Vector3 targetPos, int index)
        {
            // 依次出现：每个按钮比前一个延迟 appearDelay
            yield return new WaitForSeconds(index * appearDelay);

            float elapsed = 0f;
            while (elapsed < appearDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / appearDuration);

                // 缩放：EaseOutBack 带过冲效果
                button.localScale = Vector3.one * EaseOutBack(t);

                // Y 轴：从下方出现，上浮过冲后回落固定
                float yOffset = Mathf.Sin(t * Mathf.PI) * floatHeight - (1f - t) * floatHeight;
                button.localPosition = targetPos + Vector3.up * yOffset;

                yield return null;
            }

            button.localScale = Vector3.one;
            button.localPosition = targetPos;
        }

        private void HideLevelList()
        {
            if (animRoutine != null)
                StopCoroutine(animRoutine);
            animRoutine = StartCoroutine(PlayExitAnimation());
        }

        private IEnumerator PlayExitAnimation()
        {
            SetButtonsInteractable(false);

            // 从后往前依次消失
            var routines = new List<Coroutine>();
            for (int i = buttonRects.Count - 1; i >= 0; i--)
            {
                routines.Add(StartCoroutine(AnimateButtonExit(buttonRects[i], buttonTargetPositions[i], buttonRects.Count - 1 - i)));
            }

            foreach (var routine in routines)
                yield return routine;

            gameObject.SetActive(false);
            animRoutine = null;
        }

        private IEnumerator AnimateButtonExit(RectTransform button, Vector3 targetPos, int reverseIndex)
        {
            yield return new WaitForSeconds(reverseIndex * disappearDelay);

            float elapsed = 0f;
            while (elapsed < disappearDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / disappearDuration);

                // 缩放：从1缩小到0，EaseInBack 带下冲效果
                button.localScale = Vector3.one * EaseInBack(1f - t);

                // Y 轴：向下沉后消失
                float yOffset = Mathf.Sin(t * Mathf.PI) * floatHeight * 0.5f + t * floatHeight * 0.5f;
                button.localPosition = targetPos - Vector3.up * yOffset;

                yield return null;
            }

            button.localScale = Vector3.zero;
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
