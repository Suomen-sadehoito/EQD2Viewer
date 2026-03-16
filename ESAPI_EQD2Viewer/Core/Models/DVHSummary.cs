namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Summary row for the DVH statistics table.
    /// Each structure has one row for physical dose and optionally one for EQD2.
    /// </summary>
    public class DVHSummary
    {
        public string StructureId { get; set; }
        public string PlanId { get; set; }

        /// <summary>
        /// "Physical" or "EQD2"
        /// </summary>
        public string Type { get; set; }

        public double DMax { get; set; }
        public double DMean { get; set; }
        public double DMin { get; set; }

        /// <summary>
        /// Structure volume in cm³
        /// </summary>
        public double Volume { get; set; }
    }
}
