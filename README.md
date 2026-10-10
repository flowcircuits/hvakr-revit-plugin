# HVAKR Revit Plugin

A Revit add-in that round-trips MEP spaces between [Revit](https://www.autodesk.com/products/revit) and the [HVAKR](https://www.hvakr.com) cloud platform, so mechanical engineers can run load calcs and equipment sizing without re-keying the model.

## Prerequisites

- Visual Studio 2022 (17.9+) or JetBrains Rider
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Autodesk Revit 2025 or 2026 for runtime testing inside Revit

Builds do not require Revit to be installed. On macOS/Linux, add `-p:EnableWindowsTargeting=true`. The Revit API references come from pinned compile-only NuGet packages; Revit supplies the actual API DLLs at runtime.

## Projects

| Project                   | Purpose                                                                          |
| ------------------------- | -------------------------------------------------------------------------------- |
| `HVAKR.Api`               | `HttpClient` wrapper around the HVAKR REST API — no Revit dependency             |
| `HVAKR.Api.Tests`         | xUnit integration tests (hit the real API — read token from `HVAKR_ACCESS_TOKEN`) |
| `HVAKR.Revit`             | Revit add-in entry point — ribbon, dockable pane registration                    |
| `HVAKR.Revit.UI`          | WPF pane + `IExternalEventHandler`s — the actual plugin UI                       |
| `HVAKR.Revit.UI.Harness`  | Standalone WPF app that hosts the pane — **develop here, not in Revit**          |

## Install

The plugin ships as an Autodesk Autoloader bundle: `HVAKR-Revit-Plugin-<version>.zip` with `HVAKR.bundle` at its root. It supports Revit 2025 and 2026. No installer and no admin rights are needed.

1. If you used the old HVAKR installer (`.exe`), remove it first. See [Removing the old installer](#removing-the-old-installer). Otherwise Revit finds two add-ins with the same ID and reports an error.
2. Close Revit.
3. Download `HVAKR-Revit-Plugin.zip` from `https://storage.googleapis.com/hvakr-desktop-releases/revit/HVAKR-Revit-Plugin.zip`.
4. Extract it. Move the `HVAKR.bundle` folder into `%AppData%\Autodesk\ApplicationPlugins`. Create that folder if it does not exist. The result is `%AppData%\Autodesk\ApplicationPlugins\HVAKR.bundle\PackageContents.xml`.
5. Start Revit. The **HVAKR** tab appears on the ribbon.

To update, close Revit, delete `%AppData%\Autodesk\ApplicationPlugins\HVAKR.bundle`, and repeat steps 3–5. To uninstall, close Revit and delete that folder.

### Removing the old installer

Older versions used an Inno Setup installer. Depending on the version, it installed for all users or only for you. Remove both kinds:

1. Close Revit.
2. Open **Settings → Apps → Installed apps**. Uninstall **HVAKR Revit Plugin** if it is listed.
3. Delete any of these files that remain. The oldest installer did not remove them on uninstall.
   - `%ProgramData%\Autodesk\Revit\Addins\2025\HVAKR.addin`
   - `%ProgramData%\Autodesk\Revit\Addins\2026\HVAKR.addin`
   - `%AppData%\Autodesk\Revit\Addins\2025\HVAKR.addin`
   - `%AppData%\Autodesk\Revit\Addins\2026\HVAKR.addin`
4. Delete any of these folders that remain:
   - `C:\Program Files (x86)\HVAKR`
   - `C:\Program Files\HVAKR`
   - `%LocalAppData%\Programs\HVAKR\Revit Plugin`

Deleting files under `%ProgramData%` and `Program Files` needs administrator rights. IT can run this in an elevated PowerShell, signed in as each affected user:

```powershell
Get-Process Revit -ErrorAction SilentlyContinue | ForEach-Object { throw "Close Revit first." }
foreach ($root in @($env:ProgramData, $env:APPDATA)) {
    foreach ($year in 2025, 2026) {
        Remove-Item "$root\Autodesk\Revit\Addins\$year\HVAKR.addin" -ErrorAction SilentlyContinue
    }
}
Remove-Item -Recurse -Force "${env:ProgramFiles(x86)}\HVAKR", "$env:ProgramFiles\HVAKR", "$env:LOCALAPPDATA\Programs\HVAKR\Revit Plugin" -ErrorAction SilentlyContinue
```

This leaves the uninstall entry in **Installed apps** if step 2 was skipped. The log and update state under `%LocalAppData%\HVAKR` are kept.

## Build & run

```powershell
# Fast iteration for login/project-list UI — no Revit needed
dotnet run --project HVAKR.Revit.UI.Harness

# Compile the plugin
dotnet build HVAKR.Revit.sln -c Debug

# A normal Release build never installs or closes Revit
dotnet build HVAKR.Revit.sln -c Release

# Package HVAKR.Revit/Deploy/HVAKR-Revit-Plugin-0.1.0.zip without installing it
dotnet msbuild HVAKR.Revit/HVAKR.Revit.csproj /t:BuildBundle /p:Configuration=Release /p:VersionPrefix=0.1.0 /p:VersionSuffix=

# Verify a zip the way CI does
pwsh scripts/Test-Bundle.ps1 -Zip HVAKR.Revit/Deploy/HVAKR-Revit-Plugin-0.1.0.zip -Version 0.1.0

# Copy HVAKR.bundle into %AppData%\Autodesk\ApplicationPlugins. Refuses while Revit is open or the old installer's manifests exist.
dotnet msbuild HVAKR.Revit/HVAKR.Revit.csproj /t:InstallLocal /p:Configuration=Release /p:VersionPrefix=0.1.0 /p:VersionSuffix=

# Tests (set HVAKR_ACCESS_TOKEN to run them; otherwise they no-op)
dotnet test HVAKR.Api.Tests
```

## Bundle

- Templates: [`HVAKR.Revit/Bundle/PackageContents.xml`](HVAKR.Revit/Bundle/PackageContents.xml) and [`HVAKR.Revit/Bundle/HVAKR.addin`](HVAKR.Revit/Bundle/HVAKR.addin)
- `PrepareBundle` stages `HVAKR.Revit/Deploy/Bundle/HVAKR.bundle` with one `Contents/<year>` folder per supported Revit release. `BuildBundle` zips it.
- Each `.addin` uses a relative `Assembly` path, so the bundle works from any `ApplicationPlugins` folder.
- `UpgradeCode` never changes. `ProductCode` is derived from the version, so each release gets a new one.
- To support a new Revit release, add a `Components` entry, a `Copy` pair in `PrepareBundle`, and the series in `scripts/Test-Bundle.ps1`.
- Local builds report `0.0.0-dev`. Release automation passes one version through the assemblies, `PackageContents.xml`, zip name, tag, and `latest.json`.

## CI

GitHub Actions runs on `windows-2025`, the same image as the release workflow:

- restores NuGet packages
- builds `HVAKR.Revit.sln` in `Debug` and runs `HVAKR.Api.Tests`
- runs `scripts/Build-Release.ps1`, the same release-path build that `release.yml` uses, to produce an unsigned bundle
- runs `scripts/Test-Bundle.ps1`: zip layout, `PackageContents.xml`, each `.addin` resolving to an assembly that defines `HVAKR.Revit.App`, and one version across every HVAKR DLL
- uploads the unsigned zip as a 7-day workflow artifact for manual testing in Revit
- compiles the plugin against both the Revit 2025 and 2026 API packages
- lints the workflows with actionlint

Revit behavior still needs manual validation in Revit 2025/2026.

## Releasing

Releases are deliberate. Merging to `master` only builds and tests.

1. Run the **Release** workflow from `master` with a `major.minor.patch` version. It needs approval on the `production` environment. It fails if the tag or a release (including a draft) already exists. It builds, tests, signs the HVAKR DLLs with Azure Artifact Signing, checks signatures and versions, and creates a **draft** GitHub release with the zip and its `.sha256`. It does not create a tag or touch GCS.
2. Review the draft. Test the zip in Revit 2025 and 2026.
3. Click **Publish release**. GitHub creates the tag on the built commit.
4. **Publish to GCS** starts on the `release: published` event. It needs `production` approval again. It skips prereleases and refuses releases that are not on `master` or whose assets were not uploaded by the Release workflow. It downloads the zip, re-checks its SHA256, signatures, versions, and layout, and writes:
   - `revit/releases/X.Y.Z/HVAKR-Revit-Plugin-X.Y.Z.zip`, immutable and never overwritten
   - `revit/HVAKR-Revit-Plugin.zip`, the stable download link
   - `revit/latest.json`, last

   Each object is verified over public HTTPS before the next is written. Rerunning is safe. Publishing an older version after a newer one updates only its immutable object. If a run fails, rerun it or dispatch **Publish to GCS** with the tag.

Repository setup required before the first release:

- Protect the `production` GitHub environment with required reviewers.
- Add environment secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `GCP_WORKLOAD_IDENTITY_PROVIDER`, and `GCP_SERVICE_ACCOUNT`.
- Add environment variables `AZURE_ARTIFACT_SIGNING_ENDPOINT`, `AZURE_ARTIFACT_SIGNING_ACCOUNT`, and `AZURE_ARTIFACT_SIGNING_CERTIFICATE_PROFILE`.
- Grant the Azure workload identity only the Artifact Signing Certificate Profile Signer role. Grant the GCP workload identity write access only under `gs://hvakr-desktop-releases/revit/`.

### Testing in Revit

1. Run the explicit `InstallLocal` target after closing every Revit process, or install the CI artifact zip by hand.
2. Launch Revit 2025 or 2026. The `HVAKR` tab should appear on the ribbon.
3. Click **Show** to open the dockable pane. Paste an HVAKR API key to log in.

## Conventions

Coding conventions, Revit-API patterns, and the HVAKR wire contract live in [AGENTS.md](AGENTS.md). Skills for AI collaborators are in [`.agents/skills/`](./.agents/skills/).

## License

Licensed under the [Apache License 2.0](LICENSE). "HVAKR" is a trademark of Flow Circuits, Inc.; see [NOTICE](NOTICE).
