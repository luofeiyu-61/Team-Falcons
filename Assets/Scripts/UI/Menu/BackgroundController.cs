using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Menu
{
    public class BackgroundController : MonoBehaviour
    {
        [SerializeField] private List<Image> backgrounds = new();
        [SerializeField] private List<MosaicBlurTransitionEffect> transitionEffects = new();

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
            foreach (var effect in transitionEffects)
            {
                if (reverse)
                    effect.PlayBlurReverse();
                else
                    effect.PlayBlurForward();
            }
        }

        public void MosaicBackground(bool reverse = false)
        {
            foreach (var effect in transitionEffects)
            {
                if (reverse)
                    effect.PlayMosaicReverse();
                else
                    effect.PlayMosaicForward();
            }
        }
    }
}