using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Models;

namespace EQD2Viewer.Core.Interfaces
{
    /// <summary>
    /// Builds Dose Statistics rows for a single plan from the DVH curve Eclipse computed
    /// for it (physical, and EQD2-converted at a given fractionation and α/β).
    ///
    /// Summation ("Σ") rows do not come through here: they are computed from summed dose
    /// voxels by <see cref="ISummationService.ComputeStructureDVH"/> and
    /// <see cref="ISummationService.ComputeStructurePlanDVH"/>.
    /// </summary>
    public interface IDVHCalculation
    {
        /// <summary>
        /// Builds a physical dose summary from a pre-computed DVH curve.
        /// </summary>
        DVHSummary BuildPhysicalSummaryFromCurve(DvhCurveData dvh, string planId);

        /// <summary>
        /// Builds an EQD2-converted summary from a pre-computed DVH curve. Dmean is the
        /// volume-weighted mean of the EQD2-converted curve (each dose level converted
        /// separately), never EQD2 of the physical mean — the two differ whenever the
        /// dose is heterogeneous, and the latter underestimates for OARs.
        /// </summary>
        DVHSummary BuildEQD2SummaryFromCurve(DvhCurveData dvh, string planId,
            int numberOfFractions, double alphaBeta);
    }
}
