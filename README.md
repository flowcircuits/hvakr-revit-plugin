# HVAKR Revit Plugin

A Revit add-in that round-trips MEP spaces between [Revit](https://www.autodesk.com/products/revit) and the [HVAKR](https://www.hvakr.com) cloud platform, so mechanical engineers can run load calcs and equipment sizing without re-keying the model.

## Prerequisites

- Visual Studio 2022 (17.9+) or JetBrains Rider
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Autodesk Revit 2025 or 2026 for runtime testing inside Revit
- [Inno Setup 6](https://jrsoftware.org/download.php/is.exe?site=1) — only needed to package or locally install a release

Debug builds are portable on Windows and do not require Revit to be installed. The Revit API references come from pinned compile-only NuGet packages; Revit supplies the actual API DLLs at runtime.

## Projects

| Project                   | Purpose                                                                          |
| ------------------------- | -------------------------------------------------------------------------------- |
| `HVAKR.Api`               | `HttpClient` wrapper around the HVAKR REST API — no Revit dependency             |
| `HVAKR.Api.Tests`         | xUnit integration tests (hit the real API — read token from `HVAKR_ACCESS_TOKEN`) |
| `HVAKR.Revit`             | Revit add-in entry point — ribbon, dockable pane registration                    |
| `HVAKR.Revit.UI`          | WPF pane + `IExternalEventHandler`s — the actual plugin UI                       |
| `HVAKR.Revit.UI.Harness`  | Standalone WPF app that hosts the pane — **develop here, not in Revit**          |

## Build & run

```powershell
# Fast iteration for login/project-list UI — no Revit needed
dotnet run --project HVAKR.Revit.UI.Harness

# Compile the plugin
dotnet build HVAKR.Revit.sln -c Debug

# A normal Release build never installs or closes Revit
dotnet build HVAKR.Revit.sln -c Release

# Package an installer without installing it
dotnet msbuild HVAKR.Revit/HVAKR.Revit.csproj /t:BuildInstaller /p:Configuration=Release /p:VersionPrefix=0.1.0 /p:VersionSuffix=

# Refuses to continue while Revit is open, then installs silently
dotnet msbuild HVAKR.Revit/HVAKR.Revit.csproj /t:InstallLocal /p:Configuration=Release /p:VersionPrefix=0.1.0 /p:VersionSuffix=

# Tests (set HVAKR_ACCESS_TOKEN to run them; otherwise they no-op)
dotnet test HVAKR.Api.Tests
```

## CI

GitHub Actions builds the full solution on `windows-latest` in `Debug`:

- restores NuGet packages
- builds `HVAKR.Revit.sln`
- runs `HVAKR.Api.Tests`

Pull-request CI also builds unsigned `0.1.0` and `0.1.1` test installers and verifies per-user installation, in-place upgrade, absence of Revit API assemblies, and complete uninstall. Revit behavior still needs manual validation in Revit 2025/2026.

The manually dispatched `Release` workflow accepts a strict `major.minor.patch` version and runs only from `master`. Its protected `production` job signs the plugin, updater, and installer with Azure Artifact Signing, publishes a GitHub Release and the public GCS objects, and writes `revit/latest.json` last.

Fresh installs are per-user under `%LocalAppData%` with manifests under `%AppData%`. Upgrades from the prior machine-wide installer preserve its all-users install mode and `%ProgramData%` manifests, avoiding duplicate add-ins while retaining the existing installation path.

Repository setup required before the first `0.1.0` release:

- Protect the `production` GitHub environment with required reviewers.
- Add environment secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `GCP_WORKLOAD_IDENTITY_PROVIDER`, and `GCP_SERVICE_ACCOUNT`.
- Add environment variables `AZURE_ARTIFACT_SIGNING_ENDPOINT`, `AZURE_ARTIFACT_SIGNING_ACCOUNT`, and `AZURE_ARTIFACT_SIGNING_CERTIFICATE_PROFILE`.
- Grant the Azure workload identity only the Artifact Signing Certificate Profile Signer role. Grant the GCP workload identity write access only under `gs://hvakr-desktop-releases/revit/`.

Release objects are public at `revit/releases/X.Y.Z/HVAKR-Revit-Plugin-X.Y.Z.exe`, `revit/HVAKR-Revit-Plugin.exe`, and `revit/latest.json`. Versioned installers are immutable; the stable alias and manifest use `no-cache`.

### Testing in Revit

1. Run the explicit `InstallLocal` target after closing every Revit process.
2. Launch Revit 2025 or 2026. The `HVAKR` tab should appear on the ribbon.
3. Click **Show** to open the dockable pane. Paste an HVAKR API key to log in.

## Installer

- Script: [`HVAKR.Revit/Installer/hvakr-installer.iss`](HVAKR.Revit/Installer/hvakr-installer.iss)
- Fresh installs run without elevation in `%LocalAppData%\Programs\HVAKR\Revit Plugin` and write `.addin` manifests into `%AppData%\Autodesk\Revit\Addins\{2025,2026}`
- Upgrades from the legacy all-users install retain its existing Program Files location and `%ProgramData%` manifests
- Packages the self-contained `HVAKR.Revit.Updater.exe`; update metadata and staging live under `%LocalAppData%\HVAKR\Revit Plugin`
- Local builds report `0.0.0-dev`; release automation passes one version through assemblies, installer metadata, filenames, tag, and update manifest

## Conventions

Coding conventions, Revit-API patterns, and the HVAKR wire contract live in [AGENTS.md](AGENTS.md). Skills for AI collaborators are in [`.agents/skills/`](./.agents/skills/).

## License

Licensed under the [Apache License 2.0](LICENSE). "HVAKR" is a trademark of Flow Circuits, Inc.; see [NOTICE](NOTICE).
