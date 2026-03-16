using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ESAPI_IsodoseViewer.Services;
using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_IsodoseViewer.Core.Interfaces;

namespace ESAPI_IsodoseViewer.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly ScriptContext _context;
        private readonly PlanSetup _plan;
        private readonly IImageRenderingService _renderingService;
        private readonly IDebugExportService _debugExportService;
        private bool _renderPending = false;

        #region Properties (Manual implementation for 100% stability)

        private WriteableBitmap _ctImageSource;
        public WriteableBitmap CtImageSource
        {
            get => _ctImageSource;
            set => SetProperty(ref _ctImageSource, value);
        }

        private WriteableBitmap _doseImageSource;
        public WriteableBitmap DoseImageSource
        {
            get => _doseImageSource;
            set => SetProperty(ref _doseImageSource, value);
        }

        private int _currentSlice;
        public int CurrentSlice
        {
            get => _currentSlice;
            set
            {
                if (SetProperty(ref _currentSlice, value))
                    RequestRender();
            }
        }

        private int _maxSlice;
        public int MaxSlice
        {
            get => _maxSlice;
            set => SetProperty(ref _maxSlice, value);
        }

        private double _windowLevel;
        public double WindowLevel
        {
            get => _windowLevel;
            set
            {
                if (SetProperty(ref _windowLevel, value))
                    RequestRender();
            }
        }

        private double _windowWidth;
        public double WindowWidth
        {
            get => _windowWidth;
            set
            {
                if (SetProperty(ref _windowWidth, value))
                    RequestRender();
            }
        }

        private string _statusText;
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        #endregion

        public MainViewModel(ScriptContext context, IImageRenderingService renderingService, IDebugExportService debugExportService)
        {
            _context = context;
            _plan = context.ExternalPlanSetup;
            _renderingService = renderingService;
            _debugExportService = debugExportService;

            int width = _context.Image.XSize;
            int height = _context.Image.YSize;

            _maxSlice = _context.Image.ZSize - 1;
            _currentSlice = _maxSlice / 2;

            _renderingService.Initialize(width, height);
            StatusText = "Loading image and dose data into memory...";
            _renderingService.PreloadData(_context.Image, _plan?.Dose);

            CtImageSource = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            DoseImageSource = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            AutoPreset();
        }

        private void RequestRender()
        {
            if (_renderPending) return;
            _renderPending = true;

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _renderPending = false;
                RenderScene();
            }), DispatcherPriority.Render);
        }

        private void RenderScene()
        {
            if (_context.Image == null) return;

            _renderingService.RenderCtImage(_context.Image, CtImageSource, CurrentSlice, WindowLevel, WindowWidth);

            double planTotalDose = _plan?.TotalDose.Unit == DoseValue.DoseUnit.cGy
                ? _plan.TotalDose.Dose / 100.0
                : _plan?.TotalDose.Dose ?? 0;

            double planNormalization = _plan?.PlanNormalizationValue ?? 100.0;

            StatusText = _renderingService.RenderDoseImage(
                _context.Image,
                _plan?.Dose,
                DoseImageSource,
                CurrentSlice,
                planTotalDose,
                planNormalization);
        }

        [RelayCommand]
        private void AutoPreset()
        {
            WindowLevel = 40;
            WindowWidth = 400;
        }

        [RelayCommand]
        private void Preset(string type)
        {
            switch (type)
            {
                case "Soft": WindowLevel = 40; WindowWidth = 400; break;
                case "Lung": WindowLevel = -600; WindowWidth = 1600; break;
                case "Bone": WindowLevel = 300; WindowWidth = 1500; break;
            }
        }

        [RelayCommand]
        private void Debug()
        {
            _debugExportService.ExportDebugLog(_context, _plan, CurrentSlice);
        }
    }
}