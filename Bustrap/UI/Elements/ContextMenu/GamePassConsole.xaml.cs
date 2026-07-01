using Bustrap.Integrations;
using Bustrap.UI.Elements.Base;
using Bustrap.UI.ViewModels.ContextMenu;

namespace Bustrap.UI.Elements.ContextMenu
{
    public partial class GamePassConsole
    {
        public GamePassConsole(long userId)
        {
            InitializeComponent();
            var vm = new GamePassConsoleViewModel();
            DataContext = vm;
            vm.LoadGamePassesCommand.Execute(userId);
        }
    }
}
