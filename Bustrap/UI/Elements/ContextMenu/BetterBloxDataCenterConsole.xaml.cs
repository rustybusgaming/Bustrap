using Bustrap.Integrations;
using Bustrap.UI.Elements.Base;
using Bustrap.UI.ViewModels.ContextMenu;

namespace Bustrap.UI.Elements.ContextMenu
{
    public partial class BetterBloxDataCenterConsole
    {
        public BetterBloxDataCenterConsole()
        {
            InitializeComponent();
            var vm = new BetterBloxDataCenterConsoleViewModel();
            DataContext = vm;
        }
    }
}
