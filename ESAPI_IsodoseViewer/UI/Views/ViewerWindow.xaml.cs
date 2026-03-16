using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ESAPI_IsodoseViewer.UI.ViewModels;

namespace ESAPI_IsodoseViewer.UI.Views
{
    public partial class ViewerWindow : Window
    {
        public ViewerWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Closed += (s, e) =>
            {
                viewModel?.Dispose();
            };

            Loaded += (s, e) => UpdateLegendColors(viewModel);
        }

        private void UpdateLegendColors(MainViewModel viewModel)
        {
            if (viewModel?.IsodoseLevels == null) return;

            var itemsControl = FindVisualChild<ItemsControl>(this);
            if (itemsControl == null) return;

            itemsControl.ItemContainerGenerator.StatusChanged += (sender, args) =>
            {
                if (itemsControl.ItemContainerGenerator.Status ==
                    System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                {
                    SetRectangleColors(itemsControl, viewModel);
                }
            };

            // Also try immediately
            SetRectangleColors(itemsControl, viewModel);
        }

        private void SetRectangleColors(ItemsControl itemsControl, MainViewModel viewModel)
        {
            for (int i = 0; i < viewModel.IsodoseLevels.Count; i++)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;

                container.ApplyTemplate();
                var rect = FindVisualChild<Rectangle>(container);
                if (rect != null)
                {
                    rect.Fill = new SolidColorBrush(viewModel.IsodoseLevels[i].GetMediaColor());
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