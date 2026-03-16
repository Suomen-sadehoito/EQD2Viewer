using System.Collections.Generic;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using ESAPI_EQD2Viewer.Core.Models;

namespace ESAPI_EQD2Viewer.Core.Interfaces
{
    public interface IDVHService
    {
        /// <summary>
        /// Fetches cumulative DVH data for a structure from a plan.
        /// </summary>
        DVHData GetDVH(PlanSetup plan, Structure structure);

        /// <summary>
        /// Builds summary statistics for a structure's physical DVH.
        /// </summary>
        DVHSummary BuildPhysicalSummary(PlanSetup plan, Structure structure, DVHData dvhData);

        /// <summary>
        /// Builds summary statistics for a structure's EQD2 DVH.
        /// </summary>
        DVHSummary BuildEQD2Summary(PlanSetup plan, Structure structure, DVHData dvhData,
            int numberOfFractions, double alphaBeta, EQD2MeanMethod meanMethod);
    }
}
