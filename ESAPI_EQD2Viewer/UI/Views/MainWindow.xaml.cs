using ESAPI_EQD2Viewer.UI.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VMS.TPS.Common.Model.API;

namespace ESAPI_EQD2Viewer.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ScriptContext _context;

        public MainWindow(MainViewModel viewModel, ScriptContext context)
        {
            // Register BooleanToVisibility converter before InitializeComponent
            Resources.Add("BoolToVis", new BooleanToVisibilityConverter());

            InitializeComponent();
            _viewModel = viewModel;
            _context = context;
            DataContext = viewModel;

            Closed += (s, e) => viewModel?.Dispose();
        }

        private void SelectStructures_Click(object sender, RoutedEventArgs e)
        {
            var plan = _context.ExternalPlanSetup;
            if (plan?.StructureSet == null)
            {
                MessageBox.Show("No plan or structure set available.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new StructureSelectionDialog(plan);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedStructures.Any())
            {
                _viewModel.AddStructuresForDVH(dialog.SelectedStructures);
            }
        }
    }
}