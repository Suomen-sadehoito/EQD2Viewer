using System.Windows;
using VMS.TPS.Common.Model.API;
using ESAPI_IsodoseViewer.Core.Interfaces;
using ESAPI_IsodoseViewer.Services;
using ESAPI_IsodoseViewer.UI.ViewModels;
using ESAPI_IsodoseViewer.UI.Views;
using Microsoft.Extensions.DependencyInjection;

[assembly: ESAPIScript(IsWriteable = false)]

namespace VMS.TPS
{
    public class Script
    {
        public void Execute(ScriptContext context)
        {
            if (context.Patient == null || context.Image == null)
            {
                MessageBox.Show("Please open a patient and an image before running the script.");
                return;
            }

            // 1. Dependency Initialization (Composition Root)
            IImageRenderingService renderingService = new ImageRenderingService();
            IDebugExportService debugService = new DebugExportService();

            // 2. Inject dependencies into the ViewModel
            var mainViewModel = new MainViewModel(context, renderingService, debugService);

            // 3. Initialize the View and set DataContext
            var window = new ViewerWindow(mainViewModel);
            window.ShowDialog();
        }
    }
}