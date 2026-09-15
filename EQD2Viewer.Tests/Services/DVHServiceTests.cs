using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Services;
using FluentAssertions;

namespace EQD2Viewer.Tests.Services
{
    /// <summary>
    /// Tests for the Dose Statistics rows built from a single plan's Eclipse DVH curve.
    /// (Summation rows are covered by SummationServiceTests / DVHCalculatorTests.)
    /// </summary>
    public class DVHServiceTests
    {
        private readonly DVHService _service = new DVHService();

        [Fact]
        public void BuildPhysicalSummaryFromCurve_IsLabelledAsEclipseSource()
        {
            var dvh = new DvhCurveData { StructureId = "S", DMaxGy = 20, DMeanGy = 15, DMinGy = 10, VolumeCc = 3 };

            var summary = _service.BuildPhysicalSummaryFromCurve(dvh, "Plan1");

            summary.Type.Should().Be("Physical");
            summary.Source.Should().Be(DVHSummary.SourceEclipse);
            summary.IsSummation.Should().BeFalse();
            summary.DMax.Should().Be(20);
            summary.DMean.Should().Be(15);
            summary.DMin.Should().Be(10);
            summary.Volume.Should().Be(3);
        }

        [Fact]
        public void BuildEQD2SummaryFromCurve_HeterogeneousDose_DmeanIsMeanOfEQD2NotEQD2OfMean()
        {
            // Half the structure at 10 Gy, half at 20 Gy, 5 fractions, α/β = 3.
            //   EQD2(10 Gy, 2 Gy/fx)  = 10 · (2 + 3) / 5 = 10 Gy
            //   EQD2(20 Gy, 4 Gy/fx)  = 20 · (4 + 3) / 5 = 28 Gy   → mean of EQD2 = 19 Gy
            //   EQD2(mean 15 Gy, 3 Gy/fx) = 15 · (3 + 3) / 5 = 18 Gy  ← the removed "Simple" method
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
        public void BuildEQD2SummaryFromCurve_NoCurve_FallsBackToEQD2OfMeanAndSaysSo()
        {
            var dvh = new DvhCurveData { StructureId = "S", DMaxGy = 20, DMeanGy = 15, DMinGy = 10, Curve = null! };

            var summary = _service.BuildEQD2SummaryFromCurve(dvh, "Plan1", numberOfFractions: 5, alphaBeta: 3.0);

            summary.DMean.Should().BeApproximately(18.0, 1e-9, "without curve data only the physical mean is available");
            summary.Type.Should().Be("EQD2 (of mean)", "the underestimating method must be visible in the table");
        }
    }
}
