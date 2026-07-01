using System.Windows;
using Bustrap.UI.ViewModels;

namespace Bustrap.UI.Elements.ContextMenu
{
    public partial class AccountManagerWindow
    {
        public AccountManagerWindow()
        {
            InitializeComponent();
            DataContext = new AccountBackupsViewModel();
        }
    }
}
