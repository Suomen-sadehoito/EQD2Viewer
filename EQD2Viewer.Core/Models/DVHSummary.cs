namespace EQD2Viewer.Core.Models
{
    public class DVHSummary
    {
        public string StructureId { get; set; } = "";
        public string PlanId { get; set; } = "";
        public string Type { get; set; } = "";
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
