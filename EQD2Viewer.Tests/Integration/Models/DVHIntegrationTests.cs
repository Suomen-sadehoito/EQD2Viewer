using EQD2Viewer.Core.Models;
using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Fixtures;
using FluentAssertions;
using System.Linq;

namespace EQD2Viewer.Tests.Integration
{
    /// <summary>
    /// Integration tests for the voxel DVH engine (the path behind every Σ row) on
    /// Eclipse-exported fixture dose slices.
    ///
    /// The fixture generator writes, for each exported dose slice, the max and the mean
    /// over every voxel of that slice (zeros included). Those are the exact figures
    /// <see cref="DVHCalculator.ComputeStatistics"/> must reproduce over a whole-slice
    /// mask, so this is a direct check of the production statistics against values
    /// computed independently at export time.
    ///
    /// Eclipse's own DVH curves are also exercised: the EQD2 conversion applied to
    /// them must stay finite, ordered and bounded.
    /// </summary>
    public class DVHIntegrationTests
    {
        [Theory]
        [MemberData(nameof(FixtureLoader.AllFixtureDirectories), MemberType = typeof(FixtureLoader))]
        public void SliceStatistics_ShouldMatchFixtureMaxAndMean(string fixtureName)
        {
            var slices = FixtureLoader.LoadDoseSlices(fixtureName);
            slices.Should().NotBeEmpty("fixture must contain dose slices");

            foreach (var slice in slices)
            {
                var mask = new bool[][] { Enumerable.Repeat(true, slice.valuesGy.Length).ToArray() };

                var stats = DVHCalculator.ComputeStatistics(new[] { slice.valuesGy }, mask);

                stats.VoxelCount.Should().Be(slice.valuesGy.Length);
                // The generator rounds to 4 decimals.
                stats.DMaxGy.Should().BeApproximately(slice.maxDoseGy, 5e-5,
                    $"slice {slice.sliceIndex}: Dmax must equal the exported maximum");
                stats.DMeanGy.Should().BeApproximately(slice.meanDoseGy, 5e-5,
                    $"slice {slice.sliceIndex}: Dmean must equal the exported mean over all voxels");
            }
        }

        [Theory]
        [MemberData(nameof(FixtureLoader.AllFixtureDirectories), MemberType = typeof(FixtureLoader))]
        public void SliceCurve_StartsAtHundredEndsAtZero_AndIsMonotonic(string fixtureName)
        {
            var slices = FixtureLoader.LoadDoseSlices(fixtureName);
            slices.Should().NotBeEmpty();

            foreach (var slice in slices)
            {
                var mask = new bool[][] { Enumerable.Repeat(true, slice.valuesGy.Length).ToArray() };

                var dvh = DVHCalculator.ComputeCumulative(new[] { slice.valuesGy }, mask,
                    DomainConstants.DvhSamplingResolution, out var stats);

                dvh.Should().NotBeEmpty();
                dvh[0].VolumePercent.Should().Be(100.0, "every voxel receives ≥ 0 Gy");
                dvh.Last().VolumePercent.Should().Be(0.0, "no voxel receives more than the maximum");
                dvh.Last().DoseGy.Should().BeGreaterThan(stats.DMaxGy, "the curve is sized past the structure's own maximum");
                for (int i = 1; i < dvh.Length; i++)
                    dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent,
                        $"slice {slice.sliceIndex}: cumulative DVH must be non-increasing at bin {i}");
            }
        }

        [Theory]
        [MemberData(nameof(FixtureLoader.AllFixtureDirectories), MemberType = typeof(FixtureLoader))]
        public void EclipseCurve_LastNonZeroPoint_IsNearReportedDmax(string fixtureName)
        {
            // Sanity check on the fixture itself: Eclipse's reported Dmax must sit where
            // its own cumulative curve reaches zero volume (within the 0.01 Gy sampling
            // Eclipse was asked for, plus its own rounding).
            var dvhFixtures = FixtureLoader.LoadDvhCurves(fixtureName);

            foreach (var dvhFix in dvhFixtures)
            {
                if (dvhFix.curve == null || dvhFix.curve.Length == 0) continue;

                double lastNonZeroDose = 0;
                for (int i = dvhFix.curve.Length - 1; i >= 0; i--)
                    if (dvhFix.curve[i][1] > 0) { lastNonZeroDose = dvhFix.curve[i][0]; break; }

                lastNonZeroDose.Should().BeApproximately(dvhFix.dmaxGy, 0.5,
                    $"{dvhFix.structureId}: Eclipse Dmax {dvhFix.dmaxGy:F2} vs curve end {lastNonZeroDose:F2}");
            }
        }

        [Theory]
        [MemberData(nameof(FixtureLoader.AllFixtureDirectories), MemberType = typeof(FixtureLoader))]
        public void EQD2DVHCurve_AllPoints_ShouldBeFiniteAndOrdered(string fixtureName)
        {
            var meta = FixtureLoader.LoadMetadata(fixtureName);
            var dvhFixtures = FixtureLoader.LoadDvhCurves(fixtureName);
            int fx = meta.numberOfFractions > 0 ? meta.numberOfFractions : 1;

            foreach (var dvhFix in dvhFixtures)
            {
                if (dvhFix.curve == null || dvhFix.curve.Length < 2) continue;

                var curveAsPoints = dvhFix.curve
                    .Select(p => new DoseVolumePoint(p[0], p[1]))
                    .ToArray();

                foreach (double ab in new[] { 3.0, 10.0 })
                {
                    var eqd2Curve = EQD2Calculator.ConvertCurveToEQD2(curveAsPoints, fx, ab);

                    eqd2Curve.Should().NotBeEmpty();

                    for (int i = 0; i < eqd2Curve.Length; i++)
                    {
                        double d = eqd2Curve[i].DoseGy;
                        double.IsNaN(d).Should().BeFalse($"NaN dose at [{i}] for {dvhFix.structureId}");
                        double.IsInfinity(d).Should().BeFalse($"Inf dose at [{i}]");
                        d.Should().BeGreaterOrEqualTo(0, $"negative EQD2 dose at [{i}]");
                    }

                    for (int i = 1; i < eqd2Curve.Length; i++)
                        eqd2Curve[i].DoseGy.Should()
                            .BeGreaterOrEqualTo(eqd2Curve[i - 1].DoseGy - 1e-6,
                            $"EQD2 curve not monotonic at [{i}]");
                }
            }
        }

        [Theory]
        [MemberData(nameof(FixtureLoader.AllFixtureDirectories), MemberType = typeof(FixtureLoader))]
        public void MeanEQD2FromDVH_ShouldBeReasonable(string fixtureName)
        {
            var meta = FixtureLoader.LoadMetadata(fixtureName);
            var dvhFixtures = FixtureLoader.LoadDvhCurves(fixtureName);
            int fx = meta.numberOfFractions > 0 ? meta.numberOfFractions : 1;

            foreach (var dvhFix in dvhFixtures)
            {
                if (dvhFix.curve == null || dvhFix.curve.Length < 2) continue;

                var curveAsPoints = dvhFix.curve
                    .Select(p => new DoseVolumePoint(p[0], p[1]))
                    .ToArray();

                double meanEqd2 = EQD2Calculator.CalculateMeanEQD2FromDVH(curveAsPoints, fx, 3.0);

                meanEqd2.Should().BeGreaterOrEqualTo(0,
                    $"negative mean EQD2 for {dvhFix.structureId}");

                double eqd2Dmax = EQD2Calculator.ToEQD2(dvhFix.dmaxGy, fx, 3.0);
                meanEqd2.Should().BeLessOrEqualTo(eqd2Dmax + 0.1,
                    $"mean EQD2 exceeds Dmax for {dvhFix.structureId}");
            }
        }
    }
}
