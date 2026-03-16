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
        /// Renders dose overlay. When eqd2Settings is non-null and enabled,
        /// each voxel's physical dose is converted to EQD2 before threshold comparison.
        /// The reference dose is also converted to EQD2 so percentage-based isodose levels remain meaningful.
        /// </summary>
        string RenderDoseImage(Image ctImage, Dose dose, WriteableBitmap targetBitmap, int currentSlice,
            double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels,
            EQD2Settings eqd2Settings = null);
    }
}
