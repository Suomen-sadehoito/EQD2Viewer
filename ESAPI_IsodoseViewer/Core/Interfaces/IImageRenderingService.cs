using System;
using System.Windows.Media.Imaging;
using VMS.TPS.Common.Model.API;
using ESAPI_IsodoseViewer.Core.Models;

namespace ESAPI_IsodoseViewer.Core.Interfaces
{
    public interface IImageRenderingService : IDisposable
    {
        void Initialize(int width, int height);

        /// <summary>
        /// Preloads CT and dose voxel data into memory caches.
        /// Also pre-calculates dose scaling factors (offset, scale, unitToGyFactor).
        /// </summary>
        void PreloadData(Image ctImage, Dose dose, double prescriptionDoseGy);

        void RenderCtImage(Image ctImage, WriteableBitmap targetBitmap, int currentSlice, double windowLevel, double windowWidth);

        string RenderDoseImage(Image ctImage, Dose dose, WriteableBitmap targetBitmap, int currentSlice,
            double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels);
    }
}