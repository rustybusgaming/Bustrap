using System.Windows;
using Bustrap.UI.ViewModels;
using Bustrap.UI.ViewModels.ContextMenu;

namespace Bustrap.UI.Elements.ContextMenu
{
    public partial class RPCWindow
    {
        public RPCWindow()
        {
            InitializeComponent();
            DataContext = new RPCCustomizerViewModel();
        }
    }
}
