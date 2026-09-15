using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Models;
using FluentAssertions;
using System.Linq;

namespace EQD2Viewer.Tests.Calculations
{
    /// <summary>
    /// Pins the cumulative-DVH convention of <see cref="DVHCalculator.ComputeCumulative"/>
    /// (fixed-width bins sized to the structure's own maximum) and the exact voxel
    /// statistics of <see cref="DVHCalculator.ComputeStatistics"/> — the pair behind
    /// <c>SummationService.ComputeStructureDVH</c> and therefore every Σ row and curve.
    ///
    /// The invariant that matters: every masked voxel is binned, zero-dose voxels
    /// included, so a cumulative curve always decays to 0 % above the true maximum.
    /// (Zero-dose voxels used to be counted in the total but never binned, which left
    /// the curve stuck above 0 % all the way to the last bin — the root of the
    /// phantom-Dmax bug, where the table then read the last bin as Dmax.)
    /// </summary>
    public class DVHCalculatorTests
    {
        private const double W = DomainConstants.DvhSamplingResolution; // 0.01 Gy

        private static bool[][] AllTrue(int n) => new[] { Enumerable.Repeat(true, n).ToArray() };

        [Fact]
        public void ComputeCumulative_InvalidInputs_ReturnEmpty()
        {
            var doses = new double[][] { new[] { 5.0, 5.0 } };
            var allFalse = new bool[][] { new[] { false, false } };
            var allTrue = AllTrue(2);

            DVHCalculator.ComputeCumulative(null!, allTrue, W, out var s1).Should().BeEmpty();
            s1.VoxelCount.Should().Be(0);
            DVHCalculator.ComputeCumulative(doses, null!, W, out _).Should().BeEmpty();
            DVHCalculator.ComputeCumulative(doses, allFalse, W, out var s2).Should().BeEmpty("no voxel inside the mask");
            s2.VoxelCount.Should().Be(0);
            DVHCalculator.ComputeCumulative(doses, allTrue, 0.0, out _).Should().BeEmpty();
            DVHCalculator.ComputeCumulative(doses, allTrue, -1.0, out _).Should().BeEmpty();
            DVHCalculator.ComputeCumulative(doses, allTrue, double.NaN, out _).Should().BeEmpty();
        }

        [Fact]
        public void ComputeCumulative_AllZeroDose_CurveIsHundredThenZero()
        {
            // Zero-dose voxels are real voxels of the structure: they sit in bin 0, so the
            // curve is 100 % at 0 Gy and 0 % from the next bin on.
            var dvh = DVHCalculator.ComputeCumulative(new[] { new double[10] }, AllTrue(10), W, out var stats);

            stats.VoxelCount.Should().Be(10);
            stats.DMaxGy.Should().Be(0.0);
            stats.DMeanGy.Should().Be(0.0);
            dvh.Length.Should().Be(2, "bin 0 holds every voxel and exactly one empty bin follows");
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh[1].VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_MixedZeroAndDosedVoxels_CurveReachesZeroAboveTrueMax()
        {
            // Reported clinical case: a spinal canal partly outside the dose grid in a
            // two-plan sum whose global maximum (PTV hotspot) was 76 Gy. Seven voxels at
            // 25.6 Gy, three uncovered voxels at 0 Gy. The curve must be 70 % over
            // (0, 25.6] and 0 % above — not 30 % all the way to 83.5 Gy.
            // 25.6 / 0.01 is exactly 2560 in double arithmetic, so the dosed voxels sit in
            // bin 2560 (25.60 Gy) and bin 2561 is the empty tail.
            var dose = new double[10];
            for (int i = 0; i < 7; i++) dose[i] = 25.6;

            var dvh = DVHCalculator.ComputeCumulative(new[] { dose }, AllTrue(10), W, out var stats);

            stats.VoxelCount.Should().Be(10);
            stats.DMaxGy.Should().Be(25.6);
            stats.DMinGy.Should().Be(0.0, "part of the structure receives no dose");
            stats.DMeanGy.Should().BeApproximately(7 * 25.6 / 10, 1e-12);

            dvh.Length.Should().Be(2562);
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh[1].VolumePercent.Should().BeApproximately(70.0, 1e-9, "the three 0 Gy voxels drop out after bin 0");
            dvh[2560].VolumePercent.Should().BeApproximately(70.0, 1e-9);
            dvh[2561].DoseGy.Should().BeApproximately(25.61, 1e-9);
            dvh[2561].VolumePercent.Should().Be(0.0, "zero-dose voxels are subtracted from the curve like any other voxel");
        }

        [Fact]
        public void ComputeCumulative_UniformDose_ProducesStepFunction()
        {
            // 100 voxels at 10 Gy: 10 / 0.01 is exactly 1000 → bins 0..1000 hold 100 %, bin 1001 is empty.
            var dvh = DVHCalculator.ComputeCumulative(new[] { Enumerable.Repeat(10.0, 100).ToArray() }, AllTrue(100), W, out var stats);

            stats.DMaxGy.Should().Be(10.0);
            stats.DMeanGy.Should().Be(10.0);
            stats.DMinGy.Should().Be(10.0);
            dvh.Length.Should().Be(1002);
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh[1000].VolumePercent.Should().Be(100.0);
            dvh[1001].VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_BinWidthIsFixedAndCurveIsSizedToStructureMax()
        {
            // Two voxels at 1 and 3 Gy, 0.5 Gy bins → occupied bins 2 and 6, one empty
            // bin above → 8 points at 0, 0.5, …, 3.5 Gy. No dependence on any global max.
            var doses = new double[][] { new[] { 1.0, 3.0 } };

            var dvh = DVHCalculator.ComputeCumulative(doses, AllTrue(2), 0.5, out var stats);

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
        public void ComputeCumulative_SingleVoxel()
        {
            DVHCalculator.ComputeCumulative(new[] { new[] { 0.0 } }, AllTrue(1), W, out var s0).Length.Should().Be(2);
            s0.VoxelCount.Should().Be(1);

            var dvh = DVHCalculator.ComputeCumulative(new[] { new[] { 3.0 } }, AllTrue(1), W, out var s3);
            s3.DMaxGy.Should().Be(3.0);
            dvh.Length.Should().Be(302);
            dvh[300].VolumePercent.Should().Be(100.0);
            dvh[301].VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_IsMonotonicallyNonIncreasing()
        {
            int n = 256;
            var rng = new System.Random(7);
            double[] dose = new double[n];
            for (int i = 0; i < n; i++) dose[i] = rng.NextDouble() * 50.0;

            var dvh = DVHCalculator.ComputeCumulative(new[] { dose }, AllTrue(n), W, out var stats);

            stats.DMaxGy.Should().Be(dose.Max());
            stats.DMinGy.Should().Be(dose.Min());
            stats.DMeanGy.Should().BeApproximately(dose.Average(), 1e-9);
            for (int i = 1; i < dvh.Length; i++)
                dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent,
                    $"cumulative volume must not grow between bin {i - 1} and {i}");
            dvh.Last().VolumePercent.Should().Be(0.0);
        }

        [Fact]
        public void ComputeCumulative_DoseBeyondBinCap_ClampsIntoLastBin()
        {
            // 5000 Gy at 0.01 Gy would need 500 002 bins; the histogram is capped and the
            // voxel clamped into the last bin, so the 0 % tail is not guaranteed above the
            // cap. Only a corrupt dose value gets here — the cap bounds the allocation.
            var dvh = DVHCalculator.ComputeCumulative(new[] { new[] { 5000.0 } }, AllTrue(1), W, out var stats);

            dvh.Length.Should().Be(DomainConstants.DvhMaxHistogramBins);
            stats.DMaxGy.Should().Be(5000.0, "statistics are unaffected by the cap");
            dvh.Last().VolumePercent.Should().Be(100.0);
        }

        [Fact]
        public void ComputeStatistics_NegativeAndNaNDosesCountAsZero()
        {
            // Calibration offsets can produce tiny negatives; NaN would poison a mean.
            // Both count as 0 Gy voxels of the structure.
            var doses = new double[][] { new[] { -0.5, double.NaN, 4.0 } };

            var stats = DVHCalculator.ComputeStatistics(doses, AllTrue(3));

            stats.VoxelCount.Should().Be(3);
            stats.DMaxGy.Should().Be(4.0);
            stats.DMinGy.Should().Be(0.0);
            stats.DMeanGy.Should().BeApproximately(4.0 / 3.0, 1e-12);
        }

        [Fact]
        public void ComputeStatistics_MaskedVoxelsOnly_AcrossSlicesWithNullEntries()
        {
            // Slices the structure does not intersect are null masks and must be skipped;
            // unmasked voxels on intersected slices must not count.
            var doses = new double[][] { new[] { 1.0, 9.0 }, null!, new[] { 5.0, 100.0 } };
            var masks = new bool[][] { new[] { true, false }, null!, new[] { true, false } };

            var stats = DVHCalculator.ComputeStatistics(doses, masks);

            stats.VoxelCount.Should().Be(2);
            stats.DMaxGy.Should().Be(5.0);
            stats.DMinGy.Should().Be(1.0);
            stats.DMeanGy.Should().Be(3.0);
        }
    }
}
