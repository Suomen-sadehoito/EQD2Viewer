namespace EQD2Viewer.Core.Models
{
    public class DVHSummary
    {
        /// <summary>Row built from the DVH curve Eclipse computed for a single plan.</summary>
        public const string SourceEclipse = "Eclipse DVH";

        /// <summary>Row computed by the viewer from summed dose voxels on the reference CT grid.</summary>
        public const string SourceVoxelSum = "Voxel sum";

        public string StructureId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public string Type { get; set; } = "";

        /// <summary>
        /// Where the numbers come from: <see cref="SourceEclipse"/> or <see cref="SourceVoxelSum"/>.
        /// Shown in the table so the two engines are never confused with each other.
        /// </summary>
        public string Source { get; set; } = "";
        public double DMax { get; set; }
        public double DMean { get; set; }
        public double DMin { get; set; }
        public double Volume { get; set; }

        /// <summary>
        /// True for rows produced by plan summation (the Σ total and the per-plan Σ rows),
        /// as opposed to rows built from a single plan's Eclipse DVH curve.
        /// </summary>
        public bool IsSummation { get; set; }
    }
}
