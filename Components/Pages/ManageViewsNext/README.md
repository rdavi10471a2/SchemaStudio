# Manage Views Next

Promoted replacement surface for Manage Views, still physically routed at `/manage-views-next` until the old route and file names are retired.

## Purpose

This folder keeps the new layout isolated from the current Manage Views page while testing:

- A scrollable left selector grouped by `Domain -> Base Views / Composed Views`.
- A separate `Available Views` group for not-yet-imported source views.
- A bottom-pinned `Reload Databases` command that refreshes database choices, domains, imported views, and available candidates.
- A combined view definition and column metadata editor with one `Save All Changes` action.
- Context-specific reset actions for view fields and selected column metadata.
- A focused column merge review launched from `Review Merge`; the dialog stages parser/saved metadata choices into the current working state, then the page-level `Save All Changes` action remains the database commit point.

## File Map

- `ManageViewsNext.razor` - route, markup shell, selector tree, combined edit layout.
- `ManageViewsNext.razor.cs` - shared state, grouping, labels, save/delete helpers.
- `ManageViewsNext.Selection.cs` - database reload, workspace load, selector changes, reset, dirty navigation guard.
- `ManageViewsNext.Columns.cs` - column selector/editor, reset selected column, merge review, parser-to-column mapping.
- `Components/Dialogs/ColumnMergeReviewDialog.razor` - focused merge-review dialog used by this prototype. It owns candidate filtering, parsed-vs-saved comparison, effective-result preview, and Apply Merge staging for Business Name, Business Description, and Disable Inheritance.
- `ManageViewsNext.Parser.cs` - refresh view, show SQL, view details, where-used actions.
- `ManageViewsNext.razor.css` - isolated layout and tree/editor styling, including the selected-view toolbar's responsive button grid for narrower desktop viewports.

## Notes

The old `Components/Pages/ManageViews` page remains in the project as fallback code, but its main navigation entry is hidden while this surface is promoted.
Developer Notes are intentionally excluded from the merge review; they are preserved as app-owned metadata rather than parser-owned SQL annotations.
