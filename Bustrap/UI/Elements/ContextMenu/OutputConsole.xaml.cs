using Bustrap.Integrations;
using Bustrap.UI.ViewModels.ContextMenu;

namespace Bustrap.UI.Elements.ContextMenu
{
    public partial class OutputConsole
    {
        public OutputConsole(ActivityWatcher watcher)
        {
            var viewModel = new OutputConsoleViewModel(watcher);

            viewModel.RequestCloseEvent += (_, _) => Close();

            DataContext = viewModel;
            InitializeComponent();
        }
    }
}
