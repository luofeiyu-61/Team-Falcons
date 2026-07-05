using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Menu
{
    public class BackgroundAutoSwitcher : MonoBehaviour
    {
        [Header("自动切换")]
        [SerializeField] private float interval = 10f;
        [SerializeField] private float fadeDuration = 1.5f;
        [SerializeField] private string resourcePath = "Art/UI/Background";

        private Sprite[] sprites;
        private Image currentImage;
        private int currentIndex;
        private Coroutine switchRoutine;

        private void Start()
        {
            sprites = Resources.LoadAll<Sprite>(resourcePath);
            if (sprites == null || sprites.Length == 0)
            {
                Debug.LogWarning($"[{nameof(BackgroundAutoSwitcher)}] 未在 Resources/{resourcePath} 找到背景图。");
                return;
            }

            currentImage = GetComponentInChildren<Image>();
            if (currentImage == null)
            {
                Debug.LogWarning($"[{nameof(BackgroundAutoSwitcher)}] 未找到 Image 组件。");
                return;
            }

            // 匹配当前 sprite 的索引
            currentIndex = 0;
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null && currentImage.sprite != null
                    && sprites[i].name == currentImage.sprite.name)
                {
                    currentIndex = i;
                    break;
                }
            }

            switchRoutine = StartCoroutine(AutoSwitchLoop());
        }

        private IEnumerator AutoSwitchLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(interval);
                yield return CrossfadeToNext();
            }
        }

        private IEnumerator CrossfadeToNext()
        {
            int nextIndex = (currentIndex + 1) % sprites.Length;
            Sprite nextSprite = sprites[nextIndex];
            if (nextSprite == null)
                yield break;

            // 创建临时 Image 用于交叉淡化
            Transform parent = currentImage.transform.parent;
            GameObject tempObj = new GameObject("BackgroundCrossfade");
            tempObj.transform.SetParent(parent, false);

            Image tempImage = tempObj.AddComponent<Image>();
            RectTransform srcRect = currentImage.rectTransform;
            RectTransform tempRect = tempImage.rectTransform;

            tempRect.anchorMin = srcRect.anchorMin;
            tempRect.anchorMax = srcRect.anchorMax;
            tempRect.sizeDelta = srcRect.sizeDelta;
            tempRect.pivot = srcRect.pivot;
            tempRect.anchoredPosition = srcRect.anchoredPosition;
            tempRect.SetSiblingIndex(currentImage.transform.GetSiblingIndex() + 1);

            tempImage.sprite = nextSprite;
            tempImage.type = currentImage.type;
            tempImage.preserveAspect = currentImage.preserveAspect;
            tempImage.material = currentImage.material;
            tempImage.color = new Color(1, 1, 1, 0);

            yield return null;

            // 交叉淡化
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fadeDuration);
                tempImage.color = new Color(1, 1, 1, t);
                currentImage.color = new Color(1, 1, 1, 1f - t);
                yield return null;
            }

            // 完成后：将新 sprite 设到 currentImage，恢复颜色，销毁临时对象
            currentImage.sprite = nextSprite;
            currentImage.color = Color.white;
            currentIndex = nextIndex;

            Destroy(tempObj);
        }

        private void OnDestroy()
        {
            if (switchRoutine != null)
                StopCoroutine(switchRoutine);
        }
    }
}
