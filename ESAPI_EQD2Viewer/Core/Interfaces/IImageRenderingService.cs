using System;
using System.Windows.Media.Imaging;
using VMS.TPS.Common.Model.API;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Core.Interfaces
{
    public interface IImageRenderingService : IDisposable
    {
        void Initialize(int width, int height);

        /// <summary>
        /// Preloads CT and dose voxel data into memory caches.
        /// </summary>
        void PreloadData(Image ctImage, Dose dose, double prescriptionDoseGy);

        void RenderCtImage(Image ctImage, WriteableBitmap targetBitmap, int currentSlice,
            double windowLevel, double windowWidth);

        /// <summary>
        /// Renders dose overlay with selectable display mode.
        /// 
        /// Line mode (Eclipse default): contour lines at isodose thresholds.
        /// Fill mode: solid color fill between levels.
        /// Colorwash mode: continuous jet colormap gradient.
        /// 
        /// When eqd2Settings is non-null and enabled, voxel doses are converted to EQD2.
        /// </summary>
        string RenderDoseImage(Image ctImage, Dose dose, WriteableBitmap targetBitmap, int currentSlice,
            double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels,
            DoseDisplayMode displayMode = DoseDisplayMode.Line,
            double colorwashOpacity = 0.5, double colorwashMinPercent = 0.1,
            EQD2Settings eqd2Settings = null);

        /// <summary>
        /// Gets the physical dose in Gy at a specific CT pixel coordinate on the current slice.
        /// Returns NaN if out of dose grid bounds.
        /// </summary>
        double GetDoseAtPixel(Image ctImage, Dose dose, int currentSlice, int pixelX, int pixelY,
            EQD2Settings eqd2Settings = null);
    }
}