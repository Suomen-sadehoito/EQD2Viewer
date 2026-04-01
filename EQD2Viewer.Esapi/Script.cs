using System;
using System.Windows;
using VMS.TPS.Common.Model.API;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Logging;
using ESAPI_EQD2Viewer.Services;
using ESAPI_EQD2Viewer.UI.ViewModels;
using ESAPI_EQD2Viewer.UI.Views;

[assembly: ESAPIScript(IsWriteable = false)]
namespace VMS.TPS
{
    /// <summary>
    /// Eclipse ESAPI script entry point for the EQD2 Viewer.
    /// This is the only file in the solution that carries the [ESAPIScript] attribute.
    /// It wires together the ESAPI adapters (EQD2Viewer.Esapi layer) with the
    /// WPF UI and services (ESAPI_EQD2Viewer layer) without leaking VMS.TPS types
    /// beyond this class.
    /// </summary>
    public class Script
    {
        [System.Runtime.CompilerServices.MethodImpl(
System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context)
        {
    if (context.Patient == null || context.Image == null)
       {
     MessageBox.Show(
    "Please open a patient with an image before running the script.",
             "EQD2 Viewer",
            MessageBoxButton.OK,
             MessageBoxImage.Warning);
     return;
            }

    try
            {
    SimpleLogger.EnableFileLogging();

        // -- Load the full clinical snapshot via the ESAPI adapter layer --
      // EsapiDataSource is the only class that touches VMS.TPS namespaces for
       // patient/image/dose data.  Everything downstream works with plain C# POCOs.
                var dataSource = new EQD2Viewer.Esapi.Adapters.EsapiDataSource(context);
       var snapshot   = dataSource.LoadSnapshot();

      // -- Create WPF-layer services (no ESAPI dependency) --
                IImageRenderingService renderingService = new ImageRenderingService();
    IDebugExportService    debugService   = new DebugExportService();
    IDVHCalculation        dvhService       = new DVHService();

          // -- Create the ESAPI summation data loader for on-demand plan loading --
         // EsapiSummationDataLoader is the only class that touches VMS.TPS for
             // multi-plan dose summation.  SummationService itself stays ESAPI-free.
    ISummationDataLoader summationLoader =
       new EQD2Viewer.Esapi.Adapters.EsapiSummationDataLoader(context.Patient);

      // -- Initialise the rendering pipeline from snapshot dimensions --
     int width  = snapshot.CtImage.XSize;
        int height = snapshot.CtImage.YSize;
     renderingService.Initialize(width, height);
   renderingService.PreloadData(snapshot.CtImage, snapshot.Dose);

                // -- Build ViewModel and launch the WPF window --
        var viewModel = new MainViewModel(
   snapshot,
      renderingService,
      debugService,
        dvhService,
            summationLoader);

                var window = new MainWindow(viewModel);
                window.ShowDialog();
  }
  catch (Exception ex)
   {
     SimpleLogger.Error("Fatal error in Script.Execute", ex);
       MessageBox.Show(
            $"Error:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
         "EQD2 Viewer Error",
    MessageBoxButton.OK,
   MessageBoxImage.Error);
            }
}
    }
}
