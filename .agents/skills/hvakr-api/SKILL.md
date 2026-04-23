---
name: hvakr-api
description: >-
    HVAKR REST API reference, scoped to the endpoints this Revit plugin uses. Covers authentication,
    the /v0/projects endpoints, and the special `?revitPayload` query flag for round-tripping Revit
    geometry. For the full HVAKR API surface (calcs, weather, dryside graph, etc.), see the main
    HVAKR repo at ~/Repositories/hvakr/.agents/skills/hvakr-api/SKILL.md.
license: Proprietary
compatibility: Requires HVAKR_ACCESS_TOKEN env var (or per-user API key in the plugin)
---

# HVAKR API — Plugin Endpoints

HVAKR is an HVAC design platform for commercial and residential load calcs per ASHRAE standards. The Revit plugin talks to a small subset of the v0 REST API.

**Base URL:** `https://api.hvakr.com/v0`

**Interactive docs:** https://api.hvakr.com/v0/docs/
**Full reference (parent repo):** `~/Repositories/hvakr/.agents/skills/hvakr-api/SKILL.md`

## Authentication

Bearer token in the `Authorization` header. Tokens are created in the HVAKR app under *Settings → API → Create Token*.

In **development**, use the env var:

```bash
# $HVAKR_ACCESS_TOKEN doesn't expand cleanly inline — assign to a var first
HVAKR_AUTH="Authorization: Bearer $(echo -n $HVAKR_ACCESS_TOKEN | tr -d '[:space:]')"
curl -H "$HVAKR_AUTH" https://api.hvakr.com/v0/projects
```

In the **plugin**, the user pastes their key into the `PasswordBox` on `MainPane` at login time. `HVAKR.Api.Client` takes it as a constructor argument and adds it to every request:

```csharp
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
```

Never persist the key to disk. Never log it. Never commit one to tests.

## Endpoints used by the plugin

### List project IDs

```bash
curl -s -H "$HVAKR_AUTH" https://api.hvakr.com/v0/projects
```

Response shape: `{ "ids": ["abc123", "def456", ...] }`

C# consumer: `Client.GetProjectIdsAsync()`. Parses `json["ids"]` as a `JArray`.

### Get project

```bash
# Core fields only — fast, good for populating a picker
curl -s -H "$HVAKR_AUTH" https://api.hvakr.com/v0/projects/{id}

# With all subcollections expanded (spaces, zones, systems, types, ...)
curl -s -H "$HVAKR_AUTH" "https://api.hvakr.com/v0/projects/{id}?expand=true"

# Specific subcollections only
curl -s -H "$HVAKR_AUTH" "https://api.hvakr.com/v0/projects/{id}?expand=spaces"
```

C# consumer: `Client.GetProjectDetailsAsync(string projectId, bool expand = false)`. The client always sends `?expand=true` when `expand` is true (normalized).

Response deserializes to `HVAKR.Api.Models.ProjectDetails`. Key fields: `Id`, `Name`, `Address`, `Elevation`, `Latitude`, `Longitude`, `Spaces` (dict of `SpaceDetails`, populated only when expanded).

### Create project from Revit geometry

```bash
curl -X POST -H "$HVAKR_AUTH" -H "Content-Type: application/json" \
  -d '{ ... revit payload ... }' \
  "https://api.hvakr.com/v0/projects?revitPayload"
```

The `?revitPayload` flag tells the backend to interpret the body as **Revit-extracted geometry**, not a full HVAKR project. Payload shape (camelCase, serialized with `CamelCasePropertyNamesContractResolver`):

```json
{
  "projectAddress": "123 Main St",
  "projectName": "My Office Building",
  "projectRotationDegrees": 0.0,
  "revitSpaces": [
    {
      "area": 250.5,
      "boundaries": [[{"x1": 0, "y1": 0, "x2": 10, "y2": 0}, ...]],
      "levelElevation": 0.0,
      "name": "Office 101",
      "number": "101",
      "unboundedHeight": 10.0,
      "uniqueId": "abc-def-...",
      "volume": 2505.0
    }
  ]
}
```

**Units in this payload are Revit internal (feet, square feet, cubic feet, degrees).** The HVAKR backend converts to its own data units (inches).

C# consumer: `Client.CreateProjectFromRevitAsync(string revitPayloadJson)`.

### Update project from Revit geometry

```bash
curl -X PATCH -H "$HVAKR_AUTH" -H "Content-Type: application/json" \
  -d '{ ... revit payload ... }' \
  "https://api.hvakr.com/v0/projects/{id}?revitPayload"
```

Same payload shape as POST. Merges the Revit geometry into an existing HVAKR project.

C# consumer: `Client.UpdateProjectFromRevitAsync(string projectId, string revitPayloadJson)`.

## Wire-shape contract

Field names in the Revit payload are camelCase. The `HVAKR.Api.Models.*` classes use PascalCase C# names — Newtonsoft matches case-insensitively by default. **You do not need `[JsonProperty]` attributes** unless the wire name genuinely differs.

For outbound payloads, we serialize with `CamelCasePropertyNamesContractResolver` (see `ExportHandler.BuildPayloadJson`). Without the contract resolver, Newtonsoft uses PascalCase property names, which the backend rejects.

## When wire shape changes

The `?revitPayload` contract is shared between this repo and the HVAKR monorepo backend. When you add or rename a field:

1. Update the model class / anonymous type in `ExportHandler`.
2. Update the backend handler (search the HVAKR monorepo for `revitPayload`).
3. Ship both in the same release cycle. The plugin auto-updates on next user install, so slight drift is tolerable, but breaking renames will cause silent data loss.

## Rate limits / errors

The API uses standard HTTP status codes. `Client` calls `response.EnsureSuccessStatusCode()` internally — errors bubble as `HttpRequestException`. `ExportHandler` catches and surfaces them via `TaskDialog.Show("HVAKR", ex.Message)` and logs via `Logger.LogError`.

There is no formal rate limit today. Be polite — the project-list login flow does N+1 (list IDs, then get each by ID). For a user with many projects this is slow; consider batching if we ever add a "projects" endpoint that returns names inline.
