# EQD2 Viewer

A WPF research viewer for radiotherapy dose. It shows isodose lines and colorwash
on the CT, converts dose to EQD2, draws DVHs, and sums several plans onto one
reference CT grid for re-irradiation assessment.

It runs two ways: as an ESAPI script inside Varian Eclipse, or as a standalone
Windows app that reads exported JSON instead of touching Eclipse.

## Disclaimer

Research code, not a medical device. No CE mark, no FDA clearance, not validated
for clinical use. Do not use it to make or
support patient treatment decisions. Check anything that matters against a
validated system.

*Suomeksi: tämä on tutkimusprototyyppi, ei lääkinnällinen laite. Älä käytä sitä
potilaan hoitopäätöksiin. Käyttäjä vastaa itse käytöstään.*

## How the projects fit together

The math and the UI know nothing about Eclipse. Everything ESAPI-specific is
walled off in one project, and the rest of the app only ever sees a plain
`ClinicalSnapshot` object.

| Project | What it is | ESAPI? | WPF? |
|---|---|---|---|
| `EQD2Viewer.Core` | Domain types, EQD2 math, DVH binning, marching squares, matrix math | no | no |
| `EQD2Viewer.Services` | DVH service, dose summation engine | no | no |
| `EQD2Viewer.App` | The actual WPF UI and view models | no | yes |
| `EQD2Viewer.Esapi` | Eclipse script entry point. Reads the live plan, builds a snapshot, launches the UI | yes | yes |
| `EQD2Viewer.FixtureGenerator` | Separate Eclipse script that dumps a snapshot to JSON on disk | yes | no |
| `EQD2Viewer.DevRunner` | Standalone host. Loads a JSON snapshot and runs the same UI without Eclipse | no | yes |
| `EQD2Viewer.Fixtures` | Reads the JSON snapshot format | no | no |
| `EQD2Viewer.Stubs` | Fake ESAPI types so CI can compile without Varian DLLs | no | no |
| `EQD2Viewer.Tests` | Unit / integration / smoke tests | no | no |

`Directory.Build.props` enforces this split at build time: only `EQD2Viewer.Esapi`,
`EQD2Viewer.Stubs`, and `EQD2Viewer.FixtureGenerator` are allowed to reference
`VMS.TPS.*`. If any other project starts referencing ESAPI, the build fails with a
clear error instead of letting the dependency creep in.

## Prerequisites

- Windows
- .NET Framework 4.8 (dev pack / targeting pack)
- Visual Studio 2022 (17.x) with the **.NET desktop development** workload,
  or MSBuild 17+ on the command line
- Eclipse + your Varian ESAPI DLLs — only needed if you want to build the Eclipse
  scripts or run against live data

## ESAPI DLLs

Varian binaries are not in this repo and are not redistributed. To build the
Eclipse-facing projects against the real API, copy these two files out of your
Eclipse installation into `lib/ESAPI/`:

```
lib/ESAPI/VMS.TPS.Common.Model.API.dll
lib/ESAPI/VMS.TPS.Common.Model.Types.dll
```

Use the versions that match the Eclipse you deploy to. The folder is gitignored
apart from a `.gitkeep`.

The build detects these automatically (`HasRealEsapi`):

- **DLLs present** → `EQD2Viewer.Esapi` and `EQD2Viewer.FixtureGenerator` compile
  against the real API.
- **DLLs missing** (or `CI=true`) → they compile against `EQD2Viewer.Stubs`
  instead. The solution still builds, tests still run, and DevRunner still works —
  but a plugin built this way is stub-linked and won't run in Eclipse.

So: no ESAPI DLLs needed for the math, the tests, or DevRunner. ESAPI DLLs needed
for a working Eclipse plugin.

## Building in Visual Studio

1. Get the source and put the ESAPI DLLs in `lib/ESAPI/` (see above) if you need
   the Eclipse scripts.
2. Open `EQD2Viewer.sln`.
3. Set the platform to **x64**. Pick **Debug** for development or **Release** for
   a deployable plugin.
4. Build the solution. NuGet restore runs on first build.

Output lands in `BuildOutput/<Configuration>/` (set in `Directory.Build.props`),
not in each project's own `bin`.

In **Release** only, `Directory.Build.targets` sorts the deliverables:

```
BuildOutput/01_Eclipse_ESAPI_Plugins/   <- the .esapi plugin DLLs
BuildOutput/02_Standalone_Runner/        <- DevRunner.exe + bundled TestFixtures
```

Command line equivalent:

```
dotnet build EQD2Viewer.sln -c Debug
```

## Running the Eclipse script

The plugin assembly is `EQD2Viewer.App.esapi.dll`. Costura bundles its
dependencies into that single file (ESAPI itself is excluded — Eclipse provides
it). Build in Release, take the DLL from `BuildOutput/01_Eclipse_ESAPI_Plugins/`,
and register it in Eclipse the way your site handles scripts.

Before you run it, set up the Eclipse context correctly. The script reads the
**active external plan**, so:

- Open the **patient**.
- Have the **CT image** selected.
- Make a **normal external beam plan active** — one that has a calculated dose.
  Not a Plan Sum. The viewer pulls dose, structures and DVHs from that single
  active plan.

The active plan becomes the starting point; you bring in other plans later through the summation
dialog.

## Plan summation

Summation is for stacking dose from several plans onto one CT — typically a
re-irradiation case where an old course and a new course need to be looked at
together in EQD2.

In the summation dialog:

- Every plan with a dose, across all of the patient's courses, is listed.
- Tick the plans you want to include (at least two).
- Mark one as the **reference**. Its CT grid is the base everything is resampled
  onto.
- Set fractions and α/β so each plan is converted to EQD2 with its own
  fractionation before being summed.

About registrations: **the viewer does not compute any registration.** It reuses
the ordinary rigid registrations already stored on the patient — the ones made in
Eclipse's registration workspace during contouring/planning. Only affine (rigid)
registrations are supported; deformable was removed in 0.9.4.

So when you add a plan that sits on a **different CT (different frame of
reference)** than the reference plan, pick the matching registration in the
dialog's Registration column to map its dose onto the reference grid. If a plan
shares the reference plan's CT, no registration is needed — choose "same CT as
reference". The dialog only offers registrations whose source/target frames of
reference actually connect the two plans, and warns you if you try to sum across
different CTs with nothing selected.

The "Registration diagnostics" expander prints why each plan got the
registrations it did, which is useful when an expected one doesn't show up.

## Running without Eclipse (DevRunner)

DevRunner runs the full UI from a JSON snapshot on disk, so you can develop and
demo on any Windows machine.

```
BuildOutput/Debug/EQD2Viewer.DevRunner.exe <snapshot_or_fixture_directory>
```

With no argument it auto-discovers a fixture under `TestFixtures/`. Synthetic
example fixtures live in `EQD2Viewer.Tests/TestFixtures/`.

`--validate` loads the snapshot, checks a few invariants, and exits without a
window (exit 0 = ok). The smoke tests use this.

Note: summation needs live ESAPI data access, so it is disabled in DevRunner. The
button is there but tells you it's unavailable in this mode. Everything else
(isodose, EQD2 display, DVH, windowing) works from the fixture.

## Generating snapshots and fixtures

`EQD2Viewer.FixtureGenerator.esapi` is a second Eclipse script that exports the
JSON DevRunner reads. Deploy it the same way as the main plugin.

It handles both a single plan and a Plan Sum being active. When you run it, it
asks which kind of export you want:

- **Full Snapshot** — complete CT + dose voxels (~25–65 MB). Use this to verify
  that the app shows the same thing from JSON as it does from live Eclipse, on
  another machine.
- **Test Fixtures** — selective, lightweight data (~1 MB) for unit and
  integration tests. Copy the folder into `EQD2Viewer.Tests/TestFixtures/`.

If you want to feed the viewer real patient data, generate the fixtures yourself
under whatever access and consent your local rules require.

## Tests

```
dotnet test EQD2Viewer.Tests/EQD2Viewer.Tests.csproj
```

or run them from Test Explorer in Visual Studio. The suite covers most of the
algorithmic core. Passing tests mean the code does what was specified — not that
the numbers are clinically correct.

## Status

**0.9.4-beta** (April 2026).

- 0.9.2 / 0.9.3 added a SimpleITK deformable registration module.
- 0.9.4 pulled it back out and narrowed the scope to affine-only summation.
- Earlier 0.x betas were the project-layout split and the first feature work.

## Authors

Risto Hirvilammi & Juho Ala-Myllymäki, in a personal / research capacity.

## Licence

MIT — see [`LICENSE.txt`](LICENSE.txt).
