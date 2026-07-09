<!-- GENERATED from the AIMonitor solution index on 2026-07-07 (full file list). Regenerate, do not hand-patch. Source: project SchemaStudioWebViewer (root project). ASCII-only. -->

# SchemaStudioWebViewer (web app) - Architecture (project detail)

> Component-level detail for the root Blazor project. Solution map: [../Architecture.md](../Architecture.md).
> This project's files live at the **solution root** (not a subfolder), so paths below are relative to root.

## Role

The **Blazor Server host + UI + config + embedded MCP** (net9.0, Exe). Entry point
[../../Program.cs](../../Program.cs). Depends on `SchemaStudio.Data` (data), `WEBSemanticModel` (view parsing),
and `SchemaStudio.AIHelpers` (markers). UI is Radzen.

## Feature pages (`Components/Pages/`)

Larger features use **partial classes** (`.Sql.cs`, `.Models.cs`, `.Selection.cs`, etc.) to split one page
across files. Several feature folders carry their own `README.md` (linked below) - read those for deep detail.

| Area (route) | What it does |
|---|---|
| `ManageViewsNext` (`/manage-views`, `/manage-views-next`) | The live "Manage Views" page. Per-view **column editor**: edit each column's `BusinessName` + `BusinessDescription`; `Review Merge` opens column reconciliation. Parts: `.razor(.cs/.css)`, `.Columns.cs`, `.Parser.cs`, `.Selection.cs`. See [ManageViewsNext/README](../../Components/Pages/ManageViewsNext/README.md). |
| `DomainObjectModeler` (`/domain-object-modeler`) | Model domain objects over schema (joins/selection/SQL; partial-class heavy: `.Joins/.Models/.Sql/.Definition/.Selection`). See [DomainObjectModeler/README](../../Components/Pages/DomainObjectModeler/README.md). |
| `DomainObjectEditor` | Edit domain objects; CTE editors (`CteFieldParser`, `CteSelectionSessionEditor`, `CteSelectionPresentationEditor`). |
| `BaseViewCreator` (`/base-view-creator`) | Create base views (`.Metadata`, `.Sql`, `BaseViewCreatorSelectionEngine`, metadata dialog). |
| `BaseViewGenerator` (`/base-view-generator`) | Generate base views. |
| `ManageDatabases` (`/manage-databases`) | Databases + `ManageDatabases/DatabaseRelationshipsPanel`. |
| `ParserLab` (`/parser-lab`) | Parser test area - exercise `WEBSemanticModel` parsing directly. |
| `ToolLab` | Utility/experimental tools. |
| `Home` (`/`), `About` (`/about`) | Landing (auth-gated login button - see config reference) + info. |
| `DatabaseDomainTest`, `Counter`, `Weather`, `SQLHighlighter`, `Error` | Test/utility/framework pages. |

Support UI: `Components/Layout/` (`MainLayout`, `NavMenu`), `Components/Dialogs/` (many modals - see
[Dialogs/README](../../Components/Dialogs/README.md)), `Components/HelpSystem/`,
`Components/ColumnReconciliation/` (`ColumnReconciliationDialog` - see
[ColumnReconciliation/README](../../Components/ColumnReconciliation/README.md) and its `TODO.md`).

## Non-UI app code

- **`Repositories/`** (app-level, distinct from `SchemaStudio.Data`): `TableSchemaSmoRepository` (SQL Server
  **SMO** live schema), `ReadOnlyDatabaseRepository`, `ReadOnlySchemaObjectRepository`,
  `ReadOnlyViewDefinitionRepository`, `SchemaMCPRepository`, `TableDisplayColumnPolicy`.
- **`Models/`** - view/display models bridging Data DTOs to UI: `DisplaySchemaObject`, `SchemaObjectModel`,
  `SchemaObjectColumnModel`, `DatabaseModel`, `DatabaseDomainModel`, `DisplayAttributes`, `HelpSubject`,
  `ViewDefinitionResult`, `SQLViewMock`.
- **`McpTools/`** - `SchemaCatalogMcpTools`: schema-catalog tool surface exposed to agents via the embedded MCP server.
- **`AppConfig/`** - config binding (see [../Configuration.md](../Configuration.md)).
- **`Utils/`** - `AttributeService`, `ReflectionUtils`.

## Resolved usage (index, 2026-07-07)

- `ReadOnlyDatabaseRepository`, `ReadOnlySchemaObjectRepository`, `ReadOnlyViewDefinitionRepository` -> consumed by `Components/Pages/Home.razor`.
- `SchemaMCPRepository` -> consumed by `McpTools/SchemaCatalogMcpTools.cs` (3 sites).
- `SchemaCatalogMcpTools` -> referenced from `Program.cs` (3 sites - MCP tool registration).
- `TableSchemaSmoRepository`, `TableDisplayColumnPolicy` showed **no direct symbol references** - expected, they are DI/`@inject`-wired (the symbol index does not capture injection). Verify via `Program.cs`, not by absence here.

## Patterns to know

- **Partial-class pages:** big pages (e.g. `DomainObjectModeler`, `ManageViewsNext`) spread logic across
  sibling `.cs` files - find all parts before editing (`find_indexed_symbols` / grep the folder).
- **Two repository layers:** app `Repositories/` (live SMO + read-only/display) sit above `SchemaStudio.Data`
  repos (persisted definitions). Know which layer you are in.
- **Per-folder READMEs exist** for the gnarly features - prefer reading/refreshing those over restating them here.
- **Auth/kiosk gating** is config-driven via `AppConfig.Current` - see the config reference.

## Refresh

Regenerate: enumerate files via `query_solution_index` per folder (a whole-solution glob truncates - it
previously hid `ParserLab`, `DatabaseDomainTest`, and others); link existing `Components/**/README.md` rather
than duplicating them; usage via `find_indexed_references`.
