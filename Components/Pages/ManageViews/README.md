# Manage Views

This folder holds the full `Manage Views` feature surface: the page shell, its child fragments, isolated styles, and local notes.

## Files

- `ManageViews.razor`
  - Main page shell for view selection, view definition editing, and synchronization-launch actions.
- `ManageViews.Columns.cs`
  - Partial class for column synchronization workflow, review-merge launch, accepted-column creation, and parser-vs-saved review row comparison helpers.
- `ManageViews.Selection.cs`
  - Partial class for database/domain/view selection, workspace reloads, reset tiers, dirty-navigation guard, and selection-key helpers.
- `ManageViews.Parser.cs`
  - Partial class for parser refresh, Show SQL, parsed dependency, and SQL Server where-used actions.
- `ManageViews.razor.css`
  - Isolated styling for the page shell and shared feature layout.
- `ManageViewsColumnsTab.razor`
  - Saved-column maintenance fragment for the `Columns` tab.
  - Owns grid-local selection state, header tooltip rendering, and the placeholder edit launch.
- `ManageViewsColumnsTab.razor.css`
  - Isolated styling for the saved-columns fragment, including sticky headers, continuous scrolling, and text-area-style preview cells.
- `ManageViewsColumnSynchronizationSummary.razor`
  - Reusable summary card that launches the merge/synchronization dialog workflow.
- `ManageViewsColumnSynchronizationSummary.razor.css`
  - Isolated styling for the synchronization summary card.
- `README.md`
  - Local feature map and maintenance notes for this folder.

## Related Files

- `..\NavMenu.razor`
  - Launch point in the application navigation for the `/manage-views` route.

## Maintenance Notes

- `ManageViews.razor` should remain the markup shell plus lightweight state/computed properties. Put feature behavior into coarse partials rather than growing the Razor file again.
- Use `ManageViews.Selection.cs` for anything that changes the selected database, domain filter, view key, workspace list, or dirty-navigation behavior.
- Use `ManageViews.Parser.cs` for parser cache refresh, current parsed view rebuilds, Show SQL, parsed dependencies, and where-used actions.
- Use `ManageViews.Columns.cs` for review-merge behavior, accepted added-column creation, and parser-vs-saved comparison/preview helpers.
- Keep column editing and column synchronization as separate workflows.
- New Razor files in this area should include a header section listing referenced fragment files, even when that list is `None`.
- Keep the whole feature in this folder so page, fragments, and isolated css move together.
- The pre-split checkpoint is `checkpoint/manageviews-split-20260427_114621`; use it only when intentionally rolling back the full Manage Views partial split.
