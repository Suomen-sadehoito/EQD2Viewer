using System.Windows;
using ESAPI_IsodoseViewer.UI.ViewModels;

namespace ESAPI_IsodoseViewer.UI.Views
{
    public partial class ViewerWindow : Window
    {
        public ViewerWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            // Set the DataContext using the injected ViewModel
            DataContext = viewModel;
        }
    }
}