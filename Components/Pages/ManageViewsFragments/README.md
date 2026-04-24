# Manage Views Fragments

This folder holds fragment components that support the `Manage Views` page without making the page shell absorb every detail.

## Files

- `ManageViewsColumnsTab.razor`
  - Saved-column maintenance fragment for the `Columns` tab.
  - Owns grid-local selection state, header tooltip rendering, and the placeholder edit launch.
- `ManageViewsColumnsTab.razor.css`
  - Isolated styling for the saved-columns fragment, including sticky headers, continuous scrolling, and text-area-style preview cells.

## Related Files

- `..\ManageViews.razor`
  - Page shell, left-side selector, view definition editor, and synchronization-summary launch point.
- `..\ManageViews.razor.css`
  - Page-shell styling for the main Manage Views screen.
- `..\ManageViewsColumnSynchronizationSummary.razor`
  - Reusable summary card that launches the merge/synchronization dialog workflow.
- `..\ManageViewsColumnSynchronizationSummary.razor.css`
  - Isolated styling for the synchronization summary card.

## Maintenance Notes

- Keep column editing and column synchronization as separate workflows.
- New Razor files in this area should include a header section listing referenced fragment files, even when that list is `None`.
