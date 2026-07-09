<!-- GENERATED from the AIMonitor solution index on 2026-07-07 (full file list). Regenerate, do not hand-patch. Source: project SchemaStudioWebViewer.WEBSemanticModel. ASCII-only. -->

# WEBSemanticModel - Architecture (project detail)

> Component-level detail for the `SchemaStudioWebViewer.WEBSemanticModel` project. Solution map: [../Architecture.md](../Architecture.md).

## Role

The **SQL semantic model** (net9.0, Library). It turns raw SQL **view definitions** into a bound, structured
model - parsed columns, source tables, dependencies, and view metadata - that the UI displays and reasons
about. Pure logic: it consumes view definitions supplied by a provider; no database access, no UI.

## Pipeline (the shape to know)

```text
raw view SQL (from a provider)
  -> Parsing/       ViewParser + BasicSelectVisitor           # SQL text -> ParsedQuery / SelectItem
                    ViewMetaDataBinder                        # attach view-level metadata
                    QueryFormatter, TokenHelpers              # formatting / token helpers
  -> Binding/       QueryBinder, ColumnBinder                 # resolve columns/tables/deps to model
  -> Model/         ColumnBinding, ViewSourcedColumnDefinition,
                    SourceTable, ParsedDependency, SelectItem,
                    ParsedQuery, Enums, ExportMappers         # the bound result + export projections
Orchestration/QueryOrchestrator coordinates the run; Services/ViewParsingService is the caller entry point;
Providers/IViewDefinitionProvider (+ ViewDefinitionProvider) supply inputs; Diagnostics/ collects parse/bind issues.
```

## Full layout

- **`Parsing/`** - `ViewParser`, `BasicSelectVisitor` (walks the SELECT), `ViewMetaDataBinder` (view-level metadata), `QueryFormatter`, `TokenHelpers`.
- **`Binding/`** - `QueryBinder`, `ColumnBinder`: map parsed elements to model bindings.
- **`Model/`** - `ColumnBinding`, `ViewSourcedColumnDefinition`, `SourceTable`, `ParsedDependency`, `SelectItem`, `ParsedQuery`, `Enums`, `ExportMappers` (projection to export shapes).
- **`Orchestration/`** - `QueryOrchestrator`: coordinates parse -> bind for a query/view.
- **`Services/`** - `ViewParsingService`: the orchestration entry point callers use.
- **`Providers/`** - `IViewDefinitionProvider` + `ViewDefinitionProvider`: abstraction and implementation for where view definitions come from.
- **`Diagnostics/`** - `Diagnostics`: parse/bind diagnostics surface.

## Patterns to know

- **Entry point is `ViewParsingService`**; internally `QueryOrchestrator` drives the parse->bind sequence. Do
  not call the parsers/binders directly from UI - go through the service.
- **`IViewDefinitionProvider` decouples input** - the model does not fetch SQL itself; a provider supplies it (`ViewDefinitionProvider`).
- **`ParsedQuery`/`SelectItem`** are the parse-side shapes; **`ColumnBinding`/`ViewSourcedColumnDefinition`** are the bound-side shapes consumed downstream (e.g. by Manage Views column reconciliation).
- Parsing is **best-effort** with a `Diagnostics` channel; check it rather than assuming a clean parse.

## Where it is used (resolved from the index, 2026-07-07)

- `ViewParsingService` is referenced from `Program.cs` (DI registration - the app resolves it by injection).
- `QueryOrchestrator` is referenced from `ViewParsingService.cs` (the service drives the orchestrator).

Page-level consumers (Manage Views Next, Parser Lab) reach `ViewParsingService` via `@inject`/DI, which the
C# symbol reference index does not capture - so the absence of page references here is expected, not evidence
of non-use.

## Refresh

Regenerate from the index: enumerate files via `query_solution_index(scope: "folder")` on `WEBSemanticModel/`
(do not rely on a whole-solution glob - it truncates); usage via `find_indexed_references` on
`ViewParsingService`, `QueryOrchestrator`, and the `Model/` types.
