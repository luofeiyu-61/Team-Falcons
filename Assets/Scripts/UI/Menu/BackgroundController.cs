using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UI.Menu
{
    public class BackgroundController : MonoBehaviour
    {
        [SerializeField] private List<Image> backgrounds = new();
        [SerializeField] private MosaicBlurTransitionEffect transitionEffect;
        [SerializeField] private LevelList levelList;

        private bool blur = false;
        
        private void Awake()
        {
            
        }

        private void Start()
        {
            
        }

        public void ToggleBlurBackground()
        {
            if (blur)
            {
                BlurBackground(true);
                blur = false;
            }
            else
            {
                BlurBackground(false);
                blur = true;
            }
        }
        
        public void BlurBackground(bool reverse = false)
        {
            if (reverse)
                transitionEffect.PlayBlurReverse();
            else
                transitionEffect.PlayBlurForward();
        }

        public void MosaicBackground(bool reverse = false, UnityAction<(bool blur, bool reverse)> callback = null)
        {
            if (reverse)
                transitionEffect.PlayMosaicReverse(callback);
            else
                transitionEffect.PlayMosaicForward(callback);
        }

        public void SelectLevel(int level)
        {
            levelList.SetButtonsInteractable(false);
            MosaicBackground(false, animationInfo =>
            {
                if (!animationInfo.blur && !animationInfo.reverse)
                {
                    StartCoroutine(ChangeSceneMaskingAnimation(level));
                }
            });
            AudioManager.Instance.StopMusic(2f);
        }

        private IEnumerator ChangeSceneMaskingAnimation(int level)
        {
            var maskEffect = Camera.main!.GetComponent<CircleMaskCameraEffect>();
            maskEffect.PlayForward();
            yield return new WaitForSeconds(maskEffect.duration);
            LevelController.LoadLevel(level);
        }
    }
}