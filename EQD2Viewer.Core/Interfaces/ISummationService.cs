using EQD2Viewer.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EQD2Viewer.Core.Interfaces
{
    /// <summary>
    /// Two-phase dose summation service for multi-plan re-irradiation assessment.
    ///
    /// Architecture:
    ///   Phase 1 (UI thread):  Load plan data through ISummationDataLoader into plain arrays.
    ///   Phase 2 (any thread): Accumulate per-plan physical dose + compute EQD2 display sum.
    ///
    /// After Phase 2 completes, the service retains per-plan physical dose arrays,
    /// enabling:
    ///   - Fast EQD2 recomputation with a different display alpha/beta (no data reloading).
    ///   - Per-structure DVH calculation with structure-specific alpha/beta values.
    /// </summary>
    public interface ISummationService : IDisposable
    {
        /// <summary>
        /// Phase 1: Load plan data into plain arrays via ISummationDataLoader.
        /// MUST run on UI thread when using ESAPI data loader.
        /// </summary>
        SummationResult PrepareData(SummationConfig config);

        /// <summary>
        /// Phase 2: Heavy voxel computation. Runs on ANY thread (no ESAPI calls).
        /// Stores per-plan physical dose AND computes EQD2 display sum.
        /// </summary>
        Task<SummationResult> ComputeAsync(IProgress<int>? progress, CancellationToken ct);

        /// <summary>
        /// Recomputes the EQD2 display sum from stored per-plan physical doses.
        /// Much faster than full re-summation -- skips Phase 1 entirely.
        /// Called when the user changes the display alpha/beta slider.
        /// </summary>
        Task<SummationResult> RecomputeEQD2DisplayAsync(double displayAlphaBeta,
     IProgress<int>? progress, CancellationToken ct);

        /// <summary>
        /// Computes the cumulative DVH and exact voxel statistics of one structure on the
        /// summed dose: EQD2 with the structure's own alpha/beta when the summation method
        /// is EQD2, plain physical sum otherwise. The curve uses fixed
        /// <see cref="DomainConstants.DvhSamplingResolution"/>-wide bins sized to the
        /// structure's own maximum; statistics come straight from the voxels, never from
        /// the curve. Returns an empty result for an unknown structure or before compute.
        /// </summary>
        StructureDvhResult ComputeStructureDVH(string structureId, double structureAlphaBeta);

        /// <summary>
        /// Same as <see cref="ComputeStructureDVH"/> but for one plan's own contribution to
        /// the sum (its physical dose resampled on the reference grid, converted with its own
        /// fractionation at the structure's alpha/beta, times its weight). The plan is
        /// identified by <see cref="SummationPlanEntry.DisplayLabel"/>. Returns an empty
        /// result for an unknown plan or structure.
        /// </summary>
        StructureDvhResult ComputeStructurePlanDVH(string planDisplayLabel, string structureId, double structureAlphaBeta);

        bool HasSummedDose { get; }

        /// <summary>The EQD2 (or physical) display sum on one reference CT slice, or null before compute.</summary>
        double[]? GetSummedSlice(int sliceIndex);
        double SummedReferenceDoseGy { get; }

        /// <summary>Returns the secondary plan's CT voxels mapped onto the reference CT grid.</summary>
        int[]? GetRegisteredCtSlice(string planDisplayLabel, int sliceIndex);

        /// <summary>Gets all structure IDs that have cached masks.</summary>
        IReadOnlyList<string> GetCachedStructureIds();

        /// <summary>Gets the voxel volume in cm^3 for the reference CT grid.</summary>
        double GetVoxelVolumeCc();
    }

    public class SummationResult
    {
        public bool Success { get; set; }
        public string StatusMessage { get; set; } = "";
        public double MaxDoseGy { get; set; }
        public double TotalReferenceDoseGy { get; set; }

        /// <summary>Voxel index (on the reference CT grid) where MaxDoseGy was found.
        /// Used by the UI to jump to the hotspot slice.</summary>
        public int MaxDoseSliceZ { get; set; }
        public int MaxDosePixelX { get; set; }
        public int MaxDosePixelY { get; set; }
    }
}
