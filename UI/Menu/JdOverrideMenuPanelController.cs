using System;
using System.ComponentModel;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.FloatingScreen;
using DynamicJD.Configuration;
using HMUI;
using UnityEngine;
using Zenject;

namespace DynamicJD.UI.Menu
{
    /// <summary>
    /// Small levitating "JD Override" control shown below the song-selection UI, near the ground.
    /// Kept out of the DynamicJD settings tab so toggling it cannot reflow that scroll view.
    /// </summary>
    internal sealed class JdOverrideMenuPanelController : MonoBehaviour, INotifyPropertyChanged
    {
        private const string Resource = "DynamicJD.UI.Menu.jdOverridePanel.bsml";
        private FloatingScreen _screen;
        private LevelSelectionNavigationController _anchor;
        private float _nextAnchorScan;

        private MainFlowCoordinator _mainFlow;

        [Inject]
        private void Construct(MainFlowCoordinator mainFlow)
        {
            _mainFlow = mainFlow;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Start()
        {
            CreateScreen();
        }

        private void Update()
        {
            if (_screen == null) return;

            bool inSongSelection = _mainFlow != null &&
                                   _mainFlow.YoungestChildFlowCoordinatorOrSelf() is LevelSelectionFlowCoordinator;
            if (!inSongSelection)
            {
                _anchor = null;
                SetVisible(false);
                return;
            }

            if (_anchor == null || !_anchor.isActiveAndEnabled)
            {
                _anchor = null;
                if (Time.unscaledTime < _nextAnchorScan)
                {
                    SetVisible(false);
                    return;
                }

                _nextAnchorScan = Time.unscaledTime + 0.1f;
                _anchor = FindObjectOfType<LevelSelectionNavigationController>();
            }

            bool show = _anchor != null && _anchor.isActiveAndEnabled;
            SetVisible(show);
            if (show) PositionScreen();
        }

        private void SetVisible(bool visible)
        {
            if (_screen != null && _screen.gameObject.activeSelf != visible)
            {
                _screen.gameObject.SetActive(visible);
            }
        }

        private void CreateScreen()
        {
            if (_screen != null) return;

            _screen = FloatingScreen.CreateFloatingScreen(new Vector2(80f, 9f), false,
                new Vector3(0f, 0.5f, 1.2f), Quaternion.identity, 0f, false);
            _screen.ShowHandle = false;
            _screen.GetComponent<Canvas>().sortingOrder = 10;
            _screen.gameObject.SetActive(false);
            BSMLParser.Instance.Parse(Utilities.GetResourceContent(typeof(JdOverrideMenuPanelController).Assembly, Resource),
                _screen.gameObject, this);
        }

        private void PositionScreen()
        {
            RectTransform rect = _anchor.transform as RectTransform;
            if (rect == null) return;

            float halfHeight = rect.rect.height * Mathf.Abs(rect.lossyScale.y) * 0.5f;
            Vector3 position = rect.position - rect.up * (halfHeight + 0.32f);
            position.y = Mathf.Max(0.12f, position.y);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 toPlayer = cam.transform.position - position;
                if (toPlayer.sqrMagnitude > 0.001f)
                {
                    position += toPlayer.normalized * 0.45f;

                    // A small extra nudge along the horizontal plane, without changing its height.
                    Vector3 horizontal = cam.transform.position - position;
                    horizontal.y = 0f;
                    if (horizontal.sqrMagnitude > 0.001f)
                    {
                        position += horizontal.normalized * 0.12f;
                    }
                }
            }

            _screen.transform.SetPositionAndRotation(position,
                rect.rotation * Quaternion.Euler(12f, 0f, 0f));
        }

        private void OnDestroy()
        {
            if (_screen != null)
            {
                Destroy(_screen.gameObject);
                _screen = null;
            }
        }

        [UIValue("manual_override")]
        public bool ManualOverride
        {
            get => PluginConfig.Instance.manual_override;
            set
            {
                PluginConfig.Instance.manual_override = value;
                PluginConfig.Instance.Changed();
                Plugin.NotifyManualOverrideChanged();
                RaiseAll();
            }
        }

        [UIAction("set_manual_override")]
        public void SetManualOverride(bool value) => ManualOverride = value;

        [UIValue("manual_active")]
        public bool ManualActive => PluginConfig.Instance.manual_override;

        [UIValue("manual_jd")]
        public float ManualJd
        {
            get => PluginConfig.Instance.manual_jd;
            set
            {
                PluginConfig.Instance.manual_jd = Mathf.Clamp(value, 4f, 32f);
                PluginConfig.Instance.Changed();
                Raise(nameof(ManualJd));
            }
        }

        [UIAction("set_manual_jd")]
        public void SetManualJd(float value) => ManualJd = value;

        [UIAction("manual_jd_formatter")]
        public string ManualJdFormatter(float value) => Core.JumpDistanceMath.FormatJd(value);

        private void RaiseAll()
        {
            Raise(nameof(ManualOverride), nameof(ManualActive), nameof(ManualJd));
        }

        private void Raise(params string[] names)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler == null) return;
            foreach (string name in names)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}
