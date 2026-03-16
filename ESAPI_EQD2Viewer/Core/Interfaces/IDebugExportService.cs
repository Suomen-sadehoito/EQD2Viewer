using VMS.TPS.Common.Model.API;

namespace ESAPI_EQD2Viewer.Core.Interfaces
{
    public interface IDebugExportService
    {
        void ExportDebugLog(ScriptContext context, PlanSetup plan, int currentSlice);
    }
}
