using CommunityToolkit.Mvvm.Input;
using EQD2Viewer.App.UI.Views;
using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Logging;
using EQD2Viewer.Core.Models;
using EQD2Viewer.App.UI.Rendering;
using OxyPlot;
using OxyPlot.Series;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EQD2Viewer.App.UI.ViewModels
{
    public partial class MainViewModel
    {
        private ISummationService? _summationService;
        private SummationConfig? _activeSummationConfig;
        private CancellationTokenSource? _summationCts;
        private CancellationTokenSource? _recomputeCts;
        private DispatcherTimer? _displayAlphaBetaDebounce;

        private bool _isSummationActive;
        public bool IsSummationActive
        {
            get => _isSummationActive;
            set { if (SetProperty(ref _isSummationActive, value)) { OnPropertyChanged(nameof(SummationStatusLabel)); RequestRender(); } }
        }

        private bool _isSummationComputing;
        public bool IsSummationComputing { get => _isSummationComputing; set => SetProperty(ref _isSummationComputing, value); }

        private int _summationProgress;
        public int SummationProgress { get => _summationProgress; set => SetProperty(ref _summationProgress, value); }

        private string _summationInfo = "No summation active";
        public string SummationInfo { get => _summationInfo; set => SetProperty(ref _summationInfo, value); }

        public string SummationStatusLabel => _isSummationActive ? "Summation active" : "";

        [RelayCommand]
        private async Task OpenSummationDialog()
        {
            if (_summationDataLoader == null || _summationServiceFactory == null)
            {
                MessageBox.Show("Plan summation is not available in this mode.\n" +
                                "Summation requires full clinical data access.",
                                "EQD2 Viewer", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new PlanSummationDialog(
                _snapshot.AllCourses,
                _snapshot.Registrations,
                _snapshot.ActivePlan,
                _snapshot.CtImage);
            dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (dialog.ShowDialog() == true && dialog.ResultConfig != null)
                await ExecuteSummationAsync(dialog.ResultConfig);
        }

        [RelayCommand]
        private void CancelSummation()
        {
            _summationCts?.Cancel();
            _recomputeCts?.Cancel();
        }

        [RelayCommand]
        private void ClearSummation()
        {
            _summationCts?.Cancel();
            _recomputeCts?.Cancel();
            _summationService?.Dispose();
            _summationService = null;
            _activeSummationConfig = null;
            _lastMaxDoseGy = 0;
            _lastRefDoseGy = 0;
            IsSummationActive = false;
            IsSummationComputing = false;
            SummationProgress = 0;
            SummationInfo = "No summation active";
            _doseOverlay.SummationAlphaBetaLabel = "";
            // Hotspot is still valid for single-plan mode — recompute from snapshot dose
            // so the button keeps working after clearing the summation.
            ComputeSinglePlanHotspot();
            CurrentOverlayMode = OverlayMode.Off;
            OverlayPlanOptions.Clear();
            ClearSummationDVH();
            if (_doseOverlay.CurrentIsodoseMode == IsodoseMode.Absolute)
                _doseOverlay.LoadPreset("Eclipse");
            RequestRender();
        }

        private async Task ExecuteSummationAsync(SummationConfig config)
        {
            _summationCts?.Cancel();
            _recomputeCts?.Cancel();
            _summationCts = new CancellationTokenSource();
            var ct = _summationCts.Token;
            IsSummationComputing = true;
            SummationProgress = 0;
            SummationInfo = "Loading plan data...";

            bool succeeded = false;
            try
            {
                // The previous summation is gone from here on. Its Σ rows and curves must not
                // outlive it: if this attempt fails or is cancelled, nothing may keep showing
                // numbers that no service can recompute.
                _summationService?.Dispose();
                _summationService = null;
                _activeSummationConfig = null;
                IsSummationActive = false;
                ClearSummationDVH();

                _summationService = _summationServiceFactory!.Create(
                    _snapshot.CtImage!, _summationDataLoader!, _snapshot.Registrations);
                var prepResult = _summationService.PrepareData(config);
                if (!prepResult.Success)
                {
                    MessageBox.Show($"Failed:\n{prepResult.StatusMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusText = prepResult.StatusMessage;
                SummationInfo = "Computing...";
                var progress = new Progress<int>(pct => { SummationProgress = pct; SummationInfo = $"Computing... {pct}%"; });
                ct.ThrowIfCancellationRequested();
                var result = await _summationService.ComputeAsync(progress, ct);

                if (result.Success)
                {
                    _activeSummationConfig = config;
                    IsSummationActive = true;

                    _doseOverlay.DisplayAlphaBeta = config.GlobalAlphaBeta;
                    SetHotspot(result.MaxDoseGy, result.MaxDoseSliceZ, result.MaxDosePixelX, result.MaxDosePixelY);
                    RefreshSummationLabels(result.MaxDoseGy, result.TotalReferenceDoseGy);
                    StatusText = result.StatusMessage;

                    if (_doseOverlay.CurrentIsodoseMode != IsodoseMode.Absolute)
                        _doseOverlay.LoadPreset("ReIrradiation");

                    OverlayPlanOptions.Clear();
                    foreach (var plan in config.Plans.Where(p => !p.IsReference))
                        OverlayPlanOptions.Add(plan.DisplayLabel);
                    if (OverlayPlanOptions.Count > 0) SelectedOverlayPlanLabel = OverlayPlanOptions[0];

                    CalculateSummationDVH();
                    RequestRender();
                    succeeded = true;
                }
                else
                {
                    SummationInfo = result.StatusMessage;
                    if (!ct.IsCancellationRequested)
                        MessageBox.Show($"Failed:\n{result.StatusMessage}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException) { SummationInfo = "Cancelled."; }
            catch (Exception ex) { SimpleLogger.Error("Summation failed", ex); MessageBox.Show($"Error:\n{ex.Message}"); }
            finally
            {
                IsSummationComputing = false;
                if (!succeeded)
                {
                    _summationService?.Dispose();
                    _summationService = null;
                    _activeSummationConfig = null;
                    IsSummationActive = false;
                    ClearSummationDVH();
                    ComputeSinglePlanHotspot();
                    RequestRender();
                }
            }
        }

        /// <summary>
        /// Rebuilds both summation status labels from the current active config + display α/β.
        /// Called after initial compute, after α/β-driven recompute, and on slider drag so the
        /// labels never drift out of sync with what the isodose overlay is actually showing.
        /// </summary>
        private double _lastMaxDoseGy;
        private double _lastRefDoseGy;

        private void RefreshSummationLabels(double maxGy, double refGy)
        {
            _lastMaxDoseGy = maxGy;
            _lastRefDoseGy = refGy;
            if (_activeSummationConfig == null)
            {
                SummationInfo = "No summation active";
                _doseOverlay.SummationAlphaBetaLabel = "";
                return;
            }
            string method = _activeSummationConfig.Method == SummationMethod.EQD2 ? "EQD2" : "Physical";
            int planCount = _activeSummationConfig.Plans.Count;
            string plans = planCount == 1 ? "1 plan" : $"{planCount} plans";
            SummationInfo = $"{method} sum: {plans} | Max: {maxGy:F2} Gy | Ref: {refGy:F2} Gy";

            // The open plan's Eclipse EQD2 row uses the fraction slider; its Σ row uses the
            // fraction count entered in the summation dialog. If they differ, the two rows
            // are not comparable and the user should know why.
            var openPlanEntry = _activeSummationConfig.Plans.FirstOrDefault(p =>
                p.PlanId == _snapshot?.ActivePlan?.Id && p.CourseId == _snapshot?.ActivePlan?.CourseId);
            if (openPlanEntry != null && openPlanEntry.NumberOfFractions != _doseOverlay.NumberOfFractions)
                SummationInfo += $" | Note: Σ uses {openPlanEntry.NumberOfFractions} fx for the open plan, " +
                                 $"the EQD2 row uses the slider ({_doseOverlay.NumberOfFractions} fx)";

            double displayAb = _doseOverlay.DisplayAlphaBeta;
            double summationAb = _activeSummationConfig.GlobalAlphaBeta;
            _doseOverlay.SummationAlphaBetaLabel =
                Math.Abs(displayAb - summationAb) < EQD2Viewer.Core.Models.DomainConstants.AlphaBetaMatchTolerance
                    ? $"Isodose & summation α/β = {summationAb:F1} Gy"
                    : $"Isodose α/β = {displayAb:F1} Gy   (summation was α/β = {summationAb:F1})";
        }

        internal void RecomputeDisplayEQD2IfActive()
        {
            // Keep the α/β badge in sync immediately on slider drag, even before the debounced
            // recompute finishes — otherwise the label lags a slider tick behind reality.
            if (_isSummationActive && _activeSummationConfig != null)
                RefreshSummationLabels(_lastMaxDoseGy, _lastRefDoseGy);

            if (!_isSummationActive || _summationService == null) return;

            if (_displayAlphaBetaDebounce == null)
            {
                _displayAlphaBetaDebounce = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(RenderConstants.AlphaBetaDebounceMs)
                };
                _displayAlphaBetaDebounce.Tick += async (s, e) =>
                {
                    _displayAlphaBetaDebounce.Stop();
                    await RecomputeDisplayEQD2Core();
                };
            }
            _displayAlphaBetaDebounce.Stop();
            _displayAlphaBetaDebounce.Start();
        }

        private async Task RecomputeDisplayEQD2Core()
        {
            if (_summationService == null || !_isSummationActive) return;

            _recomputeCts?.Cancel();
            _recomputeCts = new CancellationTokenSource();
            var ct = _recomputeCts.Token;

            try
            {
                IsSummationComputing = true;
                SummationInfo = $"Updating display α/β = {_doseOverlay.DisplayAlphaBeta:F1} Gy...";

                var progress = new Progress<int>(pct => SummationProgress = pct);
                var result = await _summationService.RecomputeEQD2DisplayAsync(
                    _doseOverlay.DisplayAlphaBeta, progress, ct);

                if (result.Success)
                {
                    SetHotspot(result.MaxDoseGy, result.MaxDoseSliceZ, result.MaxDosePixelX, result.MaxDosePixelY);
                    RefreshSummationLabels(result.MaxDoseGy, result.TotalReferenceDoseGy);
                    StatusText = result.StatusMessage;
                    RequestRender();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SimpleLogger.Error("RecomputeDisplayEQD2 failed", ex);
            }
            finally { IsSummationComputing = false; }
        }

        /// <summary>
        /// Recomputes the summation ("Σ") rows and curves when the selected structures or
        /// their α/β change while a summation is active. No-op otherwise.
        ///
        /// Debounced: the α/β cell updates its binding on every keystroke, and each Σ refresh
        /// is a full pass over every selected structure's voxels for every plan. Typing "2.5"
        /// must cost one recompute, not four.
        /// </summary>
        private DispatcherTimer? _summationDvhDebounce;

        private void RefreshSummationDVHIfActive()
        {
            if (!_isSummationActive || _summationService == null || !_summationService.HasSummedDose) return;

            if (_summationDvhDebounce == null)
            {
                _summationDvhDebounce = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(RenderConstants.AlphaBetaDebounceMs)
                };
                _summationDvhDebounce.Tick += (s, e) =>
                {
                    _summationDvhDebounce.Stop();
                    if (_isSummationActive && _summationService != null && _summationService.HasSummedDose)
                        CalculateSummationDVH();
                };
            }
            _summationDvhDebounce.Stop();
            _summationDvhDebounce.Start();
        }

        private void CalculateSummationDVH()
        {
            if (_summationService == null || !_summationService.HasSummedDose) return;
            var structureIds = _summationService.GetCachedStructureIds();
            if (structureIds == null || structureIds.Count == 0) return;

            var selectedIds = _dvhCache.Select(c => c.Structure.Id).ToHashSet();
            double voxelVolCc = _summationService.GetVoxelVolumeCc();
            bool isEqd2Sum = _activeSummationConfig?.Method == SummationMethod.EQD2;

            ClearSummationDVH();

            foreach (var structureId in structureIds)
            {
                if (!selectedIds.Contains(structureId)) continue;

                var structureSetting = StructureSettings.FirstOrDefault(s => s.Id == structureId);
                double structureAlphaBeta = structureSetting?.AlphaBeta ?? 3.0;
                // Σ total and per-plan rows share the Plan column prefix "Σ"; the Type column
                // tells them apart ("Σ EQD2 …" for the total, "EQD2 …" for one plan's part).
                string methodLabel = isEqd2Sum ? $"EQD2 α/β={structureAlphaBeta:F1}" : "Physical";
                string totalLabel = isEqd2Sum ? $"Σ EQD2 α/β={structureAlphaBeta:F1}" : "Σ Physical";

                // One calculation path for both EQD2 and Physical mode. The table row is
                // built from exact voxel statistics — never read back from the curve.
                var result = _summationService.ComputeStructureDVH(structureId, structureAlphaBeta);
                var cached = _dvhCache.FirstOrDefault(c => c.Structure.Id == structureId);

                if (result.IsEmpty)
                {
                    // The contour rasterised to no CT voxel (thinner than a pixel, or between row
                    // centres). Say so in the table instead of silently leaving the structure out.
                    SimpleLogger.Warning($"Summation DVH: structure '{structureId}' covers no voxel on the CT grid.");
                    SummaryData.Add(new DVHSummary
                    {
                        StructureId = structureId,
                        PlanId = "Summation",
                        Type = "no voxels on CT grid",
                        Source = DVHSummary.SourceVoxelSum,
                        DMax = double.NaN, DMean = double.NaN, DMin = double.NaN, Volume = 0,
                        IsSummation = true
                    });
                    continue;
                }

                // Volume is the volume the statistics were computed on: CT-grid voxels inside the
                // contour. Eclipse's own figure stays on the Eclipse rows — the difference between
                // the two is exactly what a single-plan validation is meant to show.
                double volumeCc = result.Statistics.VoxelCount * voxelVolCc;

                SummaryData.Add(new DVHSummary
                {
                    StructureId = structureId,
                    PlanId = "Summation",
                    Type = totalLabel,
                    Source = DVHSummary.SourceVoxelSum,
                    DMax = result.Statistics.DMaxGy,
                    DMean = result.Statistics.DMeanGy,
                    DMin = result.Statistics.DMinGy,
                    Volume = volumeCc,
                    IsSummation = true
                });

                OxyColor color = cached != null
                    ? OxyColor.FromArgb(cached.Structure.ColorA, cached.Structure.ColorR, cached.Structure.ColorG, cached.Structure.ColorB)
                    : OxyColors.White;

                var series = new LineSeries
                {
                    Title = $"{structureId} {totalLabel}",
                    Tag = $"Summation_{structureId}",
                    Color = color,
                    StrokeThickness = 2.5,
                    LineStyle = LineStyle.DashDot
                };
                series.Points.AddRange(result.Curve.Select(p => new DataPoint(p.DoseGy, p.VolumePercent)));
                PlotModel.Series.Add(series);

                // Each plan's own contribution to the sum, from the same conversion and the same
                // voxels — so the user can see what the Σ curve is made of. Thin dotted lines,
                // toggled by ShowPerPlanSummationDVH; rows are labelled "Σ <plan>". With a
                // single plan the part equals the total, so nothing is duplicated.
                if (_activeSummationConfig != null && _activeSummationConfig.Plans.Count > 1)
                {
                    foreach (var plan in _activeSummationConfig.Plans)
                    {
                        var planResult = _summationService.ComputeStructurePlanDVH(plan.DisplayLabel, structureId, structureAlphaBeta);
                        if (planResult.IsEmpty) continue;

                        string fxLabel = isEqd2Sum ? $"{methodLabel} · {plan.NumberOfFractions} fx" : methodLabel;
                        SummaryData.Add(new DVHSummary
                        {
                            StructureId = structureId,
                            PlanId = $"Σ {plan.DisplayLabel}",
                            Type = fxLabel,
                            Source = DVHSummary.SourceVoxelSum,
                            DMax = planResult.Statistics.DMaxGy,
                            DMean = planResult.Statistics.DMeanGy,
                            DMin = planResult.Statistics.DMinGy,
                            Volume = volumeCc,
                            IsSummation = true
                        });

                        var planSeries = new LineSeries
                        {
                            Title = $"{structureId} Σ {plan.DisplayLabel} ({fxLabel})",
                            Tag = $"SummationPlan_{plan.DisplayLabel}_{structureId}",
                            Color = color,
                            StrokeThickness = 1.2,
                            LineStyle = LineStyle.Dot
                        };
                        planSeries.Points.AddRange(planResult.Curve.Select(p => new DataPoint(p.DoseGy, p.VolumePercent)));
                        PlotModel.Series.Add(planSeries);
                    }
                }
            }
            RefreshPlot();
        }

        private void ClearSummationDVH()
        {
            // Both the Σ total ("Summation_") and the per-plan ("SummationPlan_") series.
            foreach (var s in PlotModel.Series.Where(s => (s.Tag as string)?.StartsWith("Summation") ?? false).ToList())
                PlotModel.Series.Remove(s);
            foreach (var s in SummaryData.Where(s => s.IsSummation).ToList())
                SummaryData.Remove(s);
            // OxyPlot does not redraw on collection changes — without this the removed curves
            // stay on screen until something else invalidates the plot.
            RefreshPlot();
        }

        private void RenderSummationScene()
        {
            double[]? summedSlice = _summationService!.GetSummedSlice(CurrentSlice);
            if (summedSlice == null) return;

            double refDose = _summationService.SummedReferenceDoseGy;
            if (refDose < DomainConstants.MinReferenceDoseGy) refDose = 1.0;

            int w = _snapshot.CtImage!.XSize, h = _snapshot.CtImage!.YSize;

            if (_doseOverlay.DoseDisplayMode == DoseDisplayMode.Line)
            {
                ClearDoseBitmap(w, h);
                var contours = new ObservableCollection<IsodoseContourData>();

                foreach (var level in _doseOverlay._isodoseLevelArray)
                {
                    if (!level.IsVisible) continue;
                    double thr = _doseOverlay.GetThresholdGy(level, refDose);
                    if (thr <= 0) continue;
                    var polylines = MarchingSquares.GenerateContours(summedSlice, w, h, thr);
                    if (polylines.Count == 0) continue;

                    var geo = new System.Windows.Media.StreamGeometry();
                    using (var ctx = geo.Open())
                        foreach (var chain in polylines)
                        {
                            if (chain.Count < 2) continue;
                            ctx.BeginFigure(new System.Windows.Point(chain[0].X, chain[0].Y), false, false);
                            for (int j = 1; j < chain.Count; j++) ctx.LineTo(new System.Windows.Point(chain[j].X, chain[j].Y), true, false);
                        }
                    geo.Freeze();

                    uint c = level.Color;
                    var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(
                        (byte)((c >> 16) & 0xFF), (byte)((c >> 8) & 0xFF), (byte)(c & 0xFF)));
                    brush.Freeze();
                    contours.Add(new IsodoseContourData { Geometry = geo, Stroke = brush, StrokeThickness = 1.0 });
                }
                ContourLines = contours;
                StatusText = $"[Summation · Line] Slice {CurrentSlice} | Ref: {refDose:F2} Gy | α/β: {_doseOverlay.DisplayAlphaBeta:F1}";
            }
            else
            {
                if (_contourLines?.Count > 0) ContourLines = new ObservableCollection<IsodoseContourData>();
                RenderSummedDoseBitmap(summedSlice, refDose, w, h);
            }
        }

        private unsafe void ClearDoseBitmap(int w, int h)
        {
            var bmp = DoseImageSource;
            if (bmp == null || bmp.PixelWidth != w || bmp.PixelHeight != h)
                return;

            bmp.Lock();
            try
            {
                byte* p = (byte*)bmp.BackBuffer;
                int safeLen = h * bmp.BackBufferStride;
                for (int i = 0; i < safeLen; i++) p[i] = 0;
                bmp.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally { bmp.Unlock(); }
        }

        private unsafe void RenderSummedDoseBitmap(double[] slice, double refDose, int w, int h)
        {
            var bmp = DoseImageSource;
            if (bmp == null || bmp.PixelWidth != w || bmp.PixelHeight != h)
                return;

            bmp.Lock();
            try
            {
                byte* pBuf = (byte*)bmp.BackBuffer;
                int stride = bmp.BackBufferStride;

                // Bounds safety: verify stride is sufficient for pixel width
                if (stride < w * 4)
                    return;

                int totalBytes = h * stride;
                for (int i = 0; i < totalBytes; i++) pBuf[i] = 0;

                // Bounds safety: verify slice data matches expected dimensions
                if (slice.Length < w * h)
                {
                    bmp.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
                    return;
                }

                if (_doseOverlay.DoseDisplayMode == DoseDisplayMode.Fill)
                {
                    // ...existing Fill mode code...
                    int vc = 0;
                    for (int i = 0; i < _doseOverlay._isodoseLevelArray.Length; i++) if (_doseOverlay._isodoseLevelArray[i].IsVisible) vc++;
                    if (vc > 0)
                    {
                        double[] thr = new double[vc]; uint[] col = new uint[vc]; int vi = 0;
                        for (int i = 0; i < _doseOverlay._isodoseLevelArray.Length; i++)
                        {
                            if (!_doseOverlay._isodoseLevelArray[i].IsVisible) continue;
                            thr[vi] = _doseOverlay.GetThresholdGy(_doseOverlay._isodoseLevelArray[i], refDose);
                            col[vi] = (_doseOverlay._isodoseLevelArray[i].Color & 0x00FFFFFF) | ((uint)_doseOverlay._isodoseLevelArray[i].Alpha << 24);
                            vi++;
                        }
                        for (int py = 0; py < h; py++)
                        {
                            uint* row = (uint*)(pBuf + py * stride); int ro = py * w;
                            for (int px = 0; px < w; px++)
                            {
                                double d = slice[ro + px]; if (d <= 0) continue;
                                for (int li = 0; li < vc; li++) if (d >= thr[li]) { row[px] = col[li]; break; }
                            }
                        }
                    }
                }
                else if (_doseOverlay.DoseDisplayMode == DoseDisplayMode.Colorwash)
                {
                    // ...existing Colorwash mode code...
                    byte cwA = (byte)(System.Math.Max(0, System.Math.Min(1, _doseOverlay.ColorwashOpacity)) * 255);
                    double minGy = refDose * _doseOverlay.ColorwashMinPercent, maxGy = refDose * RenderConstants.ColorwashMaxFraction;
                    double range = maxGy - minGy;
                    if (range > 0)
                        for (int py = 0; py < h; py++)
                        {
                            uint* row = (uint*)(pBuf + py * stride); int ro = py * w;
                            for (int px = 0; px < w; px++)
                            {
                                double d = slice[ro + px]; if (d < minGy) continue;
                                row[px] = ColorMaps.Jet(System.Math.Min(1.0, (d - minGy) / range), cwA);
                            }
                        }
                }

                string ml = _doseOverlay.DoseDisplayMode == DoseDisplayMode.Fill ? "Fill" : "Colorwash";
                StatusText = $"[Summation · {ml}] Slice {CurrentSlice} | Ref: {refDose:F2} Gy | α/β: {_doseOverlay.DisplayAlphaBeta:F1}";
                bmp.AddDirtyRect(new System.Windows.Int32Rect(0, 0, w, h));
            }
            finally { bmp.Unlock(); }
        }
    }
}
