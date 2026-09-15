namespace EQD2Viewer.Core.Models
{
    /// <summary>
    /// Dose statistics of one structure computed directly from its voxel values —
    /// never read back from a binned DVH curve, so histogram resolution and bin
    /// bookkeeping cannot influence them.
    ///
    /// Every masked voxel counts, including voxels that received no dose (for
    /// example the part of a structure that lies outside a plan's dose grid).
    /// Such voxels contribute 0 Gy, which is why <see cref="DMinGy"/> is 0 for a
    /// structure that is not fully covered by the dose grid.
    /// </summary>
    public sealed class DvhStatistics
    {
        public static readonly DvhStatistics Empty = new DvhStatistics(0, 0, 0, 0);

        public DvhStatistics(double dMaxGy, double dMeanGy, double dMinGy, long voxelCount)
        {
            DMaxGy = dMaxGy;
            DMeanGy = dMeanGy;
            DMinGy = dMinGy;
            VoxelCount = voxelCount;
        }

        public double DMaxGy { get; }
        public double DMeanGy { get; }
        public double DMinGy { get; }

        /// <summary>Number of voxels inside the structure mask (all of them, dosed or not).</summary>
        public long VoxelCount { get; }
    }
}
