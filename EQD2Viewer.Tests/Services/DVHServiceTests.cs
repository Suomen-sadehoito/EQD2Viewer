using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Services;
using FluentAssertions;
using System.Linq;

namespace EQD2Viewer.Tests.Services
{
    /// <summary>
    /// Tests for DVHService methods that don't require ESAPI objects.
    /// Covers DVH histogram computation from summed dose arrays and summary building from curves.
    /// </summary>
    public class DVHServiceTests
    {
        private readonly DVHService _service = new DVHService();

        // ════════════════════════════════════════════════════════
        // CalculateDVHFromSummedDose
        // ════════════════════════════════════════════════════════

        [Fact]
        public void CalculateDVHFromSummedDose_UniformDose_ShouldGiveStepFunction()
        {
            // All voxels = 10 Gy → DVH should be 100% up to 10 Gy, then drop to 0%
            int w = 10, h = 10;
            double[] doseSlice = Enumerable.Repeat(10.0, w * h).ToArray();
            bool[] mask = Enumerable.Repeat(true, w * h).ToArray();

            var summedSlices = new double[][] { doseSlice };
            var structureMasks = new bool[][] { mask };

            var result = _service.CalculateDVHFromSummedDose(summedSlices, structureMasks, 0.001, 20.0);

            result.Should().NotBeEmpty();
            // Volume at 0 Gy should be 100%
            result[0].VolumePercent.Should().BeApproximately(100.0, 0.1);
            // Volume well above 10 Gy should be ~0%
            var pointAbove = result.FirstOrDefault(p => p.DoseGy > 11.0);
            if (pointAbove != null)
                pointAbove.VolumePercent.Should().BeLessThan(1.0);
        }

        [Fact]
        public void CalculateDVHFromSummedDose_ZeroDose_ShouldHandleCorrectly()
        {
            int size = 25;
            double[] doseSlice = new double[size]; // all zeros
            bool[] mask = Enumerable.Repeat(true, size).ToArray();

            var result = _service.CalculateDVHFromSummedDose(
                new double[][] { doseSlice }, new bool[][] { mask }, 0.001, 10.0);

            result.Should().NotBeEmpty();
            result[0].VolumePercent.Should().Be(100.0);
        }

        [Fact]
        public void CalculateDVHFromSummedDose_NoMaskedVoxels_ShouldReturnEmpty()
        {
            double[] doseSlice = Enumerable.Repeat(10.0, 25).ToArray();
            bool[] mask = new bool[25]; // all false

            var result = _service.CalculateDVHFromSummedDose(
                new double[][] { doseSlice }, new bool[][] { mask }, 0.001, 20.0);

            result.Should().BeEmpty("no voxels inside structure");
        }

        [Fact]
        public void CalculateDVHFromSummedDose_NullInput_ShouldReturnEmpty()
        {
            _service.CalculateDVHFromSummedDose(null!, null!, 1.0, 10.0).Should().BeEmpty();
        }

        [Fact]
        public void CalculateDVHFromSummedDose_ZeroMaxDose_ShouldReturnEmpty()
        {
            double[] doseSlice = Enumerable.Repeat(10.0, 25).ToArray();
            bool[] mask = Enumerable.Repeat(true, 25).ToArray();

            var result = _service.CalculateDVHFromSummedDose(
                new double[][] { doseSlice }, new bool[][] { mask }, 0.001, 0);

            result.Should().BeEmpty();
        }

        [Fact]
        public void CalculateDVHFromSummedDose_DVH_ShouldBeMonotonicallyDecreasing()
        {
            // Random-ish dose distribution
            int size = 100;
            double[] doseSlice = new double[size];
            bool[] mask = Enumerable.Repeat(true, size).ToArray();
            var rng = new Random(42);
            for (int i = 0; i < size; i++) doseSlice[i] = rng.NextDouble() * 50;

            var result = _service.CalculateDVHFromSummedDose(
                new double[][] { doseSlice }, new bool[][] { mask }, 0.001, 60.0);

            for (int i = 1; i < result.Length; i++)
                result[i].VolumePercent.Should().BeLessOrEqualTo(result[i - 1].VolumePercent,
                    $"cumulative DVH must be monotonically non-increasing at bin {i}");
        }

        [Fact]
        public void CalculateDVHFromSummedDose_MultipleSlices_ShouldCombineCorrectly()
        {
            // 2 slices, each with 10 voxels = 20 total
            double[] slice1 = Enumerable.Repeat(5.0, 10).ToArray();
            double[] slice2 = Enumerable.Repeat(15.0, 10).ToArray();
            bool[] mask1 = Enumerable.Repeat(true, 10).ToArray();
            bool[] mask2 = Enumerable.Repeat(true, 10).ToArray();

            var result = _service.CalculateDVHFromSummedDose(
                new double[][] { slice1, slice2 },
                new bool[][] { mask1, mask2 },
                0.001, 20.0);

            result.Should().NotBeEmpty();
            // At 0 Gy: 100% (all 20 voxels)
            result[0].VolumePercent.Should().BeApproximately(100.0, 0.1);
            // At 10 Gy: 50% (only slice2's 10 voxels ≥ 10)
            var mid = result.FirstOrDefault(p => p.DoseGy > 10.0 && p.DoseGy < 11.0);
            if (mid != null)
                mid.VolumePercent.Should().BeApproximately(50.0, 5.0);
        }

        // ════════════════════════════════════════════════════════
        // BuildSummaryFromCurve
        // ════════════════════════════════════════════════════════

        [Fact]
        public void BuildSummaryFromCurve_KnownCurve_ShouldExtractCorrectStatistics()
        {
            // Simple step curve: 100% at 0 Gy, drops to 0% at 10 Gy
            var curve = new DoseVolumePoint[]
            {
                new DoseVolumePoint(0.0, 100.0),
                new DoseVolumePoint(5.0, 100.0),
                new DoseVolumePoint(10.0, 50.0),
                new DoseVolumePoint(15.0, 0.0),
            };

            var summary = _service.BuildSummaryFromCurve("TestStruct", "Plan1", "EQD2", curve, 100.0);

            summary.StructureId.Should().Be("TestStruct");
            summary.DMax.Should().Be(10.0, "last bin with volume > 0.01%");
            summary.Volume.Should().Be(100.0);
        }

        [Fact]
        public void BuildSummaryFromCurve_NullCurve_ShouldReturnEmptySummary()
        {
            var summary = _service.BuildSummaryFromCurve("Test", "P1", "Phys", null!, 50.0);
            summary.DMax.Should().Be(0);
            summary.DMean.Should().Be(0);
        }

        [Fact]
        public void BuildSummaryFromCurve_EmptyCurve_ShouldReturnEmptySummary()
        {
            var summary = _service.BuildSummaryFromCurve("Test", "P1", "Phys",
                new DoseVolumePoint[0], 50.0);
            summary.DMax.Should().Be(0);
        }

        [Fact]
        public void BuildSummaryFromCurve_DMean_ShouldBeReasonable()
        {
            // Linear drop: 100% at 0 Gy to 0% at 20 Gy → mean ≈ 10 Gy
            var curve = new DoseVolumePoint[21];
            for (int i = 0; i <= 20; i++)
                curve[i] = new DoseVolumePoint(i, 100.0 - i * 5.0);

            var summary = _service.BuildSummaryFromCurve("S", "P", "T", curve, 100.0);
            summary.DMean.Should().BeInRange(5.0, 15.0,
                "mean of linear distribution should be near center");
        }

        // ════════════════════════════════════════════════════════
        // Eclipse-curve summaries
        // ════════════════════════════════════════════════════════

        [Fact]
        public void BuildEQD2SummaryFromCurve_HeterogeneousDose_DmeanIsMeanOfEQD2NotEQD2OfMean()
        {
            // Half the structure at 10 Gy, half at 20 Gy, 5 fractions, α/β = 3.
            //   EQD2(10 Gy, 2 Gy/fx)  = 10 · (2 + 3) / 5 = 10 Gy
            //   EQD2(20 Gy, 4 Gy/fx)  = 20 · (4 + 3) / 5 = 28 Gy   → mean of EQD2 = 19 Gy
            //   EQD2(mean 15 Gy, 3 Gy/fx) = 15 · (3 + 3) / 5 = 18 Gy  ← the old "Simple" method
            // EQD2 is convex in dose, so EQD2-of-mean always underestimates for an OAR.
            var dvh = new DvhCurveData
            {
                StructureId = "OAR",
                DMaxGy = 20,
                DMeanGy = 15,
                DMinGy = 10,
                VolumeCc = 12.5,
                Curve = new[]
                {
                    new[] { 0.0, 100.0 }, new[] { 10.0, 100.0 }, new[] { 10.01, 50.0 },
                    new[] { 20.0, 50.0 }, new[] { 20.01, 0.0 }
                }
            };

            var summary = _service.BuildEQD2SummaryFromCurve(dvh, "Plan1", numberOfFractions: 5, alphaBeta: 3.0);

            summary.DMean.Should().BeApproximately(19.0, 0.05);
            summary.DMean.Should().BeGreaterThan(18.05, "EQD2 of the physical mean would give 18 Gy");
            summary.DMax.Should().BeApproximately(28.0, 1e-9);
            summary.DMin.Should().BeApproximately(10.0, 1e-9);
            summary.Type.Should().Be("EQD2");
            summary.Source.Should().Be(DVHSummary.SourceEclipse);
            summary.Volume.Should().Be(12.5);
        }

        [Fact]
        public void BuildEQD2SummaryFromCurve_NoCurve_FallsBackToEQD2OfMean()
        {
            var dvh = new DvhCurveData { StructureId = "S", DMaxGy = 20, DMeanGy = 15, DMinGy = 10, Curve = null! };

            var summary = _service.BuildEQD2SummaryFromCurve(dvh, "Plan1", numberOfFractions: 5, alphaBeta: 3.0);

            summary.DMean.Should().BeApproximately(18.0, 1e-9, "without curve data only the physical mean is available");
        }

        [Fact]
        public void BuildPhysicalSummaryFromCurve_IsLabelledAsEclipseSource()
        {
            var dvh = new DvhCurveData { StructureId = "S", DMaxGy = 20, DMeanGy = 15, DMinGy = 10, VolumeCc = 3 };

            var summary = _service.BuildPhysicalSummaryFromCurve(dvh, "Plan1");

            summary.Type.Should().Be("Physical");
            summary.Source.Should().Be(DVHSummary.SourceEclipse);
            summary.IsSummation.Should().BeFalse();
            summary.DMean.Should().Be(15);
        }

        /// <summary>
        /// Regression for the phantom-Dmax bug, end to end through the legacy binner and
        /// the curve-based summary: 7 voxels at 25.6 Gy, 3 voxels at 0 Gy (structure partly
        /// outside the dose grid), global summed maximum 76 Gy. Before the fix the curve never
        /// decayed below 30 %, so BuildSummaryFromCurve reported the top of the histogram —
        /// bin 999 × 1.1 × 76 / 1000 = 83.5 Gy — as Dmax, regardless of the structure's dose.
        /// </summary>
        [Fact]
        public void CalculateDVHFromSummedDose_StructureWithZeroDoseVoxels_DmaxIsTrueMaxNotHistogramEdge()
        {
            var dose = new double[10];
            for (int i = 0; i < 7; i++) dose[i] = 25.6;
            var mask = Enumerable.Repeat(true, 10).ToArray();

            var curve = _service.CalculateDVHFromSummedDose(new[] { dose }, new[] { mask }, 0.001, 76.0);
            var summary = _service.BuildSummaryFromCurve("SpinalCanal", "Summation", "EQD2", curve, 10 * 0.001);

            double binWidth = 76.0 * 1.1 / DomainConstants.DvhHistogramBins;
            summary.DMax.Should().BeApproximately(25.6, binWidth + 1e-9,
                "Dmax must be the bin holding the true maximum, not the histogram edge");
            summary.DMax.Should().BeLessThan(30.0, "83.5 Gy was the histogram edge, not a dose");
            summary.DMin.Should().Be(0.0, "part of the structure receives no dose");
            curve.Last().VolumePercent.Should().Be(0.0);
        }
    }
}