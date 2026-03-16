using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VMS.TPS.Common.Model.API;
using ESAPI_EQD2Viewer.UI.ViewModels;

namespace ESAPI_EQD2Viewer.UI.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly ScriptContext _context;

        public MainWindow(MainViewModel viewModel, ScriptContext context)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _context = context;
            DataContext = viewModel;

            Closed += (s, e) => viewModel?.Dispose();
            Loaded += (s, e) => UpdateLegendColors();
        }

        /// <summary>
        /// Opens a structure selection dialog and loads selected structures into DVH analysis.
        /// </summary>
        private void SelectStructures_Click(object sender, RoutedEventArgs e)
        {
            var plan = _context.ExternalPlanSetup;
            if (plan?.StructureSet == null)
            {
                MessageBox.Show("No plan or structure set available.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new StructureSelectionDialog(plan);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedStructures.Any())
            {
                _viewModel.AddStructuresForDVH(dialog.SelectedStructures);
            }
        }

        private void UpdateLegendColors()
        {
            if (_viewModel?.IsodoseLevels == null) return;

            var itemsControl = FindName("IsodoseLegend") as ItemsControl;
            if (itemsControl == null) return;

            itemsControl.ItemContainerGenerator.StatusChanged += (sender, args) =>
            {
                if (itemsControl.ItemContainerGenerator.Status ==
                    System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    SetRectangleColors(itemsControl);
                }
            };

            SetRectangleColors(itemsControl);
        }

        private void SetRectangleColors(ItemsControl itemsControl)
        {
            for (int i = 0; i < _viewModel.IsodoseLevels.Count; i++)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;

                container.ApplyTemplate();
                var rect = FindVisualChild<Rectangle>(container);
                if (rect != null)
                {
                    rect.Fill = new SolidColorBrush(_viewModel.IsodoseLevels[i].GetMediaColor());
                }
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
