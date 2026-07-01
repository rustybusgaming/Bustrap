using System.Windows.Controls;
using Bustrap.UI.ViewModels.Pages;

namespace Bustrap.UI.Elements.Settings.Pages
{
    public partial class HistoryPage : Page
    {
        private readonly HistoryPageViewModel _viewModel;
        public HistoryPage()
        {
            InitializeComponent();
            _viewModel = new HistoryPageViewModel();
            DataContext = _viewModel;
        }
    }
}