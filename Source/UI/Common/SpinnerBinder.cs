using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Animations;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicJD.UI.Common
{
    internal static class SpinnerBinder
    {
        public static void Bind(Image image)
        {
            if (image == null) return;
            if (TryBind(image)) return;
            image.gameObject.AddComponent<SpinnerWait>();
        }

        internal static bool TryBind(Image image)
        {
            if (image == null) return false;
            try
            {
                AnimationController controller = BeatSaberUI.DiContainer.Resolve<AnimationController>();
                AnimationControllerData animation = controller?.LoadingAnimation;
                if (animation?.Sprites == null || animation.Sprites.Length == 0) return false;
                image.sprite = animation.Sprites[animation.UvIndex];
                if (!animation.ActiveImages.Contains(image)) animation.ActiveImages.Add(image);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.Debug("Loading spinner not ready: " + ex.Message);
                return false;
            }
        }

    }

    internal sealed class SpinnerWait : MonoBehaviour
    {
        private Image _image;

        private void Awake()
        {
            _image = GetComponent<Image>();
        }

        private void Update()
        {
            if (SpinnerBinder.TryBind(_image))
            {
                Destroy(this);
            }
        }
    }
}
