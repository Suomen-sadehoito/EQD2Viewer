using EQD2Viewer.Core.Calculations;
using EQD2Viewer.Core.Models;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Data;
using System.Linq;

namespace EQD2Viewer.Services
{
    public class DVHService : IDVHCalculation
    {
        public DVHSummary BuildPhysicalSummaryFromCurve(DvhCurveData dvh, string planId)
        {
            return new DVHSummary
            {
                StructureId = dvh.StructureId,
                PlanId = planId,
                Type = "Physical",
                Source = DVHSummary.SourceEclipse,
                DMax = dvh.DMaxGy,
                DMean = dvh.DMeanGy,
                DMin = dvh.DMinGy,
                Volume = dvh.VolumeCc
            };
        }

        public DVHSummary BuildEQD2SummaryFromCurve(DvhCurveData dvh, string planId,
            int numberOfFractions, double alphaBeta)
        {
            double eqd2Dmax = EQD2Calculator.ToEQD2(dvh.DMaxGy, numberOfFractions, alphaBeta);
            double eqd2Dmin = EQD2Calculator.ToEQD2(dvh.DMinGy, numberOfFractions, alphaBeta);
            double eqd2Dmean;
            string type = "EQD2";

            if (dvh.Curve != null && dvh.Curve.Length >= 2)
            {
                // Convert every dose level of the curve, then take the volume-weighted mean.
                // This is the same "convert each element, then average" that the voxel-based
                // summation does, so single-plan and Σ rows agree in method.
                var curvePoints = dvh.Curve.Select(p => new DoseVolumePoint(p[0], p[1])).ToArray();
                eqd2Dmean = EQD2Calculator.CalculateMeanEQD2FromDVH(curvePoints, numberOfFractions, alphaBeta);
            }
            else
            {
                // No curve data — EQD2 of the physical mean is the only figure available.
                // Exact for a uniform dose; an underestimate for a heterogeneous one, so the
                // row says which method produced it.
                eqd2Dmean = EQD2Calculator.ToEQD2(dvh.DMeanGy, numberOfFractions, alphaBeta);
                type = "EQD2 (of mean)";
            }

            return new DVHSummary
            {
                StructureId = dvh.StructureId,
                PlanId = planId,
                Type = type,
                Source = DVHSummary.SourceEclipse,
                DMax = eqd2Dmax,
                DMean = eqd2Dmean,
                DMin = eqd2Dmin,
                Volume = dvh.VolumeCc
            };
        }
    }
}
