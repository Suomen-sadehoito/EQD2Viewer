using System;

namespace EQD2Viewer.Core.Models
{
    /// <summary>
    /// Result of a per-structure DVH computation on summed dose: the cumulative
    /// curve for plotting plus exact voxel statistics for the summary table.
    /// </summary>
    public sealed class StructureDvhResult
    {
        public StructureDvhResult(string structureId, DoseVolumePoint[] curve, DvhStatistics statistics)
        {
            StructureId = structureId ?? "";
            Curve = curve ?? Array.Empty<DoseVolumePoint>();
            Statistics = statistics ?? DvhStatistics.Empty;
        }

        public static StructureDvhResult Empty(string structureId)
            => new StructureDvhResult(structureId, Array.Empty<DoseVolumePoint>(), DvhStatistics.Empty);

        public string StructureId { get; }

        /// <summary>Cumulative DVH: (dose Gy, percent of structure volume receiving at least that dose).</summary>
        public DoseVolumePoint[] Curve { get; }

        /// <summary>Exact statistics computed from the voxel values, independent of the curve binning.</summary>
        public DvhStatistics Statistics { get; }

        /// <summary>True when the structure had no mask or no voxels — nothing to show.</summary>
        public bool IsEmpty => Curve.Length == 0 || Statistics.VoxelCount == 0;
    }
}
