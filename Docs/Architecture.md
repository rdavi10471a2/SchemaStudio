# SchemaStudioWebViewer - Architecture (general)

> Generated from the AIMonitor solution index (4 projects) on 2026-07-07. ASCII-only.
> Level: **orientation only.** Per-project detail lives in `Docs/<Project>/Architecture.md`; this file is the map.

## What it is

A **Blazor Server** web app (net9.0, `SchemaStudioWebViewer.csproj`, OutputType `Exe`) for browsing and
authoring SQL Server schema/view metadata. UI is **Radzen** components. The app also **hosts its own MCP
server** (`Mcp` config section + `McpTools/`) exposing a schema catalog to agents. Auth is a lightweight
gate (stub), with a **Kiosk** mode for unauthenticated demo display.

Entry point: [Program.cs](Program.cs) at the solution root. Runtime config: [appsettings.json](appsettings.json).

## Projects (dependency order, leaf to root)

| Project | Type | Role | Detail doc |
|---|---|---|---|
| **SchemaStudio.AIHelpers** | Library | Source-marker attributes (`[FileVersion]`, `[AIFileContext]`). No runtime logic. | [Docs/SchemaStudio.AIHelpers](Docs/SchemaStudio.AIHelpers/Architecture.md) |
| **SchemaStudio.Data** | Library | Data access: `Models/` DTOs, `Repositories/` (sync + async), `Sql/` raw scripts. SQL Server. | [Docs/SchemaStudio.Data](Docs/SchemaStudio.Data/Architecture.md) |
| **SchemaStudioWebViewer.WEBSemanticModel** | Library | SQL semantic model: parse view SQL -> bound columns/dependencies (`ViewParsingService` / `QueryOrchestrator`). | [Docs/WEBSemanticModel](Docs/WEBSemanticModel/Architecture.md) |
| **SchemaStudioWebViewer** | **Exe** | Blazor host + UI + config + embedded MCP. Depends on all three above. | [Docs/SchemaStudioWebViewer](Docs/SchemaStudioWebViewer/Architecture.md) |

## Configuration (the pattern to know)

`appsettings.json` is **never read directly** by feature code. [AppConfig.cs](AppConfig/AppConfig.cs) reads it
once into the static `AppConfig.Current.<Section>.<Property>`. Full key-by-key reference (and the login-button
example): **[Docs/Configuration.md](Docs/Configuration.md)**.

Quick exemplar - the login button is `Auth:ShowAuthButton` ([Home.razor:118](Components/Pages/Home.razor#L118)),
bound at [AppConfig.cs:49](AppConfig/AppConfig.cs#L49); set it `true` to show. (Not `Kiosk:ShowLoginLink`.)

## Runtime flow (high level)

1. `Program.cs` builds the Blazor Server host, binds `AppConfig`, registers repositories + semantic-model
   services, and (if `Mcp:Enabled`) mounts the MCP endpoint at `Mcp:BaseRoute`.
2. Browser hits a page under `Components/Pages/`; auth/kiosk gates (`AppConfig.Current.Auth/.Kiosk`) decide access.
3. Pages call **app `Repositories/`** (live SMO + read-only) and **`SchemaStudio.Data` repos** -> SQL Server.
4. View SQL runs through **WEBSemanticModel** (`ViewParsingService` -> `QueryOrchestrator` -> parse/bind) to
   produce bound columns/dependencies shown in the UI (e.g. Manage Views Next column editing).
5. Agents can hit the embedded MCP server (`McpTools/SchemaCatalogMcpTools`) for the schema catalog.

## Documentation in this repo

- **`Docs/`** (this tree) - generated architecture map + per-project + config reference. Regenerated from the index; do not hand-patch.
- **Per-feature `README.md`** inside component folders - deeper, hand-authored, present-tense specs:
  `Components/ColumnReconciliation/README.md`, `Components/Dialogs/README.md`,
  `Components/Pages/ManageViewsNext/README.md`, `Components/Pages/DomainObjectModeler/README.md`.
- **`TODO.md`** files (root and `Components/ColumnReconciliation/`) - future/speculative work, kept separate from the READMEs.

## Notes

- All C# here is **AI-generated**; the only hand-edited surface is `appsettings.json` (+ `.Development.json`).
- Keep `Docs/` files ASCII-only: the watched-source save path corrupts non-ASCII punctuation to `?`.

## Refreshing this doc

Generated artifact. Regenerate against the index: projects from `get_solution_index_tree`; per-project files
from `query_solution_index(scope: "folder")` (not a whole-solution glob - it truncates); config from
`AppConfig.cs`; usage via `find_indexed_references`. Link existing `README.md`/`TODO.md`, do not duplicate them.
