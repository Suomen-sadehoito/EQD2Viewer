namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Dose overlay display modes, matching Varian Eclipse behavior.
    /// </summary>
    public enum DoseDisplayMode
    {
        /// <summary>
        /// Eclipse default: thin contour lines at each isodose threshold.
        /// Only boundary voxels (where dose crosses a threshold) are drawn.
        /// </summary>
        Line,

        /// <summary>
        /// Solid color fill between isodose levels (original behavior).
        /// Each voxel is colored by its highest matching isodose level.
        /// </summary>
        Fill,

        /// <summary>
        /// Continuous color gradient (jet/rainbow colormap).
        /// Dose is mapped to a smooth color ramp from blue (low) to red (high).
        /// </summary>
        Colorwash
    }
}