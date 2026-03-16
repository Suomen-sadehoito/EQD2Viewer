using System;
using System.Windows;
using System.Windows.Media.Imaging;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_IsodoseViewer.Core.Interfaces;
using ESAPI_IsodoseViewer.Core.Extensions;
using ESAPI_IsodoseViewer.Core.Models;

namespace ESAPI_IsodoseViewer.Services
{
    public class ImageRenderingService : IImageRenderingService
    {
        private int _width;
        private int _height;

        // Caches for storing all slices in memory
        private int[][,] _ctCache;
        private int[][,] _doseCache;

        // Pre-calculated dose scaling factors (computed once in PreloadData)
        private double _doseRawScale;
        private double _doseRawOffset;
        private double _doseUnitToGyFactor;
        private bool _doseScalingReady;

        // HU offset determined during preload from statistical analysis
        private int _huOffset;

        private bool _disposed;

        public void Initialize(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

            _width = width;
            _height = height;
        }

        public void PreloadData(Image ctImage, Dose dose, double prescriptionDoseGy)
        {
            if (ctImage != null)
            {
                _ctCache = new int[ctImage.ZSize][,];
                for (int z = 0; z < ctImage.ZSize; z++)
                {
                    _ctCache[z] = new int[ctImage.XSize, ctImage.YSize];
                    ctImage.GetVoxels(z, _ctCache[z]);
                }

                // Determine HU offset from the middle slice using statistical sampling
                _huOffset = DetermineHuOffset(ctImage);
            }

            if (dose != null)
            {
                _doseCache = new int[dose.ZSize][,];
                for (int z = 0; z < dose.ZSize; z++)
                {
                    _doseCache[z] = new int[dose.XSize, dose.YSize];
                    dose.GetVoxels(z, _doseCache[z]);
                }

                // Pre-calculate dose scaling factors ONCE
                DoseValue dv0 = dose.VoxelToDoseValue(0);
                DoseValue dvRef = dose.VoxelToDoseValue(10000);

                _doseRawScale = (dvRef.Dose - dv0.Dose) / 10000.0;
                _doseRawOffset = dv0.Dose;

                if (dvRef.Unit == DoseValue.DoseUnit.Percent)
                    _doseUnitToGyFactor = prescriptionDoseGy / 100.0;
                else if (dvRef.Unit == DoseValue.DoseUnit.cGy)
                    _doseUnitToGyFactor = 0.01;
                else
                    _doseUnitToGyFactor = 1.0; // Gy

                _doseScalingReady = true;
            }
        }

        /// <summary>
        /// Determines the HU offset by sampling the middle slice.
        /// If the majority of sampled voxels are above 30000, the data is unsigned (offset = 32768).
        /// </summary>
        private int DetermineHuOffset(Image ctImage)
        {
            int midSlice = ctImage.ZSize / 2;
            if (_ctCache == null || midSlice < 0 || midSlice >= _ctCache.Length)
                return 0;

            int[,] slice = _ctCache[midSlice];
            int xSize = ctImage.XSize;
            int ySize = ctImage.YSize;

            // Sample every 8th pixel in both directions for speed
            int step = 8;
            int countAboveThreshold = 0;
            int totalSamples = 0;

            for (int y = 0; y < ySize; y += step)
            {
                for (int x = 0; x < xSize; x += step)
                {
                    totalSamples++;
                    if (slice[x, y] > 30000)
                        countAboveThreshold++;
                }
            }

            // If more than 50% of samples are above 30000, data is unsigned
            return (totalSamples > 0 && countAboveThreshold > totalSamples / 2) ? 32768 : 0;
        }

        public unsafe void RenderCtImage(Image ctImage, WriteableBitmap targetBitmap, int currentSlice,
            double windowLevel, double windowWidth)
        {
            if (_ctCache == null || currentSlice < 0 || currentSlice >= _ctCache.Length)
                return;

            int[,] currentCtSlice = _ctCache[currentSlice];

            // Validate that cached dimensions match expected dimensions
            int sliceWidth = currentCtSlice.GetLength(0);
            int sliceHeight = currentCtSlice.GetLength(1);
            if (sliceWidth != _width || sliceHeight != _height)
                return;

            targetBitmap.Lock();
            try
            {
                byte* pBackBuffer = (byte*)targetBitmap.BackBuffer;
                int stride = targetBitmap.BackBufferStride;

                double huMin = windowLevel - (windowWidth / 2.0);
                double factor = (windowWidth > 0) ? 255.0 / windowWidth : 0;
                int huOffset = _huOffset;

                for (int y = 0; y < _height; y++)
                {
                    uint* pRow = (uint*)(pBackBuffer + y * stride);
                    for (int x = 0; x < _width; x++)
                    {
                        int hu = currentCtSlice[x, y] - huOffset;
                        double valDouble = (hu - huMin) * factor;
                        byte val = (byte)(valDouble < 0 ? 0 : (valDouble > 255 ? 255 : valDouble));

                        // BGRA format
                        pRow[x] = (0xFFu << 24) | ((uint)val << 16) | ((uint)val << 8) | val;
                    }
                }

                targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            }
            finally
            {
                targetBitmap.Unlock();
            }
        }

        public unsafe string RenderDoseImage(Image ctImage, Dose dose, WriteableBitmap targetBitmap,
            int currentSlice, double planTotalDoseGy, double planNormalization, IsodoseLevel[] levels)
        {
            targetBitmap.Lock();
            try
            {
                int doseStride = targetBitmap.BackBufferStride;
                byte* pDoseBuffer = (byte*)targetBitmap.BackBuffer;

                // Clear dose buffer
                for (int i = 0; i < _height * doseStride; i++)
                    pDoseBuffer[i] = 0;

                if (dose == null || _doseCache == null || !_doseScalingReady || levels == null || levels.Length == 0)
                {
                    targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    return "No dose available.";
                }

                double prescriptionGy = planTotalDoseGy;
                double normalization = planNormalization;
                if (double.IsNaN(normalization) || normalization <= 0)
                    normalization = 100.0;
                else if (normalization < 5.0)
                    normalization *= 100.0;

                double referenceDoseGy = prescriptionGy * (normalization / 100.0);
                if (referenceDoseGy < 0.1)
                    referenceDoseGy = prescriptionGy;

                // Dose Z lookup
                VVector ctPlaneCenterWorld = ctImage.Origin + ctImage.ZDirection * (currentSlice * ctImage.ZRes);
                VVector relativeToDoseOrigin = ctPlaneCenterWorld - dose.Origin;
                int doseSlice = (int)Math.Round(relativeToDoseOrigin.Dot(dose.ZDirection) / dose.ZRes);

                if (doseSlice < 0 || doseSlice >= dose.ZSize)
                {
                    targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    return $"CT Z: {currentSlice} | Dose Z: {doseSlice} (Out of range)";
                }

                int dx = dose.XSize;
                int dy = dose.YSize;
                int[,] doseBuffer = _doseCache[doseSlice];

                // Use pre-calculated scaling factors
                double rawScale = _doseRawScale;
                double rawOffset = _doseRawOffset;
                double unitToGyFactor = _doseUnitToGyFactor;

                double maxDoseInSlice = 0;

                for (int y = 0; y < dy; y++)
                {
                    for (int x = 0; x < dx; x++)
                    {
                        double valInUnits = doseBuffer[x, y] * rawScale + rawOffset;
                        double dGy = valInUnits * unitToGyFactor;
                        if (dGy > maxDoseInSlice) maxDoseInSlice = dGy;

                        uint color = 0;
                        for (int i = 0; i < levels.Length; i++)
                        {
                            if (dGy >= referenceDoseGy * levels[i].Fraction)
                            {
                                color = (levels[i].Color & 0x00FFFFFF) | ((uint)levels[i].Alpha << 24);
                                break;
                            }
                        }

                        if (color != 0)
                        {
                            VVector worldPos = dose.Origin +
                                               dose.XDirection * (x * dose.XRes) +
                                               dose.YDirection * (y * dose.YRes) +
                                               dose.ZDirection * (doseSlice * dose.ZRes);

                            VVector diff = worldPos - ctImage.Origin;
                            double px = diff.Dot(ctImage.XDirection) / ctImage.XRes;
                            double py = diff.Dot(ctImage.YDirection) / ctImage.YRes;

                            double scaleX = dose.XRes / ctImage.XRes;
                            double scaleY = dose.YRes / ctImage.YRes;

                            int startX = (int)(px - scaleX / 2.0);
                            int startY = (int)(py - scaleY / 2.0);
                            int endX = (int)(px + scaleX / 2.0);
                            int endY = (int)(py + scaleY / 2.0);

                            // Clamp to bitmap bounds
                            if (startX < 0) startX = 0;
                            if (startY < 0) startY = 0;
                            if (endX > _width) endX = _width;
                            if (endY > _height) endY = _height;

                            for (int pyImg = startY; pyImg < endY; pyImg++)
                            {
                                uint* row = (uint*)(pDoseBuffer + pyImg * doseStride);
                                for (int pxImg = startX; pxImg < endX; pxImg++)
                                {
                                    row[pxImg] = color;
                                }
                            }
                        }
                    }
                }

                targetBitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                return $"CT Z: {currentSlice} | Dose Z: {doseSlice} | Max: {maxDoseInSlice:F2} Gy | Ref: {referenceDoseGy:F2} Gy";
            }
            finally
            {
                targetBitmap.Unlock();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ctCache = null;
            _doseCache = null;
        }
    }
}
