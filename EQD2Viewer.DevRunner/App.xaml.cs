using System;
using System.IO;
using System.Linq;
using System.Windows;
using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Interfaces;
using EQD2Viewer.Core.Logging;

namespace EQD2Viewer.DevRunner
{
    /// <summary>
    /// Standalone development launcher for EQD2 Viewer.
    /// Replaces Eclipse's Script.Execute() entry point.
    /// Loads clinical data from JSON fixtures instead of live ESAPI.
    /// 
    /// Supports two fixture formats:
    ///   1. Snapshot format (snapshot_meta.json) — full ClinicalSnapshot from SnapshotSerializer
    ///   2. Test fixture format (metadata.json) — selective data from FixtureExporter
    /// 
    /// Uses AppLauncher for all service/ViewModel/Window wiring —
    /// this class only handles fixture discovery and snapshot loading.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
      base.OnStartup(e);
            try
     {
        SimpleLogger.EnableFileLogging("EQD2Viewer_Dev.log");
     SimpleLogger.Info("=== DevRunner starting ===");

  // — 1. Find fixture directory —
      string fixturePath = ResolveFixturePath(e.Args);
                if (fixturePath == null)
  {
 MessageBox.Show(
       "No fixture directory found.\n\n" +
        "Usage:\n" +
      "  EQD2Viewer.DevRunner.exe <fixture_path>\n\n" +
           "Or place fixtures in TestFixtures/ next to the exe.\n\n" +
              "Generate fixtures by running FixtureGenerator in Eclipse.",
            "EQD2 Viewer — DevRunner",
       MessageBoxButton.OK, MessageBoxImage.Information);

   Shutdown(1);
           return;
           }

    SimpleLogger.Info($"Using fixtures: {fixturePath}");

   // — 2. Load clinical data — auto-detect format —
     IClinicalDataSource dataSource;
                if (EQD2Viewer.Fixtures.JsonDataSource.IsSnapshotDirectory(fixturePath))
                {
              SimpleLogger.Info("Detected snapshot format");
           dataSource = new EQD2Viewer.Fixtures.JsonDataSource(fixturePath);
         }
          else
    {
         SimpleLogger.Info("Detected test fixture format");
           dataSource = new FixtureFormatDataSource(fixturePath);
                }

                ClinicalSnapshot snapshot = dataSource.LoadSnapshot();

             SimpleLogger.Info($"Snapshot: {snapshot.Patient.Id} | " +
            $"{snapshot.ActivePlan.CourseId}/{snapshot.ActivePlan.Id} | " +
          $"{snapshot.ActivePlan.TotalDoseGy:F1} Gy / {snapshot.ActivePlan.NumberOfFractions} fx");

   // — 3. Launch via the shared composition root —
         EQD2Viewer.App.AppLauncher.Launch(
          snapshot,
    summationLoader: null,
         windowTitle: "[DEV MODE — Fixture Data]",
        useShowDialog: false);

     SimpleLogger.Info("DevRunner UI launched successfully");
            }
   catch (Exception ex)
      {
     SimpleLogger.Error("DevRunner startup failed", ex);
     MessageBox.Show(
  $"Startup error:\n\n{ex.Message}\n\n{ex.StackTrace}",
      "EQD2 Viewer — DevRunner Error",
       MessageBoxButton.OK, MessageBoxImage.Error);
   Shutdown(1);
       }
  }

        /// <summary>
        /// Resolves fixture directory from command line args or auto-discovery.
        /// Supports both snapshot format (snapshot_meta.json) and test fixture format (metadata.json).
        /// </summary>
        private static string ResolveFixturePath(string[] args)
        {
            if (args != null && args.Length > 0 && Directory.Exists(args[0]))
    return args[0];

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

    // Look for TestFixtures/ next to exe
    string localFixtures = Path.Combine(baseDir, "TestFixtures");
  if (Directory.Exists(localFixtures))
            {
 string first = Directory.GetDirectories(localFixtures)
      .FirstOrDefault(d => IsFixtureDirectory(d));
    if (first != null) return first;
       }

          // Walk up the directory tree to find TestFixtures in the project
            string dir = baseDir;
            for (int i = 0; i < 8; i++)
            {
      string candidate = Path.Combine(dir, "EQD2Viewer.Tests", "TestFixtures");
     if (Directory.Exists(candidate))
       {
      string first = Directory.GetDirectories(candidate)
            .FirstOrDefault(d => IsFixtureDirectory(d));
             if (first != null) return first;
            }

       dir = Path.GetDirectoryName(dir);
 if (dir == null) break;
        }

      return null;
        }

        private static bool IsFixtureDirectory(string dir)
     {
          return File.Exists(Path.Combine(dir, "metadata.json"))
    || EQD2Viewer.Fixtures.JsonDataSource.IsSnapshotDirectory(dir);
        }
    }
}