using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Calculations;
using ESAPI_EQD2Viewer.Core.Interfaces;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Services
{
    public class DVHService : IDVHService
    {
        public DVHData GetDVH(PlanSetup plan, Structure structure)
        {
            return plan.GetDVHCumulativeData(structure,
                DoseValuePresentation.Absolute,
                VolumePresentation.Relative,
                0.01);
        }

        public DVHSummary BuildPhysicalSummary(PlanSetup plan, Structure structure, DVHData dvhData)
        {
            return new DVHSummary
            {
                StructureId = structure.Id,
                PlanId = plan.Id,
                Type = "Physical",
                DMax = ConvertToGy(dvhData.MaxDose),
                DMean = ConvertToGy(dvhData.MeanDose),
                DMin = ConvertToGy(dvhData.MinDose),
                Volume = dvhData.Volume
            };
        }

        public DVHSummary BuildEQD2Summary(PlanSetup plan, Structure structure, DVHData dvhData,
            int numberOfFractions, double alphaBeta, EQD2MeanMethod meanMethod)
        {
            double physDmax = ConvertToGy(dvhData.MaxDose);
            double physDmin = ConvertToGy(dvhData.MinDose);
            double physDmean = ConvertToGy(dvhData.MeanDose);

            double eqd2Dmax = EQD2Calculator.ToEQD2(physDmax, numberOfFractions, alphaBeta);
            double eqd2Dmin = EQD2Calculator.ToEQD2(physDmin, numberOfFractions, alphaBeta);

            double eqd2Dmean;
            if (meanMethod == EQD2MeanMethod.Differential)
            {
                // Convert curve data to Gy first if needed
                var curveInGy = dvhData.CurveData.Select(p => new DVHPoint(
                    new DoseValue(ConvertToGy(p.DoseValue), DoseValue.DoseUnit.Gy),
                    p.Volume, p.VolumeUnit)).ToArray();

                eqd2Dmean = EQD2Calculator.CalculateMeanEQD2FromDVH(curveInGy, numberOfFractions, alphaBeta);
            }
            else
            {
                eqd2Dmean = EQD2Calculator.ToEQD2(physDmean, numberOfFractions, alphaBeta);
            }

            return new DVHSummary
            {
                StructureId = structure.Id,
                PlanId = plan.Id,
                Type = "EQD2",
                DMax = eqd2Dmax,
                DMean = eqd2Dmean,
                DMin = eqd2Dmin,
                Volume = dvhData.Volume
            };
        }

        private static double ConvertToGy(DoseValue dv)
        {
            if (dv.Unit == DoseValue.DoseUnit.cGy)
                return dv.Dose / 100.0;
            return dv.Dose;
        }
    }
}
