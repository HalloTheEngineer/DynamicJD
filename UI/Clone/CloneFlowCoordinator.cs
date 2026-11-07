using DynamicJD.Configuration;
using HMUI;
using Zenject;

namespace DynamicJD.UI.Clone
{
    internal sealed class CloneFlowCoordinator : FlowCoordinator
    {
        internal FlowCoordinator ParentFlow;

        private CloneViewController _view;

        [Inject]
        private void Construct(CloneViewController view)
        {
            _view = view;
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            showBackButton = true;
            SetTitle("DynamicJD · Copy a player's setup");
            ProvideInitialViewControllers(_view);
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            PluginConfig.Instance?.Changed();
            ParentFlow?.DismissFlowCoordinator(this);
        }
    }
}
