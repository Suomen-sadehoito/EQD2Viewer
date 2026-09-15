using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Services;
using EQD2Viewer.Tests.Common;
using FluentAssertions;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EQD2Viewer.Tests.Services
{
    /// <summary>
    /// Core tests for SummationService — covers the direct-sample reference path,
    /// the affine-registration path, EQD2 recompute, and per-structure DVH.
    ///
    /// Test strategy:
    ///   * Build a tiny 4x4x2 reference CT with identity orientation.
    ///   * Mock ISummationDataLoader to return deterministic dose voxels.
    ///   * Assert voxel-level dose placement matches the expected sampling.
    /// </summary>
    public class SummationServiceTests
    {
        private const int RefX = 4, RefY = 4, RefZ = 2;

        // ── Test fixtures (delegate to shared TestVolumeFactory) ──────────

        private static VolumeData MakeReferenceCt()
            => TestVolumeFactory.MakeCt(RefX, RefY, RefZ, "FOR_REF");

        private static SummationPlanDoseData MakeDoseData(int[][,] doseGy)
            => TestVolumeFactory.MakeSummationDoseData(doseGy, RefX, RefY, RefZ);

        private static int[][,] FillDose(int value)
            => TestVolumeFactory.FillDose(RefX, RefY, RefZ, value);

        private static Mock<ISummationDataLoader> MakeLoader(
            int refDoseGy,
            int movingDoseGy,
            string movingFor = "FOR_MOV")
        {
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>()))
                  .Returns(MakeDoseData(FillDose(refDoseGy)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>()))
                  .Returns(MakeDoseData(FillDose(movingDoseGy)));
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>());
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns(movingFor);
            return loader;
        }

        private static SummationConfig MakeConfig()
            => new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = false }
                }
            };

        // ── PrepareData validation ─────────────────────────────────────────

        [Fact]
        public void PrepareData_EmptyConfig_ReturnsFailure()
        {
            var svc = new SummationService(MakeReferenceCt(),
                new Mock<ISummationDataLoader>().Object, new List<RegistrationData>());
            var result = svc.PrepareData(new SummationConfig { Plans = new List<SummationPlanEntry>() });
            result.Success.Should().BeFalse();
            result.StatusMessage.Should().Contain("No plans");
        }

        [Fact]
        public void PrepareData_NoReferencePlan_ReturnsFailure()
        {
            var svc = new SummationService(MakeReferenceCt(),
                new Mock<ISummationDataLoader>().Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C", PlanId = "P", IsReference = false }
                }
            };
            var result = svc.PrepareData(config);
            result.Success.Should().BeFalse();
            result.StatusMessage.Should().Contain("reference");
        }

        // ── ComputeAsync: reference-only pathway ───────────────────────────

        [Fact]
        public async Task ComputeAsync_ReferenceOnlyPlan_AccumulatesDirectDose()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var r = await svc.ComputeAsync(null, CancellationToken.None);
            r.Success.Should().BeTrue();
            r.MaxDoseGy.Should().BeApproximately(5, 1e-6);
        }

        // ── ComputeAsync: cancellation ─────────────────────────────────────

        [Fact]
        public async Task ComputeAsync_WithCancelledToken_PropagatesCancellation()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 1);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            // Task.Run with a pre-cancelled token schedules a canceled Task; awaiting surfaces
            // TaskCanceledException. That is the contract the UI layer expects — it catches
            // OperationCanceledException to distinguish user-cancel from server error.
            System.Func<Task> act = async () => await svc.ComputeAsync(null, cts.Token);
            await act.Should().ThrowAsync<System.Threading.Tasks.TaskCanceledException>();
        }

        [Fact]
        public void Dispose_ClearsInternalState()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 1);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            svc.Dispose();
            svc.HasSummedDose.Should().BeFalse();
        }

        [Fact]
        public void SliceCount_MatchesReferenceZSize()
        {
            var loader = MakeLoader(refDoseGy: 0, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig());
            svc.SliceCount.Should().Be(RefZ);
        }

        [Fact]
        public void VoxelVolume_ComputedFromSpacing_1mm3IsOneNanoLiter()
        {
            var loader = MakeLoader(refDoseGy: 0, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig());
            // 1mm × 1mm × 1mm = 0.001 cm³ = 1 µL
            svc.GetVoxelVolumeCc().Should().BeApproximately(0.001, 1e-9);
        }

        // ── Affine (registered) pathway ────────────────────────────────────

        /// <summary>
        /// Affine path (RegistrationId set) with identity matrix must behave exactly
        /// like the direct path — covers AccumulatePhysicalRegistered.
        /// </summary>
        [Fact]
        public async Task ComputeAsync_AffineIdentityMatrix_SamplesAtSamePosition()
        {
            // Moving dose varies by X so we can verify per-voxel correctness.
            var movingDose = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++)
            {
                movingDose[z] = new int[RefX, RefY];
                for (int y = 0; y < RefY; y++)
                    for (int x = 0; x < RefX; x++)
                        movingDose[z][x, y] = x * 10;
            }

            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(0)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(movingDose));
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>());
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF"); // same FOR

            var identityMatrix = new RegistrationData
            {
                Id = "REG_ID",
                SourceFOR = "FOR_REF",
                RegisteredFOR = "FOR_REF",
                Matrix = new double[]
                {
                    1, 0, 0, 0,
                    0, 1, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                }
            };

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData> { identityMatrix });
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = false,
                        RegistrationId = "REG_ID" }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var r = await svc.ComputeAsync(null, CancellationToken.None);
            r.Success.Should().BeTrue();

            var slice = svc.GetSummedSlice(0);
            slice.Should().NotBeNull();
            // Identity transform → each ref voxel gets moving dose at same index
            // ref (0, 0, 0) → moving (0, 0, 0) → 0*10 = 0
            slice![0 + 0 * RefX].Should().BeApproximately(0, 1e-4);
            // ref (2, 0, 0) → moving (2, 0, 0) → 2*10 = 20
            slice[2 + 0 * RefX].Should().BeApproximately(20, 1e-4);
        }

        // ── Structure DVH ──────────────────────────────────────────────────

        [Fact]
        public async Task ComputeStructureDVH_NonexistentStructure_ReturnsEmpty()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var result = svc.ComputeStructureDVH("NonExistent", structureAlphaBeta: 3.0);
            result.IsEmpty.Should().BeTrue();
            result.Curve.Should().BeEmpty();
            result.Statistics.VoxelCount.Should().Be(0);
        }

        [Fact]
        public void ComputeStructureDVH_BeforeCompute_ReturnsEmpty()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            // No ComputeAsync → no per-plan physical slices yet.
            svc.ComputeStructureDVH("AnyStruct", structureAlphaBeta: 3.0).IsEmpty.Should().BeTrue();
        }

        // ── Display α/β recompute ──────────────────────────────────────────

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_WithoutPriorCompute_ReturnsFailure()
        {
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            // Note: no ComputeAsync call → no cached physical slices
            var r = await svc.RecomputeEQD2DisplayAsync(3.0, null, CancellationToken.None);
            r.Success.Should().BeFalse();
            r.StatusMessage.Should().Contain("No");
        }

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_PhysicalMode_ReproducesOriginalMaxDose()
        {
            // In Physical mode, changing α/β should NOT change the summed dose — it's a no-op.
            var loader = MakeLoader(refDoseGy: 3, movingDoseGy: 7);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            var original = await svc.ComputeAsync(null, CancellationToken.None);
            double originalMax = original.MaxDoseGy;

            var recomputed = await svc.RecomputeEQD2DisplayAsync(10.0, null, CancellationToken.None);
            recomputed.Success.Should().BeTrue();
            recomputed.MaxDoseGy.Should().BeApproximately(originalMax, 1e-6,
                "Physical-mode summation is α/β-independent");
        }

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_AlphaBetaZero_FallsBackToPhysicalDose()
        {
            // α/β = 0 is a hypo-fractionation edge case that must not produce NaN/Inf.
            // EQD2Calculator treats α/β ≤ 0 as "no EQD2 transform" and returns identity
            // factors (Q=0, L=1 → eqd2 = 0·D² + 1·D = D). Verify this propagates through
            // the recompute path without crashing.
            var loader = MakeLoader(refDoseGy: 5, movingDoseGy: 3);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            var first = await svc.ComputeAsync(null, CancellationToken.None);
            first.Success.Should().BeTrue();

            // Recompute with α/β = 0: must not crash, must return finite values.
            var r = await svc.RecomputeEQD2DisplayAsync(0.0, null, CancellationToken.None);
            r.Success.Should().BeTrue("α/β = 0 path must fall back gracefully, not throw");
            double.IsNaN(r.MaxDoseGy).Should().BeFalse();
            double.IsInfinity(r.MaxDoseGy).Should().BeFalse();
        }

        [Fact]
        public async Task RecomputeEQD2DisplayAsync_VeryHighAlphaBeta_StaysFinite()
        {
            // α/β = 1e6 → EQD2 formula → (d + 1e6) / (2 + 1e6) ≈ 1. Essentially physical dose.
            var loader = MakeLoader(refDoseGy: 2, movingDoseGy: 2);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);
            var r = await svc.RecomputeEQD2DisplayAsync(1e6, null, CancellationToken.None);
            r.Success.Should().BeTrue();
            double.IsNaN(r.MaxDoseGy).Should().BeFalse();
            double.IsInfinity(r.MaxDoseGy).Should().BeFalse();
        }

        // ── Affine path: FOR-flip (inversion branch) ─────────────────────

        /// <summary>
        /// When the registration is stored as plan→ref (SourceFOR == planFOR), CachePlanData
        /// must invert the matrix to get ref→plan. This exercises MatrixMath.Invert4x4 through
        /// the affine summation path. Uses translation-by-one-in-X so the effect is observable
        /// in the sampled dose row.
        /// </summary>
        [Fact]
        public async Task ComputeAsync_AffineSourceIsPlan_InvertsMatrix()
        {
            // Moving dose gradient: dose at (x, y, z) = x * 10
            var movingDose = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++)
            {
                movingDose[z] = new int[RefX, RefY];
                for (int y = 0; y < RefY; y++)
                    for (int x = 0; x < RefX; x++)
                        movingDose[z][x, y] = x * 10;
            }

            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(0)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(movingDose));
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>());
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_MOV");

            // Plan→ref translation: shift +1 in X. Matrix is SourceFOR=FOR_MOV (the plan).
            // CachePlanData must invert → ref→plan = shift -1 in X.
            // Effect on sampling: ref voxel (x, 0, 0) samples plan at (x - 1, 0, 0) → dose (x-1)*10.
            var reg = new RegistrationData
            {
                Id = "REG_PLAN_TO_REF",
                SourceFOR = "FOR_MOV",   // plan side — triggers inversion branch
                RegisteredFOR = "FOR_REF",
                Matrix = new double[]
                {
                    1, 0, 0, 1,  // translate +1 in X (plan → ref)
                    0, 1, 0, 0,
                    0, 0, 1, 0,
                    0, 0, 0, 1
                }
            };

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData> { reg });
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = false,
                        RegistrationId = "REG_PLAN_TO_REF" }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var r = await svc.ComputeAsync(null, CancellationToken.None);
            r.Success.Should().BeTrue();

            var slice = svc.GetSummedSlice(0);
            slice.Should().NotBeNull();
            // ref (1, 0, 0) → inverted transform → plan (0, 0, 0) → dose 0
            slice![1 + 0 * RefX].Should().BeApproximately(0, 1e-4);
            // ref (2, 0, 0) → plan (1, 0, 0) → dose 10
            slice[2 + 0 * RefX].Should().BeApproximately(10, 1e-4);
            // ref (3, 0, 0) → plan (2, 0, 0) → dose 20
            slice[3 + 0 * RefX].Should().BeApproximately(20, 1e-4);
        }

        // ── Structure-specific EQD2 DVH ──────────────────────────────────

        /// <summary>
        /// Constructs a structure covering the whole volume, computes the DVH, and verifies
        /// monotonic cumulative volume plus exact statistics for a uniform dose.
        /// </summary>
        [Fact]
        public async Task ComputeStructureDVH_NonEmptyStructure_ProducesMonotonicCurve()
        {
            // Reference plan dose = 10 Gy everywhere. Structure: the whole volume.
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(10)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData
                      {
                          Id = "BODY",
                          DicomType = "EXTERNAL",
                          // Polygon that covers the entire slice for all Z
                          ContoursBySlice = BuildWholeVolumeStructure()
                      }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 25, TotalDoseGy = 50, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var result = svc.ComputeStructureDVH("BODY", structureAlphaBeta: 3.0);
            var dvh = result.Curve;

            result.IsEmpty.Should().BeFalse();
            result.Statistics.VoxelCount.Should().Be(RefX * RefY * RefZ);
            result.Statistics.DMaxGy.Should().BeApproximately(10, 1e-9);
            result.Statistics.DMeanGy.Should().BeApproximately(10, 1e-9);
            result.Statistics.DMinGy.Should().BeApproximately(10, 1e-9);
            // Cumulative DVH must be monotonically non-increasing.
            for (int i = 1; i < dvh.Length; i++)
                dvh[i].VolumePercent.Should().BeLessOrEqualTo(dvh[i - 1].VolumePercent + 0.01,
                    $"cumulative DVH must not grow at bin {i}");
            // First bin (dose 0) is 100 % volume; the curve ends at 0 % just above 10 Gy.
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh.Last().VolumePercent.Should().Be(0.0);
            dvh.Last().DoseGy.Should().BeInRange(10.0, 10.03, "bins are 0.01 Gy wide and sized to the structure's own max");
        }

        /// <summary>
        /// Regression for the phantom-Dmax bug. A structure that extends beyond the dose grid
        /// (here: a 4-slice spinal canal over a 2-slice dose grid) has voxels that receive no
        /// dose. They are still voxels of the structure: counted at 0 Gy, so the curve decays
        /// to 0 % above the true maximum and Dmax is the real maximum — not the top of a
        /// histogram sized to the global summed maximum (≈ 1.1 × global max).
        /// </summary>
        [Fact]
        public async Task ComputeStructureDVH_StructureExtendsBeyondDoseGrid_UncoveredVoxelsCountAsZero()
        {
            const int ctZ = 4, doseZ = 2;
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>()))
                  .Returns(TestVolumeFactory.MakeSummationDoseData(
                      TestVolumeFactory.FillDose(RefX, RefY, doseZ, 10), RefX, RefY, doseZ));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "SpinalCanal", DicomType = "ORGAN", ContoursBySlice = BuildWholeVolumeStructure(ctZ) }
                  });

            var svc = new SummationService(TestVolumeFactory.MakeCt(RefX, RefY, ctZ, "FOR_REF"), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 5, TotalDoseGy = 10, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var computed = await svc.ComputeAsync(null, CancellationToken.None);
            computed.Success.Should().BeTrue();
            computed.MaxDoseGy.Should().BeApproximately(10, 1e-9);

            var result = svc.ComputeStructureDVH("SpinalCanal", structureAlphaBeta: 3.0);

            result.IsEmpty.Should().BeFalse();
            result.Statistics.VoxelCount.Should().Be(RefX * RefY * ctZ, "uncovered slices still belong to the structure");
            result.Statistics.DMaxGy.Should().BeApproximately(10, 1e-9, "Dmax is the true maximum, not 1.1 × global max");
            result.Statistics.DMinGy.Should().Be(0.0, "half of the structure is outside the dose grid");
            result.Statistics.DMeanGy.Should().BeApproximately(5, 1e-9);

            var dvh = result.Curve;
            dvh[0].VolumePercent.Should().Be(100.0);
            dvh.First(p => p.DoseGy > 5.0).VolumePercent.Should().BeApproximately(50.0, 1e-9,
                "the covered half receives 10 Gy, the uncovered half 0 Gy");
            dvh.First(p => p.DoseGy > 9.985).VolumePercent.Should().BeApproximately(50.0, 1e-9,
                "the covered half still counts one bin below the maximum");
            dvh.Last().DoseGy.Should().BeInRange(10.0, 10.02, "bins are 0.01 Gy and the curve is sized to the structure max");
            dvh.Last().VolumePercent.Should().Be(0.0, "the curve must decay to zero above the true maximum");
        }

        /// <summary>
        /// Second latent Dmax bug: the structure curve used to be binned over 1.1 × the global
        /// display maximum (computed at the GLOBAL α/β) while its voxels were converted at the
        /// STRUCTURE α/β. For an OAR (α/β = 3) in a sum computed at α/β = 10, the structure's
        /// EQD2 exceeds that range and was clamped into the last bin. Statistics now come
        /// straight from the voxels and the curve is sized to the structure's own maximum.
        /// </summary>
        [Fact]
        public async Task ComputeStructureDVH_StructureAlphaBetaBelowGlobal_DmaxIsExactNotClamped()
        {
            // 20 Gy in one fraction. Global α/β = 10 → EQD2 = 20·(20+10)/(2+10) = 50 Gy (display sum).
            // Structure α/β = 3 → EQD2 = 20·(20+3)/(2+3) = 92 Gy, well above 1.1 × 50 = 55 Gy.
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(20)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "OAR", DicomType = "ORGAN", ContoursBySlice = BuildWholeVolumeStructure() }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.EQD2,
                GlobalAlphaBeta = 10.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 1, TotalDoseGy = 20, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var computed = await svc.ComputeAsync(null, CancellationToken.None);
            computed.MaxDoseGy.Should().BeApproximately(50, 1e-9, "display sum uses the global α/β");

            var result = svc.ComputeStructureDVH("OAR", structureAlphaBeta: 3.0);

            result.Statistics.DMaxGy.Should().BeApproximately(92, 1e-9);
            result.Statistics.DMeanGy.Should().BeApproximately(92, 1e-9);
            result.Statistics.DMinGy.Should().BeApproximately(92, 1e-9);
            result.Curve.Last().DoseGy.Should().BeInRange(92.0, 92.02, "the curve is sized to the structure's own maximum");
            result.Curve.Last().VolumePercent.Should().Be(0.0);
            result.Curve.First(p => p.DoseGy > 91.9).VolumePercent.Should().Be(100.0);
        }

        // ── Mask placement ─────────────────────────────────────────────────

        /// <summary>
        /// Regression: contours are keyed by the slice index of the image the structure set
        /// was drawn on, which is not necessarily the reference CT (same frame of reference,
        /// other series). Masks used to be placed by that key; they must be placed by the
        /// contour's world z against the reference CT geometry. Here the structure set's
        /// image starts 2 mm below the reference CT, so its slice k is the CT's slice k − 2.
        /// </summary>
        [Fact]
        public async Task CacheStructureMasks_ContourKeyedByForeignSliceIndex_PlacedByWorldZ()
        {
            const int ctZ = 6;
            // Dose: 10 Gy on CT slices 0-1 only (plane index 2 = 2 mm → zero), so a correctly placed
            // mask on CT slices 0-1 reads 10 Gy and a mask misplaced by +2 slices reads 0 Gy.
            var dose = new int[ctZ][,];
            for (int z = 0; z < ctZ; z++) dose[z] = TestVolumeFactory.FillDose(RefX, RefY, 1, z < 2 ? 10 : 0)[0];

            // Structure set image origin z = -2 mm: its slices 2 and 3 are world z = 0 and 1 mm,
            // i.e. reference CT slices 0 and 1. The dictionary keys are 2 and 3.
            var contours = new Dictionary<int, List<double[][]>>();
            foreach (int foreignSlice in new[] { 2, 3 })
            {
                double worldZ = foreignSlice - 2.0;
                contours[foreignSlice] = new List<double[][]>
                {
                    new[]
                    {
                        new[] { -0.5, -0.5, worldZ }, new[] { 3.6, -0.5, worldZ },
                        new[] { 3.6, 3.6, worldZ }, new[] { -0.5, 3.6, worldZ }
                    }
                };
            }

            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>()))
                  .Returns(TestVolumeFactory.MakeSummationDoseData(dose, RefX, RefY, ctZ));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "Cord", DicomType = "ORGAN", ContoursBySlice = contours }
                  });

            var svc = new SummationService(TestVolumeFactory.MakeCt(RefX, RefY, ctZ, "FOR_REF"), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 5, TotalDoseGy = 10, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var result = svc.ComputeStructureDVH("Cord", structureAlphaBeta: 3.0);

            result.Statistics.VoxelCount.Should().Be(RefX * RefY * 2, "two contoured slices");
            result.Statistics.DMinGy.Should().BeApproximately(10, 1e-9,
                "the mask must land on CT slices 0-1 (10 Gy), not on slices 2-3 (0 Gy) where the foreign keys point");
            result.Statistics.DMaxGy.Should().BeApproximately(10, 1e-9);
        }

        [Fact]
        public async Task CacheStructureMasks_ContourOutsideReferenceCt_IsDropped()
        {
            // A contour at world z = 10 mm on a 2-slice CT (0..1 mm) has no slice to land on.
            var contours = new Dictionary<int, List<double[][]>>
            {
                [0] = new List<double[][]>
                {
                    new[] { new[] { -0.5, -0.5, 10.0 }, new[] { 3.6, -0.5, 10.0 }, new[] { 3.6, 3.6, 10.0 }, new[] { -0.5, 3.6, 10.0 } }
                }
            };
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(10)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData> { new StructureData { Id = "Far", ContoursBySlice = contours } });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.Physical, GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 5, TotalDoseGy = 10, Weight = 1.0, IsReference = true }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            svc.GetCachedStructureIds().Should().NotContain("Far", "a structure with no contour on this CT gets no mask");
            svc.ComputeStructureDVH("Far", 3.0).IsEmpty.Should().BeTrue();
        }

        // ── Plan weight ────────────────────────────────────────────────────

        /// <summary>
        /// Weight scales a plan's contribution after EQD2 conversion (it means "fraction of
        /// the course delivered": dose per fraction is unchanged). Pinned so a refactor that
        /// scaled the physical dose before conversion — a different number — is caught.
        /// </summary>
        [Fact]
        public async Task ComputeStructureDVH_PlanWeight_ScalesContributionAfterEQD2Conversion()
        {
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(0)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(FillDose(20)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "OAR", DicomType = "ORGAN", ContoursBySlice = BuildWholeVolumeStructure() }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.EQD2,
                GlobalAlphaBeta = 3.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "Ref",
                        NumberOfFractions = 5, TotalDoseGy = 10, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "Mov",
                        NumberOfFractions = 1, TotalDoseGy = 20, Weight = 0.5, IsReference = false }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            var computed = await svc.ComputeAsync(null, CancellationToken.None);

            // EQD2(20 Gy / 1 fx, α/β 3) = 20·(20+3)/5 = 92 Gy; × 0.5 = 46 Gy.
            // Scaling the dose first would give EQD2(10 Gy / 1 fx) = 10·13/5 = 26 Gy.
            computed.MaxDoseGy.Should().BeApproximately(46, 1e-9, "display sum");
            svc.ComputeStructurePlanDVH("Mov", "OAR", 3.0).Statistics.DMaxGy.Should().BeApproximately(46, 1e-9, "per-plan row");
            svc.ComputeStructureDVH("OAR", 3.0).Statistics.DMaxGy.Should().BeApproximately(46, 1e-9, "Σ row");
        }

        [Fact]
        public async Task ComputeStructurePlanDVH_HeterogeneousDose_DmeansAddUpAndDmaxDoesNot()
        {
            // Ref: gradient 0/10/20/30 Gy along x. Mov: 7 Gy everywhere, weight 0.5 → 3.5 Gy.
            var refDose = new int[RefZ][,];
            for (int z = 0; z < RefZ; z++)
            {
                refDose[z] = new int[RefX, RefY];
                for (int y = 0; y < RefY; y++)
                    for (int x = 0; x < RefX; x++)
                        refDose[z][x, y] = x * 10;
            }
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(refDose));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(FillDose(7)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "BODY", DicomType = "EXTERNAL", ContoursBySlice = BuildWholeVolumeStructure() }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = MakeConfig();
            config.Plans[1].Weight = 0.5;
            svc.PrepareData(config).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var refPart = svc.ComputeStructurePlanDVH("Ref", "BODY", 3.0).Statistics;
            var movPart = svc.ComputeStructurePlanDVH("Mov", "BODY", 3.0).Statistics;
            var total = svc.ComputeStructureDVH("BODY", 3.0).Statistics;

            refPart.DMeanGy.Should().BeApproximately(15, 1e-9);
            movPart.DMeanGy.Should().BeApproximately(3.5, 1e-9);
            // Each voxel of the Σ sum is the sum of the per-plan voxels, so the means add exactly …
            total.DMeanGy.Should().BeApproximately(refPart.DMeanGy + movPart.DMeanGy, 1e-9);
            // … while max/min are bounded by, not equal to, the sums of the parts in general.
            total.DMaxGy.Should().BeApproximately(33.5, 1e-9);
            total.DMaxGy.Should().BeLessOrEqualTo(refPart.DMaxGy + movPart.DMaxGy + 1e-9);
            total.DMinGy.Should().BeApproximately(3.5, 1e-9);
        }

        // ── Per-plan contribution ─────────────────────────────────────────

        [Fact]
        public async Task ComputeStructurePlanDVH_PhysicalMode_ReturnsEachPlansOwnContribution()
        {
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(3)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(FillDose(7)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "BODY", DicomType = "EXTERNAL", ContoursBySlice = BuildWholeVolumeStructure() }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();   // labels "Ref" and "Mov"
            await svc.ComputeAsync(null, CancellationToken.None);

            var refPart = svc.ComputeStructurePlanDVH("Ref", "BODY", structureAlphaBeta: 3.0);
            var movPart = svc.ComputeStructurePlanDVH("Mov", "BODY", structureAlphaBeta: 3.0);
            var total = svc.ComputeStructureDVH("BODY", structureAlphaBeta: 3.0);

            refPart.Statistics.DMaxGy.Should().BeApproximately(3, 1e-9);
            refPart.Statistics.VoxelCount.Should().Be(RefX * RefY * RefZ);
            movPart.Statistics.DMaxGy.Should().BeApproximately(7, 1e-9);
            total.Statistics.DMaxGy.Should().BeApproximately(10, 1e-9,
                "for a uniform dose the Σ Dmax equals the sum of the per-plan Dmax");
            refPart.Curve.Last().VolumePercent.Should().Be(0.0);

            svc.ComputeStructurePlanDVH("NoSuchPlan", "BODY", 3.0).IsEmpty.Should().BeTrue();
            svc.ComputeStructurePlanDVH("Ref", "NoSuchStructure", 3.0).IsEmpty.Should().BeTrue();
        }

        [Fact]
        public async Task ComputeStructurePlanDVH_EQD2Mode_ConvertsEachPlanWithItsOwnFractionation()
        {
            // Ref: 20 Gy in 1 fraction → EQD2(α/β=3) = 20·(20+3)/5 = 92 Gy.
            // Mov: 20 Gy in 10 fractions (2 Gy/fx) → EQD2 = 20 Gy at any α/β.
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(20)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(FillDose(20)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "OAR", DicomType = "ORGAN", ContoursBySlice = BuildWholeVolumeStructure() }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            var config = new SummationConfig
            {
                Method = SummationMethod.EQD2,
                GlobalAlphaBeta = 10.0,
                Plans = new List<SummationPlanEntry>
                {
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanRef", DisplayLabel = "C1 / PlanRef",
                        NumberOfFractions = 1, TotalDoseGy = 20, Weight = 1.0, IsReference = true },
                    new SummationPlanEntry { CourseId = "C1", PlanId = "PlanMov", DisplayLabel = "C1 / PlanMov",
                        NumberOfFractions = 10, TotalDoseGy = 20, Weight = 1.0, IsReference = false }
                }
            };
            svc.PrepareData(config).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            var refPart = svc.ComputeStructurePlanDVH("C1 / PlanRef", "OAR", structureAlphaBeta: 3.0);
            var movPart = svc.ComputeStructurePlanDVH("C1 / PlanMov", "OAR", structureAlphaBeta: 3.0);
            var total = svc.ComputeStructureDVH("OAR", structureAlphaBeta: 3.0);

            refPart.Statistics.DMaxGy.Should().BeApproximately(92, 1e-9);
            movPart.Statistics.DMaxGy.Should().BeApproximately(20, 1e-9);
            total.Statistics.DMaxGy.Should().BeApproximately(112, 1e-9);
            total.Statistics.DMeanGy.Should().BeApproximately(112, 1e-9);
        }

        /// <summary>
        /// Physical mode goes through the same method as EQD2 mode: plans are summed with
        /// their weights and no fractionation conversion.
        /// </summary>
        [Fact]
        public async Task ComputeStructureDVH_PhysicalMode_SumsPlansWithoutConversion()
        {
            var loader = new Mock<ISummationDataLoader>(MockBehavior.Strict);
            loader.Setup(l => l.LoadPlanDose("C1", "PlanRef", It.IsAny<double>())).Returns(MakeDoseData(FillDose(3)));
            loader.Setup(l => l.LoadPlanDose("C1", "PlanMov", It.IsAny<double>())).Returns(MakeDoseData(FillDose(7)));
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanRef")).Returns("FOR_REF");
            loader.Setup(l => l.GetPlanImageFOR("C1", "PlanMov")).Returns("FOR_REF");
            loader.Setup(l => l.LoadStructureContours(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(new List<StructureData>
                  {
                      new StructureData { Id = "BODY", DicomType = "EXTERNAL", ContoursBySlice = BuildWholeVolumeStructure() }
                  });

            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();   // Physical, weights 1.0
            await svc.ComputeAsync(null, CancellationToken.None);

            var result = svc.ComputeStructureDVH("BODY", structureAlphaBeta: 3.0);

            result.Statistics.VoxelCount.Should().Be(RefX * RefY * RefZ);
            result.Statistics.DMaxGy.Should().BeApproximately(10, 1e-9);
            result.Statistics.DMeanGy.Should().BeApproximately(10, 1e-9);
            result.Statistics.DMinGy.Should().BeApproximately(10, 1e-9);
        }

        // ── Round-trip: GetSummedSlice / GetStructureMask ──────────────────

        [Fact]
        public async Task GetSummedSlice_OutOfRangeIndex_ReturnsNull()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig()).Success.Should().BeTrue();
            await svc.ComputeAsync(null, CancellationToken.None);

            svc.GetSummedSlice(-1).Should().BeNull();
            svc.GetSummedSlice(RefZ).Should().BeNull();
            svc.GetSummedSlice(RefZ + 10).Should().BeNull();
        }

        [Fact]
        public void GetSummedSlice_BeforeCompute_ReturnsNull()
        {
            var loader = MakeLoader(refDoseGy: 1, movingDoseGy: 0);
            var svc = new SummationService(MakeReferenceCt(), loader.Object, new List<RegistrationData>());
            svc.PrepareData(MakeConfig());
            svc.GetSummedSlice(0).Should().BeNull("no compute → no data");
        }

        /// <summary>Builds a single polygon covering the entire reference slice, repeated for <paramref name="zCount"/> slices.</summary>
        private static Dictionary<int, List<double[][]>> BuildWholeVolumeStructure(int zCount = RefZ)
        {
            var dict = new Dictionary<int, List<double[][]>>();
            // Reference CT origin at (0,0,0), spacing 1×1×1, size 4×4×Z → slice extent 0..3 in x,y.
            // Build a closed square polygon that covers the full slice. Contour points are world mm.
            // The far edge sits at 3.6, not 3.5: the rasterizer samples row centres (y + 0.5) with a
            // half-open rule, so an edge exactly on 3.5 would exclude the last row.
            for (int z = 0; z < zCount; z++)
            {
                double zMm = z; // identity direction → z index == z mm
                var polygon = new double[][]
                {
                    new double[] { -0.5, -0.5, zMm },
                    new double[] {  3.6, -0.5, zMm },
                    new double[] {  3.6,  3.6, zMm },
                    new double[] { -0.5,  3.6, zMm },
                };
                dict[z] = new List<double[][]> { polygon };
            }
            return dict;
        }
    }
}
