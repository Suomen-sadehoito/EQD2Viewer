namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Configuration for EQD2 isodose rendering (voxel-level).
    /// Uses a single global α/β because individual voxels don't belong to specific structures.
    /// </summary>
    public class EQD2Settings
    {
        /// <summary>
        /// Whether EQD2 conversion is active. When false, physical dose is displayed.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Global α/β ratio (Gy) used for isodose rendering.
        /// Typical values: 10.0 for tumor, 3.0 for late-responding normal tissue.
        /// </summary>
        public double AlphaBeta { get; set; } = 3.0;

        /// <summary>
        /// Number of treatment fractions. Read from PlanSetup but can be overridden
        /// (important for re-irradiation scenarios).
        /// </summary>
        public int NumberOfFractions { get; set; } = 1;
    }
}
