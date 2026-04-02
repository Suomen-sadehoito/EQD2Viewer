using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Rendering
{
    /// <summary>
    /// WPF-specific rendering service interface.
    /// Operates on WriteableBitmap and produces WPF StreamGeometry contours.
    /// 
    /// This interface lives in the WPF layer, not in EQD2Viewer.Core,
    /// because it depends on WPF types (WriteableBitmap, StreamGeometry).
    /// 
    /// All data inputs come from Core types (VolumeData, DoseVolumeData, etc.)
    /// — this is the adapter boundary between pure domain data and WPF rendering.
    /// </summary>
    public interface IImageRenderingService : IDisposable
    {
        void Initialize(int width, int height);

        void PreloadData(VolumeData ctImage, DoseVolumeData dose);

        void RenderCtImage(WriteableBitmap targetBitmap, int currentSlice, double windowLevel, double windowWidth);

        string RenderDoseImage(WriteableBitmap targetBitmap, int currentSlice, double planTotalDoseGy,
            double planNormalization, IsodoseLevel[] levels, DoseDisplayMode displayMode = DoseDisplayMode.Line,
            double colorwashOpacity = 0.5, double colorwashMinPercent = 0.1, EQD2Settings eqd2Settings = null);

        ContourGenerationResult GenerateVectorContours(int currentSlice, double planTotalDoseGy,
            double planNormalization, IsodoseLevel[] levels, EQD2Settings eqd2Settings = null);

        double GetDoseAtPixel(int currentSlice, int pixelX, int pixelY, EQD2Settings eqd2Settings = null);

        List<StructureContourData> GenerateStructureContours(int currentSlice, IEnumerable<StructureData> structures);
    }

    public class ContourGenerationResult
    {
        public List<IsodoseContourData> Contours { get; set; }
        public string StatusText { get; set; }
    }
}