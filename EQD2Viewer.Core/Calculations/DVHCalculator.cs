using EQD2Viewer.Core.Models;
using System;

namespace EQD2Viewer.Core.Calculations
{
    /// <summary>
    /// Pure-function DVH utilities shared by every consumer.
    ///
    /// Cumulative-DVH semantics: a point (D, V) means "V percent of the structure's
    /// voxels receive at least D Gy". Every masked voxel — including voxels with zero
    /// or negative dose, such as the part of a structure that lies outside a plan's
    /// dose grid — is placed in exactly one histogram bin (bin 0 for dose ≤ 0), so
    /// the curve starts at 100% and always decays to 0% above the true maximum.
    ///
    /// Two binning conventions:
    ///   * <see cref="BinToHistogram"/>: relative bins. numBins =
    ///     <see cref="DomainConstants.DvhHistogramBins"/>, binWidth = maxDoseGy * 1.1 / numBins,
    ///     bin index = floor(dose / binWidth) clamped to numBins - 1.
    ///   * <see cref="ComputeCumulative"/>: fixed-width bins (typically
    ///     <see cref="DomainConstants.DvhSamplingResolution"/>, the same resolution the
    ///     viewer requests from Eclipse for single-plan curves) sized to the structure's
    ///     own maximum, plus exact voxel statistics from the same pass over the data.
    /// </summary>
    public static class DVHCalculator
    {
        /// <summary>
        /// Cumulative-DVH histogram for a structure. Walks every (slice, voxel)
        /// pair where <paramref name="structureMasks"/>[z][i] is true, accumulates
        /// the corresponding dose from <paramref name="doseSlices"/>[z][i] into
        /// the histogram, and returns one <see cref="DoseVolumePoint"/> per bin.
        ///
        /// Returns an empty array when:
        ///   * either input is null;
        ///   * <paramref name="maxDoseGy"/> ≤ 0;
        ///   * no voxel inside the structure mask was found.
        /// </summary>
        public static DoseVolumePoint[] BinToHistogram(
            double[][] doseSlices, bool[][] structureMasks, double maxDoseGy)
        {
            if (doseSlices == null || structureMasks == null || maxDoseGy <= 0)
                return Array.Empty<DoseVolumePoint>();

            int numBins = DomainConstants.DvhHistogramBins;
            double binWidth = maxDoseGy * 1.1 / numBins;
            long[] histogram = new long[numBins];
            long totalVoxels = Accumulate(doseSlices, structureMasks, binWidth, histogram);

            if (totalVoxels == 0) return Array.Empty<DoseVolumePoint>();
            return ToCumulative(histogram, binWidth, totalVoxels);
        }

        /// <summary>
        /// Cumulative DVH with fixed-width bins of <paramref name="binWidthGy"/>, sized to
        /// the structure's own maximum so no voxel is ever clamped into a last bin, plus
        /// exact <see cref="DvhStatistics"/> (max, mean, min, voxel count) read straight
        /// from the voxel values.
        ///
        /// The last point of the curve is always 0%: the bin above floor(max / width)
        /// holds no voxel. Returns an empty array (and <see cref="DvhStatistics.Empty"/>)
        /// when an input is null, the bin width is not positive, or the mask is empty.
        /// </summary>
        public static DoseVolumePoint[] ComputeCumulative(
            double[][] doseSlices, bool[][] structureMasks, double binWidthGy,
            out DvhStatistics statistics)
        {
            statistics = DvhStatistics.Empty;
            if (doseSlices == null || structureMasks == null || binWidthGy <= 0
                || double.IsNaN(binWidthGy) || double.IsInfinity(binWidthGy))
                return Array.Empty<DoseVolumePoint>();

            var stats = ComputeStatistics(doseSlices, structureMasks);
            if (stats.VoxelCount == 0) return Array.Empty<DoseVolumePoint>();
            statistics = stats;

            // Highest occupied bin is floor(max / width); one more above it guarantees
            // the curve ends at 0%. The cap only matters for absurd inputs and keeps a
            // corrupt dose value from allocating an unbounded histogram.
            double wanted = Math.Floor(stats.DMaxGy / binWidthGy) + 2;
            int numBins = wanted >= DomainConstants.DvhMaxHistogramBins
                ? DomainConstants.DvhMaxHistogramBins
                : (int)wanted;

            long[] histogram = new long[numBins];
            Accumulate(doseSlices, structureMasks, binWidthGy, histogram);
            return ToCumulative(histogram, binWidthGy, stats.VoxelCount);
        }

        /// <summary>
        /// Exact dose statistics over every masked voxel. Dose values that are not
        /// positive (zero, negative from calibration offsets, NaN) count as 0 Gy.
        /// </summary>
        public static DvhStatistics ComputeStatistics(double[][] doseSlices, bool[][] structureMasks)
        {
            if (doseSlices == null || structureMasks == null) return DvhStatistics.Empty;

            long count = 0;
            double max = 0, min = double.MaxValue, sum = 0;
            int sliceCount = Math.Min(doseSlices.Length, structureMasks.Length);

            for (int z = 0; z < sliceCount; z++)
            {
                double[] doseSlice = doseSlices[z];
                bool[] mask = structureMasks[z];
                if (doseSlice == null || mask == null) continue;

                int len = Math.Min(doseSlice.Length, mask.Length);
                for (int i = 0; i < len; i++)
                {
                    if (!mask[i]) continue;
                    double d = doseSlice[i];
                    if (!(d > 0)) d = 0;
                    count++;
                    sum += d;
                    if (d > max) max = d;
                    if (d < min) min = d;
                }
            }

            if (count == 0) return DvhStatistics.Empty;
            return new DvhStatistics(max, sum / count, min, count);
        }

        /// <summary>
        /// Bins every masked voxel: dose ≤ 0 (or NaN) lands in bin 0, dose beyond the
        /// last bin is clamped to it. Returns the number of masked voxels.
        /// </summary>
        private static long Accumulate(double[][] doseSlices, bool[][] structureMasks,
            double binWidth, long[] histogram)
        {
            int numBins = histogram.Length;
            long totalVoxels = 0;
            int sliceCount = Math.Min(doseSlices.Length, structureMasks.Length);

            for (int z = 0; z < sliceCount; z++)
            {
                double[] doseSlice = doseSlices[z];
                bool[] mask = structureMasks[z];
                if (doseSlice == null || mask == null) continue;

                int len = Math.Min(doseSlice.Length, mask.Length);
                for (int i = 0; i < len; i++)
                {
                    if (!mask[i]) continue;
                    totalVoxels++;
                    double d = doseSlice[i];
                    int bin = 0;
                    if (d > 0)
                    {
                        double b = d / binWidth;
                        bin = b >= numBins ? numBins - 1 : (int)b;
                    }
                    histogram[bin]++;
                }
            }
            return totalVoxels;
        }

        private static DoseVolumePoint[] ToCumulative(long[] histogram, double binWidth, long totalVoxels)
        {
            int numBins = histogram.Length;
            var points = new DoseVolumePoint[numBins];
            long cumulative = totalVoxels;
            for (int i = 0; i < numBins; i++)
            {
                points[i] = new DoseVolumePoint(i * binWidth, cumulative * 100.0 / totalVoxels);
                cumulative -= histogram[i];
            }
            return points;
        }
    }
}
