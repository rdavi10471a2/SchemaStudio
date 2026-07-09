<!-- GENERATED from the AIMonitor solution index on 2026-07-07. Regenerate, do not hand-patch. Source: project SchemaStudio.AIHelpers. ASCII-only. -->

# SchemaStudio.AIHelpers - Architecture (project detail)

> Component-level detail for the `SchemaStudio.AIHelpers` project. Solution map: [../Architecture.md](../Architecture.md).

## Role

A tiny **attribute library** (net9.0, Library) that defines the source-marker attributes the codebase uses as
an authoring convention. **No runtime behavior** - these are metadata markers read by tooling/humans, not
executed logic.

## Contents

- **`AIAttributes.cs`** - defines the marker attributes:
  - `[FileVersion("x.y")]` - a per-file version stamp (e.g. `AppConfig.cs` carries `[FileVersion("1.4")]`).
  - `[AIFileContext("path", "summary")]` - a one-line, in-source description of what a file is for.

## Why it exists

The convention: every hand-maintained source file carries `[FileVersion]` + `[AIFileContext]` so an agent (or
human) opening the file gets a version and a purpose line without external docs. When editing a marked file,
bump `FileVersion` and keep `AIFileContext` current. Adding these is maintenance-on-edit, not a separate task.

## Where it is used

Referenced across the solution as attributes on type/file declarations. `find_indexed_references` on
`FileVersionAttribute` / `AIFileContextAttribute` enumerates the marked files.

## Refresh

Trivial and stable - regenerate only if the attribute set in `AIAttributes.cs` changes.
