# Manage Views

This folder holds the full `Manage Views` feature surface: the page shell, its child fragments, isolated styles, and local notes.

## Files

- `ManageViews.razor`
  - Main page shell for view selection, view definition editing, and synchronization-launch actions.
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

- Keep column editing and column synchronization as separate workflows.
- New Razor files in this area should include a header section listing referenced fragment files, even when that list is `None`.
- Keep the whole feature in this folder so page, fragments, and isolated css move together.
