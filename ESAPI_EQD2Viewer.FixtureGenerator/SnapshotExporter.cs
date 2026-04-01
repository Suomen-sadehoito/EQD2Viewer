using System;
using System.IO;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Serialization;
using VMS.TPS.Common.Model.API;

namespace ESAPI_EQD2Viewer.FixtureGenerator
{
    /// <summary>
    /// Serializes a pre-loaded ClinicalSnapshot to a JSON directory using SnapshotSerializer.
    ///
    /// The caller (Script.cs) is responsible for loading the ClinicalSnapshot via
    /// EsapiDataSource — this class only handles the serialization step.
    /// This avoids a circular project reference (FixtureGenerator ? EQD2Viewer.Esapi).
    ///
    /// Compared to the existing FixtureExporter (targeted testing tool):
    ///   FixtureExporter   ? selective fixture files for unit/integration tests (~1 MB)
    ///   SnapshotExporter  ? full ClinicalSnapshot for end-to-end QA (~25–65 MB)
    ///
    /// Output: Desktop\EQD2_Snapshots\{PatientId}_{CourseId}_{PlanId}_snapshot\
    ///
    /// Typical file sizes:
    ///   CT voxels:    ~50–150 MB raw ? ~20–50 MB with RLE compression
    ///   Dose voxels:  ~2–10 MB
    ///   Structures:   ~1–5 MB
    ///   Total:        ~25–65 MB per snapshot
    /// </summary>
    public class SnapshotExporter
    {
        /// <summary>
        /// Serializes the given snapshot to <paramref name="outputDir"/>.
        /// Returns a human-readable export summary.
        /// </summary>
        public string ExportSnapshot(ClinicalSnapshot snapshot, string outputDir)
        {
            return SnapshotSerializer.Write(snapshot, outputDir);
        }

        public static string BuildOutputDirName(string patientId, string courseId, string planId)
        {
            string label = SanitizePath($"{patientId}_{courseId}_{planId}_snapshot");
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "EQD2_Snapshots",
                label);
        }

        private static string SanitizePath(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
