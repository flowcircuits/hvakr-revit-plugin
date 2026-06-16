# AGENTS.md

This repo is the **HVAKR Revit Plugin** — a .NET 8 / WPF plugin that connects Autodesk Revit to the HVAKR cloud platform (`https://api.hvakr.com/v0`). It lets mechanical engineers round-trip spaces, boundaries, and building data between Revit and HVAKR so they can run load calcs and design equipment without re-keying the model.

This file documents the conventions and architecture for anyone (human or AI) working in the codebase.

## Engineering Practices

Keep the code simple and direct.

- Avoid unnecessary abstractions. No IoC containers, no mediator patterns, no repository wrappers around `HttpClient`.
- Don't add defensive code for scenarios that can't happen. Trust Revit's invariants and the project's own types.
- Validate at system boundaries only (user input in the pane, HVAKR API responses, Revit API return values that are documented to be nullable).
- Inline one-off helpers rather than building a utility class for code used once.
- Prefer LINQ and standard BCL collections over hand-rolled loops (`GroupBy`, `OrderBy`, `ToDictionary`).

HVAC domain context: ASHRAE 62.1 (ventilation), ANSI/ASHRAE/ACCA 183 (load calculations), ASHRAE 90.1 (energy). Revit units are always internal (feet, Fahrenheit, radians) — HVAKR API values are in data units (inches, Fahrenheit, cfm, BTU/h). Always convert at the boundary with `UnitUtils`.

## Repo Layout

Project folders, solution file, and assembly names all match each other (`HVAKR.X` → `HVAKR.X\HVAKR.X.csproj` → `HVAKR.X.dll`).

| Project                  | Purpose                                                                     |
| ------------------------ | --------------------------------------------------------------------------- |
| `HVAKR.Api`              | `HttpClient` wrapper for the HVAKR REST API. No Revit dependency.           |
| `HVAKR.Api.Tests`        | xUnit integration tests that hit the real HVAKR API (read token from env).  |
| `HVAKR.Revit.UI`         | WPF `UserControl` + `IExternalEventHandler`s. Referenced by plugin + harness. |
| `HVAKR.Revit`            | Revit add-in entry point (`IExternalApplication`, ribbon, dockable pane).   |
| `HVAKR.Revit.UI.Harness` | Standalone WPF app that hosts the dockable pane — iterate here, not in Revit. |

Shared csproj settings (`TargetFramework`, `Nullable`, `LangVersion`, etc.) live in `Directory.Build.props`. Style rules live in `.editorconfig`. Don't duplicate those in individual csproj files.

Namespace convention is one-to-one with the project name (e.g. `HVAKR.Api.Client`, `HVAKR.Revit.UI.MainPane`). Use `HVAKR` for types, namespaces, and user-facing text, and `hvakr` for URLs and package names. Don't use the mixed-case form `Hvakr`.

`HVAKR.Api` targets `net8.0-windows` only because of inheritance from `Directory.Build.props`. Drop `-windows` to `net8.0` if the API tests need to run cross-platform.

## Workflow

### Iterate fast — don't open Revit for every change

Use `HVAKR.Revit.UI.Harness` as the dev harness:

```
dotnet run --project HVAKR.Revit.UI.Harness
```

It hosts `MainPane` in a plain WPF window. Login and project list loading work without booting Revit. Export, and anything inside `IExternalEventHandler.Execute`, requires Revit — that code path only fires when `ExternalEvent.Raise()` is called inside Revit's API context, and outside Revit the handler has no `UIApplication` to act on.

### Commands

```bash
dotnet restore                                   # restore NuGet packages
dotnet build HVAKR.Revit.sln -c Debug            # build all — fast path
dotnet build HVAKR.Revit.sln -c Release          # build + run Inno Setup installer (Windows only)
dotnet test HVAKR.Api.Tests                      # integration tests (read HVAKR_ACCESS_TOKEN)
dotnet run --project HVAKR.Revit.UI.Harness      # standalone UI harness
dotnet format HVAKR.Revit.sln                    # run formatter / style analyzers
```

In `Release` configuration on Windows, the `HVAKR.Revit` csproj runs a `PackageAndInstall` target that copies DLLs to `HVAKR.Revit\Deploy\Plugin`, invokes Inno Setup (`ISCC.exe`), then silently runs the installer. This reinstalls the plugin into Revit on every Release build.

### Cross-platform caveats

The solution targets `net8.0-windows` everywhere and references `RevitAPI.dll` / `RevitAPIUI.dll` from `C:\Program Files\Autodesk\Revit 202X`. Practically:

- `HVAKR.Revit` and `HVAKR.Revit.UI` only build on Windows with Revit installed.
- `HVAKR.Api` and `HVAKR.Api.Tests` build anywhere once `-windows` is dropped from the TFM.
- Release builds invoke Inno Setup and must be on Windows.

On macOS/Linux, a full solution build won't succeed; read the source and reason about correctness instead. A Windows runner verifies.

### Pre-commit

There's no pre-commit hook. Treat `dotnet build -c Debug` + `dotnet test HVAKR.Api.Tests` as the minimum gate before pushing. The `.editorconfig` drives the `dotnet format` analyzer rules; `EnforceCodeStyleInBuild` is on, so style violations are build warnings.

## Code Style

- 4-space indentation, allman braces, file-scoped namespaces (`namespace HVAKR.Revit;`)
- Nullable reference types are on — fix warnings rather than suppressing them with `!`
- `ImplicitUsings` is on — skip `using System;` / `using System.Linq;` in new files
- Prefer `var` when the type is obvious on the right-hand side
- Prefer expression-bodied members for trivial properties/methods
- Prefer `using var x = ...;` over `using (var x = ...) { ... }`
- Prefer primary constructors where they fit (`public sealed class Client(HttpClient http, string apiKey)`)
- Prefer pattern matching (`if (foo is Bar bar)` over `as`/`!= null`)
- Private fields are `_camelCase`; the `.editorconfig` enforces this
- No `async void` except for WPF event handlers — those are `async void` by design
- Avoid `.GetAwaiter().GetResult()` on hot paths. `ExportHandler.Execute` is the one allowed site, because `IExternalEventHandler.Execute` is synchronous by Revit's design

### JSON (Newtonsoft.Json)

- Outbound payloads are camelCase: serialize with `CamelCasePropertyNamesContractResolver`. The API expects it.
- Inbound models in `HVAKR.Api.Models` use PascalCase property names and rely on Newtonsoft's case-insensitive matching. Don't add `[JsonProperty]` unless the wire name actually differs.
- Use `JObject`/`JToken` for one-off dynamic extraction (see `Client.GetProjectIdsAsync`); use typed deserialization for well-known shapes (`ProjectDetails`).

### Unit system — important

Revit stores every length in **decimal feet** and every angle in **radians**, regardless of the project's display units. HVAKR stores lengths in **inches** (data units) and angles in **degrees**. Always convert at the handler boundary:

```csharp
// Revit -> HVAKR
double inches  = UnitUtils.ConvertFromInternalUnits(feet,   UnitTypeId.Inches);
double degrees = UnitUtils.ConvertFromInternalUnits(radians, UnitTypeId.Degrees);

// HVAKR -> Revit
double feet    = UnitUtils.ConvertToInternalUnits(inches,  UnitTypeId.Inches);
double radians = UnitUtils.ConvertToInternalUnits(degrees, UnitTypeId.Degrees);
```

Don't do `feet / 12.0` or `degrees * PI / 180` inline. `UnitUtils` is the single source of truth.

## Revit Plugin Patterns

### Ribbon + dockable pane

The plugin registers one ribbon tab (`HVAKR`), one panel, one button (`Show`), and one dockable pane GUID (`App.DockablePaneGuid` = `3c649293-...`). Changing the pane GUID or the `AddInId` (`F6EF6882-...` in the installer) orphans every user's existing state, so don't change them.

### `IExternalCommand` vs `IExternalEventHandler`

- **`IExternalCommand`** (`ToggleDockablePaneCommand`) — called when the user clicks a ribbon button. Runs in Revit's API context; safe to call Revit API directly.
- **`IExternalEventHandler`** (`ExportHandler`) — the only safe way to call Revit API from a WPF event handler. Create an `ExternalEvent` in the `UserControl` constructor, set fields on the handler, call `.Raise()`. Revit invokes `Execute(UIApplication)` on its own thread when it's safe.

Don't call Revit API directly from a WPF button handler. It will throw (modeless) or silently misbehave. Always go through `ExternalEvent`.

### Transactions

Any document mutation must happen inside `using var t = new Transaction(doc, "...");` followed by `t.Start()` / `t.Commit()`. Name the transaction descriptively — it shows up in Revit's undo stack.

Read-only extraction (`FilteredElementCollector`, reading element properties) does **not** need a transaction. `ExportHandler` is read-only.

### Revit API version skew

- `HVAKR.Revit.csproj` references Revit **2026** DLLs.
- `HVAKR.Revit.UI.csproj` references Revit **2025** DLLs.

This is intentional: the UI library targets the older surface to avoid APIs that break on 2025. When you add a Revit API call in `HVAKR.Revit.UI`, verify it exists in both 2025 and 2026 before merging. Annotate version-gated usage with a trailing `// 2025.3`-style comment when it matters.

## Logging

One logger: `HVAKR.Api.Logger`. Writes to `%LOCALAPPDATA%\HVAKR\plugin.log` (falls back to `%TEMP%` if `LOCALAPPDATA` is unavailable). Handles file-locking internally and swallows its own exceptions — logging never throws.

User-facing error surfaces:
- Inside Revit context: `TaskDialog.Show("HVAKR", ...)`
- Inside WPF code (before an `ExternalEvent` is raised): `MessageBox.Show(...)`

Extend `HVAKR.Api.Logger` rather than introducing a second logger or a third dialog style.

## HVAKR API

Project data lives in the HVAKR backend, not in this repo. To work with real project data during development, use the HVAKR REST API directly:

```bash
# Set the auth header (token doesn't expand correctly inline)
HVAKR_AUTH="Authorization: Bearer $(echo -n $HVAKR_ACCESS_TOKEN | tr -d '[:space:]')"

# List projects
curl -s -H "$HVAKR_AUTH" https://api.hvakr.com/v0/projects

# Get a project with subcollections expanded
curl -s -H "$HVAKR_AUTH" "https://api.hvakr.com/v0/projects/{id}?expand=true"
```

Endpoints currently consumed by the plugin:

| Method | Path                                   | Caller                                  |
| ------ | -------------------------------------- | --------------------------------------- |
| GET    | `/v0/projects`                         | `Client.GetProjectIdsAsync`             |
| GET    | `/v0/projects/{id}[?expand=true]`      | `Client.GetProjectDetailsAsync`         |
| POST   | `/v0/projects?revitPayload`            | `Client.CreateProjectFromRevitAsync`    |
| PATCH  | `/v0/projects/{id}?revitPayload`       | `Client.UpdateProjectFromRevitAsync`    |

`?revitPayload` is a query flag that tells the HVAKR backend to interpret the body as `{ projectAddress, projectName, projectRotationDegrees, revitSpaces[] }` instead of the normal project schema. When field names change on one side, update the other in the same release cycle.

See `.claude/skills/hvakr-api/SKILL.md` for the API reference, or the interactive docs at https://api.hvakr.com/v0/docs/.

## Testing

- **xUnit** (`HVAKR.Api.Tests`). Tests are integration tests — they hit the real API.
- Tests read `HVAKR_ACCESS_TOKEN` (and optionally `HVAKR_TEST_PROJECT_ID`) from the environment. If the env vars aren't set, the tests no-op so CI stays green without secrets.
- Never commit a real API key. Tests read it from `HVAKR_ACCESS_TOKEN`.
- There are no UI tests yet. `HVAKR.Revit.UI.Harness` is the manual dev loop, not an automated one.

## Deploy & Installer

- Script: `HVAKR.Revit/Installer/hvakr-installer.iss`
- Writes an `HVAKR.addin` into `%ProgramData%\Autodesk\Revit\Addins\{2025,2026}` pointing at `{app}\HVAKR.Revit.dll` with `FullClassName = HVAKR.Revit.App`.
- The AddInId GUID (`F6EF6882-...`) is stable — changing it leaves users with two plugins installed.
- Bump `MyAppVersion` in the `.iss` when cutting a release.
- The `PackageAndInstall` MSBuild target in `HVAKR.Revit.csproj` runs ISCC and installs silently on every Release build. Keep the `'$(Configuration)' == 'Release'` condition so Debug builds don't stomp on the installed plugin.

## Common Pitfalls

- **Don't call Revit API from the WPF thread.** Use `IExternalEventHandler`.
- **Don't forget the Transaction.** Revit throws if you mutate the document outside one.
- **Don't hardcode unit strings or conversion factors.** Use `UnitUtils.ConvertFromInternalUnits` / `ConvertToInternalUnits`.
- **Don't use the mixed-case `Hvakr`** — it's `HVAKR` (types, namespaces, UI) or `hvakr` (URLs, package names).
- **Don't add `[JsonProperty]` unless the wire name genuinely differs.** Newtonsoft matches case-insensitively by default.
- **Don't commit a real HVAKR API key.** Tests read from `HVAKR_ACCESS_TOKEN`.
- **Don't assume Revit 2025 and 2026 have identical APIs.** Test against both before shipping.
- **Don't change `DockablePaneGuid`, `AddInId`, or the assembly name `HVAKR.Revit`.** All three are stable identifiers users depend on.
- **Don't introduce a second logger or dialog helper.** One `Logger`, one `TaskDialog`/`MessageBox` split based on context.
