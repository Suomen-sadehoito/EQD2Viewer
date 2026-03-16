using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_IsodoseViewer.Core.Interfaces;
using ESAPI_IsodoseViewer.Core.Models;

namespace ESAPI_IsodoseViewer.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ScriptContext _context;
        private readonly PlanSetup _plan;
        private readonly IImageRenderingService _renderingService;
        private readonly IDebugExportService _debugExportService;

        private int _renderPendingFlag = 0;
        private bool _disposed;

        #region Observable Properties

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

        public ObservableCollection<IsodoseLevel> IsodoseLevels { get; }

        private IsodoseLevel[] _isodoseLevelArray;

        public MainViewModel(ScriptContext context, IImageRenderingService renderingService, IDebugExportService debugExportService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _plan = context.ExternalPlanSetup;
            _renderingService = renderingService ?? throw new ArgumentNullException(nameof(renderingService));
            _debugExportService = debugExportService ?? throw new ArgumentNullException(nameof(debugExportService));

            var defaults = IsodoseLevel.GetDefaults();
            IsodoseLevels = new ObservableCollection<IsodoseLevel>(defaults);
            _isodoseLevelArray = defaults;

            IsodoseLevels.CollectionChanged += (s, e) =>
            {
                _isodoseLevelArray = new IsodoseLevel[IsodoseLevels.Count];
                IsodoseLevels.CopyTo(_isodoseLevelArray, 0);
                RequestRender();
            };

            int width = _context.Image.XSize;
            int height = _context.Image.YSize;

            _maxSlice = _context.Image.ZSize - 1;
            _currentSlice = _maxSlice / 2;

            _renderingService.Initialize(width, height);
            StatusText = "Loading image and dose data into memory...";

            double prescriptionGy = 0;
            if (_plan != null)
            {
                prescriptionGy = _plan.TotalDose.Unit == DoseValue.DoseUnit.cGy
                    ? _plan.TotalDose.Dose / 100.0
                    : _plan.TotalDose.Dose;
            }

            _renderingService.PreloadData(_context.Image, _plan?.Dose, prescriptionGy);

            CtImageSource = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            DoseImageSource = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

            AutoPreset();
        }

        private void RequestRender()
        {
            if (Interlocked.CompareExchange(ref _renderPendingFlag, 1, 0) != 0)
                return;

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Interlocked.Exchange(ref _renderPendingFlag, 0);
                RenderScene();
            }), DispatcherPriority.Render);
        }

        private void RenderScene()
        {
            if (_disposed || _context.Image == null) return;

            _renderingService.RenderCtImage(_context.Image, CtImageSource, CurrentSlice, WindowLevel, WindowWidth);

            double planTotalDoseGy = 0;
            if (_plan != null)
            {
                planTotalDoseGy = _plan.TotalDose.Unit == DoseValue.DoseUnit.cGy
                    ? _plan.TotalDose.Dose / 100.0
                    : _plan.TotalDose.Dose;
            }

            double planNormalization = _plan?.PlanNormalizationValue ?? 100.0;

            StatusText = _renderingService.RenderDoseImage(
                _context.Image,
                _plan?.Dose,
                DoseImageSource,
                CurrentSlice,
                planTotalDoseGy,
                planNormalization,
                _isodoseLevelArray);
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
                case "Soft":
                    WindowLevel = 40;
                    WindowWidth = 400;
                    break;
                case "Lung":
                    WindowLevel = -600;
                    WindowWidth = 1600;
                    break;
                case "Bone":
                    WindowLevel = 300;
                    WindowWidth = 1500;
                    break;
            }
        }

        [RelayCommand]
        private void Debug()
        {
            _debugExportService.ExportDebugLog(_context, _plan, CurrentSlice);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _renderingService?.Dispose();
            _ctImageSource = null;
            _doseImageSource = null;
        }
    }
}
