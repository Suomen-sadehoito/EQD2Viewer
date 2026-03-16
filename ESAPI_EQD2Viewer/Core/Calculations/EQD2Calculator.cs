using System.Linq;
using VMS.TPS.Common.Model.Types;

namespace ESAPI_EQD2Viewer.Core.Calculations
{
    /// <summary>
    /// Unified EQD2 calculator for both voxel-level isodose rendering and DVH curve conversion.
    /// EQD2 = D × (d + α/β) / (2 + α/β), where d = D/n
    /// </summary>
    public static class EQD2Calculator
    {
        /// <summary>
        /// Converts a single total dose value to EQD2.
        /// Used for both individual voxels (isodose rendering) and DVH point conversion.
        /// </summary>
        /// <param name="totalDoseGy">Total physical dose in Gy at this point</param>
        /// <param name="numberOfFractions">Number of treatment fractions</param>
        /// <param name="alphaBeta">α/β ratio in Gy (e.g. 10.0 for tumor, 3.0 for late-responding tissue)</param>
        /// <returns>EQD2 dose in Gy</returns>
        public static double ToEQD2(double totalDoseGy, int numberOfFractions, double alphaBeta)
        {
            if (numberOfFractions <= 0 || alphaBeta <= 0)
                return totalDoseGy;

            double dosePerFraction = totalDoseGy / numberOfFractions;
            return totalDoseGy * (dosePerFraction + alphaBeta) / (2.0 + alphaBeta);
        }

        /// <summary>
        /// Pre-calculates scaling factors for fast voxel-level EQD2 conversion during rendering.
        /// Since EQD2 = D * (D/n + αβ) / (2 + αβ) = D²/(n*(2+αβ)) + D*αβ/(2+αβ),
        /// this can be expressed as: EQD2 = A*D² + B*D where A and B are constants.
        /// This avoids division per-voxel in the hot rendering loop.
        /// </summary>
        public static void GetVoxelScalingFactors(int numberOfFractions, double alphaBeta,
            out double quadraticFactor, out double linearFactor)
        {
            double denom = 2.0 + alphaBeta;
            if (numberOfFractions <= 0 || denom <= 0)
            {
                quadraticFactor = 0;
                linearFactor = 1.0;
                return;
            }

            // EQD2 = D * (D/n + αβ) / (2 + αβ)
            //       = D²/(n*(2+αβ)) + D*αβ/(2+αβ)
            //       = A*D² + B*D
            quadraticFactor = 1.0 / (numberOfFractions * denom);
            linearFactor = alphaBeta / denom;
        }

        /// <summary>
        /// Fast inline EQD2 conversion using pre-calculated factors.
        /// Call GetVoxelScalingFactors once, then use this per-voxel.
        /// </summary>
        public static double ToEQD2Fast(double totalDoseGy, double quadraticFactor, double linearFactor)
        {
            return totalDoseGy * totalDoseGy * quadraticFactor + totalDoseGy * linearFactor;
        }

        /// <summary>
        /// Converts an entire cumulative DVH curve to EQD2 form.
        /// Each dose point on the curve is independently converted.
        /// </summary>
        public static DVHPoint[] ConvertCurveToEQD2(DVHPoint[] originalCurve, int numberOfFractions, double alphaBeta)
        {
            if (originalCurve == null || originalCurve.Length == 0)
                return new DVHPoint[0];

            return originalCurve.Select(p => new DVHPoint(
                new DoseValue(ToEQD2(p.DoseValue.Dose, numberOfFractions, alphaBeta), DoseValue.DoseUnit.Gy),
                p.Volume,
                p.VolumeUnit
            )).ToArray();
        }

        /// <summary>
        /// Calculates biological mean dose (EQD2 Dmean) using differential DVH weighting.
        /// More accurate than simply converting the physical Dmean, especially for heterogeneous dose distributions.
        /// </summary>
        public static double CalculateMeanEQD2FromDVH(DVHPoint[] cumulativeCurve, int numberOfFractions, double alphaBeta)
        {
            if (cumulativeCurve == null || cumulativeCurve.Length < 2)
                return 0.0;

            double totalVolume = cumulativeCurve.First().Volume;
            if (totalVolume <= 0)
                return 0.0;

            double totalBioDose = 0;

            for (int i = 0; i < cumulativeCurve.Length - 1; i++)
            {
                DVHPoint p1 = cumulativeCurve[i];
                DVHPoint p2 = cumulativeCurve[i + 1];
                double volumeSegment = p1.Volume - p2.Volume;

                if (volumeSegment > 0)
                {
                    double midDose = (p1.DoseValue.Dose + p2.DoseValue.Dose) / 2.0;
                    double eqd2Segment = ToEQD2(midDose, numberOfFractions, alphaBeta);
                    totalBioDose += eqd2Segment * volumeSegment;
                }
            }

            return totalBioDose / totalVolume;
        }
    }
}
