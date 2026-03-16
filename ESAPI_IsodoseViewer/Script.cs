using System.Windows;
using VMS.TPS.Common.Model.API;
using ESAPI_IsodoseViewer.Core.Interfaces;
using ESAPI_IsodoseViewer.Services;
using ESAPI_IsodoseViewer.UI.ViewModels;
using ESAPI_IsodoseViewer.UI.Views;

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

            IImageRenderingService renderingService = new ImageRenderingService();
            IDebugExportService debugService = new DebugExportService();

            var mainViewModel = new MainViewModel(context, renderingService, debugService);

            var window = new ViewerWindow(mainViewModel);
            window.ShowDialog();
        }
    }
}