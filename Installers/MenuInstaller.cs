using DynamicJD.Services;
using DynamicJD.UI.Clone;
using DynamicJD.UI.Menu;
using Zenject;

namespace DynamicJD.Installers
{
    internal sealed class MenuInstaller : Installer
    {
        public override void InstallBindings()
        {
            Container.BindInterfacesAndSelfTo<JdUIManager>().AsSingle();
            Container.BindInterfacesAndSelfTo<JdOverrideMenuPanelController>()
                .FromNewComponentOnNewGameObject().AsSingle().NonLazy();
            Container.Bind<CloneFlowCoordinator>().FromNewComponentOnNewGameObject().AsSingle();
            Container.Bind<CloneViewController>().FromNewComponentAsViewController().AsSingle();
            Container.BindInterfacesAndSelfTo<MainMenuTab>().AsSingle();
        }
    }
}
