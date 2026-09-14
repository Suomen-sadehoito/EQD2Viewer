using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Models;
using FluentAssertions;
using System.Linq;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Pins the cumulative-DVH conventions of <see cref="DVHCalculator"/>:
    /// <see cref="DVHCalculator.BinToHistogram"/> (relative bins, behind
    /// <c>DVHService.CalculateDVHFromSummedDose</c>) and
    /// <see cref="DVHCalculator.ComputeCumulative"/> (fixed-width bins plus exact
    /// voxel statistics, behind <c>SummationService.ComputeStructureDVH</c>).
    ///
    /// The one invariant both share: every masked voxel is binned, zero-dose voxels
    /// included, so a cumulative curve always decays to 0 % above the true maximum.
    /// </summary>
    public class DVHCalculatorTests
    {
        [Fact]
        public void BinToHistogram_NullInputs_ReturnsEmpty()
        {
            DVHCalculator.BinToHistogram(null!, null!, 10.0).Should().BeEmpty();
        }

        [Fact]
        public void BinToHistogram_ZeroMaxDose_ReturnsEmpty()
        {
            var doses = new double[][] { new[] { 5.0, 5.0 } };
            var masks = new bool[][] { new[] { true, true } };
            DVHCalculator.BinToHistogram(doses, masks, 0).Should().BeEmpty();
        }

        [Fact]
        public void BinToHistogram_NoMaskedVoxels_ReturnsEmpty()
        {
            var doses = new double[][] { new[] { 5.0, 5.0 } };
            var masks = new bool[][] { new[] { false, false } };
            DVHCalculator.BinToHistogram(doses, masks, 10.0).Should().BeEmpty();
        }

        [Fact]
        public void BinToHistogram_AllZeroDose_CurveDropsToZeroAfterBinZero()
        {
            // Zero-dose voxels are real voxels of the structure: they sit in bin 0, so
            // the curve is 100 % at 0 Gy and 0 % from the next bin on. (They used to be
            // counted in the total but never binned, which left the curve stuck at 100 %
            // all the way to the last bin — the root of the phantom-Dmax bug.)
            var doses = new double[][] { new double[10] };
            var masks = new bool[][] { Enumerable.Repeat(true, 10).ToArray() };

            var dvh = DVHCalculator.BinToHistogram(doses, masks, 10.0);

            dvh.Should().NotBeEmpty();
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh[1].VolumePercent.Should().Be(0.0, "no voxel receives ≥ one bin width");
            dvh.Last().VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void BinToHistogram_MixedZeroAndDosedVoxels_CurveReachesZeroAboveTrueMax()
        {
            // Reported clinical case: a spinal canal partly outside the dose grid in a
            // two-plan sum whose global maximum (PTV hotspot) was 76 Gy. Seven voxels at
            // 25.6 Gy, three uncovered voxels at 0 Gy. The curve must be 70 % over
            // (0, 25.6] and 0 % above — not 30 % all the way to 83.5 Gy.
            var dose = new double[10];
            for (int i = 0; i < 7; i++) dose[i] = 25.6;
            var masks = new bool[][] { Enumerable.Repeat(true, 10).ToArray() };

            var dvh = DVHCalculator.BinToHistogram(new[] { dose }, masks, 76.0);

            dvh[0].VolumePercent.Should().Be(100.0);
            dvh.First(p => p.DoseGy > 1.0).VolumePercent.Should().BeApproximately(70.0, 1e-9);
            dvh.First(p => p.DoseGy > 25.7).VolumePercent.Should().Be(0.0);
            dvh.Last().VolumePercent.Should().Be(0.0,
                "zero-dose voxels must be subtracted from the curve like any other voxel");
        }

        [Fact]
        public void BinToHistogram_UniformDose_ProducesStepFunction()
        {
            // 100 voxels at 10 Gy. Curve sits at 100% up through bin 10/binWidth
            // and falls to 0 thereafter. Pin: bin convention is FLOOR.
            int n = 100;
            var doses = new double[][] { Enumerable.Repeat(10.0, n).ToArray() };
            var masks = new bool[][] { Enumerable.Repeat(true, n).ToArray() };

            var dvh = DVHCalculator.BinToHistogram(doses, masks, 20.0);

            dvh[0].VolumePercent.Should().BeApproximately(100.0, 0.1);
            // Far above the dose, curve is at 0% (cumulative subtracted everything).
            dvh.Last().VolumePercent.Should().BeApproximately(0.0, 0.1);
        }

        [Fact]
        public void BinToHistogram_DvhIsMonotonicallyNonIncreasing()
        {
            // Random distribution; pin invariant: cumulative DVH never grows.
            int n = 256;
            var rng = new System.Random(42);
            double[] dose = new double[n];
            for (int i = 0; i < n; i++) dose[i] = rng.NextDouble() * 50.0;

            var dvh = DVHCalculator.BinToHistogram(
                new[] { dose }, new[] { Enumerable.Repeat(true, n).ToArray() }, 60.0);

            for (int i = 1; i < dvh.Length; i++)
                dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent,
                    $"cumulative volume must not grow between bin {i - 1} and {i}");
        }

        [Fact]
        public void BinToHistogram_BinWidth_IsTenPercentAboveMaxDoseDividedByNumBins()
        {
            // Pin: binWidth = maxDoseGy * 1.1 / numBins. Verified by reading the
            // first two output dose points (which are i * binWidth).
            var doses = new double[][] { new[] { 1.0 } };
            var masks = new bool[][] { new[] { true } };
            const double maxDoseGy = 10.0;

            var dvh = DVHCalculator.BinToHistogram(doses, masks, maxDoseGy);

            double expectedBinWidth = maxDoseGy * 1.1 / DomainConstants.DvhHistogramBins;
            dvh[0].DoseGy.Should().Be(0);
            dvh[1].DoseGy.Should().BeApproximately(expectedBinWidth, 1e-12);
            dvh[2].DoseGy.Should().BeApproximately(2 * expectedBinWidth, 1e-12);
        }

        // ── ComputeCumulative: fixed-width bins + exact statistics ────────

        [Fact]
        public void ComputeCumulative_StatisticsComeFromVoxelsNotFromCurve()
        {
            // Same clinical case as above. Max/mean/min must be the exact voxel values;
            // the curve is only for plotting.
            var dose = new double[10];
            for (int i = 0; i < 7; i++) dose[i] = 25.6;
            var masks = new bool[][] { Enumerable.Repeat(true, 10).ToArray() };

            var dvh = DVHCalculator.ComputeCumulative(new[] { dose }, masks, 0.01, out var stats);

            stats.VoxelCount.Should().Be(10);
            stats.DMaxGy.Should().Be(25.6);
            stats.DMinGy.Should().Be(0.0, "part of the structure receives no dose");
            stats.DMeanGy.Should().BeApproximately(7 * 25.6 / 10, 1e-12);

            dvh[0].VolumePercent.Should().Be(100.0);
            dvh.First(p => p.DoseGy > 1.0).VolumePercent.Should().BeApproximately(70.0, 1e-9);
            // 25.6 / 0.01 lands a hair below 2560 in floating point, so the dosed voxels sit in
            // the 25.59 bin; the curve is sized so exactly one empty bin follows the maximum.
            dvh.First(p => p.DoseGy > 25.585).VolumePercent.Should().BeApproximately(70.0, 1e-9);
            dvh.Last().DoseGy.Should().BeInRange(25.6, 25.62);
            dvh.Last().VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_BinWidthIsFixedAndCurveIsSizedToStructureMax()
        {
            // Two voxels at 1 and 3 Gy, 0.5 Gy bins → occupied bins 2 and 6, one empty
            // bin above → 8 points at 0, 0.5, …, 3.5 Gy. No dependence on any global max.
            var doses = new double[][] { new[] { 1.0, 3.0 } };
            var masks = new bool[][] { new[] { true, true } };

            var dvh = DVHCalculator.ComputeCumulative(doses, masks, 0.5, out var stats);

            stats.DMaxGy.Should().Be(3.0);
            stats.DMinGy.Should().Be(1.0);
            stats.DMeanGy.Should().Be(2.0);
            dvh.Length.Should().Be(8);
            dvh[1].DoseGy.Should().Be(0.5);
            dvh.Last().DoseGy.Should().Be(3.5);
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh[2].VolumePercent.Should().Be(100.0, "both voxels receive ≥ 1 Gy");
            dvh[3].VolumePercent.Should().Be(50.0, "only the 3 Gy voxel receives ≥ 1.5 Gy");
            dvh[6].VolumePercent.Should().Be(50.0, "the 3 Gy voxel receives ≥ 3 Gy");
            dvh[7].VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_IsMonotonicallyNonIncreasing()
        {
            int n = 256;
            var rng = new System.Random(7);
            double[] dose = new double[n];
            for (int i = 0; i < n; i++) dose[i] = rng.NextDouble() * 50.0;

            var dvh = DVHCalculator.ComputeCumulative(
                new[] { dose }, new[] { Enumerable.Repeat(true, n).ToArray() }, 0.01, out var stats);

            stats.DMaxGy.Should().Be(dose.Max());
            for (int i = 1; i < dvh.Length; i++)
                dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent,
                    $"cumulative volume must not grow between bin {i - 1} and {i}");
            dvh.Last().VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_InvalidInputs_ReturnEmpty()
        {
            var doses = new double[][] { new[] { 5.0, 5.0 } };
            var allFalse = new bool[][] { new[] { false, false } };
            var allTrue = new bool[][] { new[] { true, true } };

            DVHCalculator.ComputeCumulative(null!, allTrue, 0.01, out var s1).Should().BeEmpty();
            s1.VoxelCount.Should().Be(0);
            DVHCalculator.ComputeCumulative(doses, null!, 0.01, out _).Should().BeEmpty();
            DVHCalculator.ComputeCumulative(doses, allFalse, 0.01, out var s2).Should().BeEmpty();
            s2.VoxelCount.Should().Be(0);
            DVHCalculator.ComputeCumulative(doses, allTrue, 0.0, out _).Should().BeEmpty();
            DVHCalculator.ComputeCumulative(doses, allTrue, -1.0, out _).Should().BeEmpty();
        }

        [Fact]
        public void ComputeStatistics_NegativeAndNaNDosesCountAsZero()
        {
            // Calibration offsets can produce tiny negatives; NaN would poison a mean.
            // Both count as 0 Gy voxels of the structure.
            var doses = new double[][] { new[] { -0.5, double.NaN, 4.0 } };
            var masks = new bool[][] { new[] { true, true, true } };

            var stats = DVHCalculator.ComputeStatistics(doses, masks);

            stats.VoxelCount.Should().Be(3);
            stats.DMaxGy.Should().Be(4.0);
            stats.DMinGy.Should().Be(0.0);
            stats.DMeanGy.Should().BeApproximately(4.0 / 3.0, 1e-12);
        }
    }
}
