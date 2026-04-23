# HVAKR Revit Plugin

A Revit add-in that round-trips MEP spaces between [Revit](https://www.autodesk.com/products/revit) and the [HVAKR](https://www.hvakr.com) cloud platform, so mechanical engineers can run load calcs and equipment sizing without re-keying the model.

## Prerequisites

- Visual Studio 2022 (17.9+) or JetBrains Rider
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Autodesk Revit 2025 or 2026 for runtime testing inside Revit
- [Inno Setup 6](https://jrsoftware.org/download.php/is.exe?site=1) — only needed for Release builds

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

# Release: copies output → Deploy\Plugin, builds Inno Setup installer,
# drops it in Deploy\Installer, then runs it silently so Revit picks up the new DLL.
dotnet build HVAKR.Revit.sln -c Release

# Tests (set HVAKR_ACCESS_TOKEN to run them; otherwise they no-op)
dotnet test HVAKR.Api.Tests
```

## CI

GitHub Actions builds the full solution on `windows-latest` in `Debug`:

- restores NuGet packages
- builds `HVAKR.Revit.sln`
- runs `HVAKR.Api.Tests`

The workflow intentionally does not build `Release` because Release invokes Inno Setup and silently runs the installer. Revit behavior still needs manual validation in Revit 2025/2026; CI verifies compilation, WPF/XAML build, and API integration tests when `HVAKR_ACCESS_TOKEN` is available.

### Testing in Revit

1. Build in `Release` (this reinstalls the add-in silently).
2. Launch Revit 2025 or 2026. The `HVAKR` tab should appear on the ribbon.
3. Click **Show** to open the dockable pane. Paste an HVAKR API key to log in.

## Installer

- Script: [`HVAKR.Revit/Installer/hvakr-installer.iss`](HVAKR.Revit/Installer/hvakr-installer.iss)
- Installs to `C:\Program Files\HVAKR\`
- Writes an `.addin` manifest into `%ProgramData%\Autodesk\Revit\Addins\{2025,2026}` for every installed Revit version
- Bump `MyAppVersion` in the `.iss` when cutting a release

## Conventions

Coding conventions, Revit-API patterns, and the HVAKR wire contract live in [AGENTS.md](AGENTS.md). Skills for AI collaborators are in [`.agents/skills/`](./.agents/skills/).
