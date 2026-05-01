# Dialog Components

This folder holds shared Radzen dialog components used by Schema Studio Web.

## ColumnMergeReviewDialog

Files:

- `ColumnMergeReviewDialog.razor`
- `ColumnMergeReviewDialog.razor.css`

Launch point:

- `Components/Pages/ManageViewsNext/ManageViewsNext.razor`
- `Review Merge` opens the dialog from `OpenColumnMergeReviewAsync`.

Purpose:

- Compare parser-current metadata against saved Schema Studio column metadata.
- Stage merge choices into the caller's in-memory column list.
- Leave persistence to the main page's `Save All Changes` action.

Merge ownership:

- User-mergeable fields are `BusinessName`, `BusinessDescription`, and `DisableInheritance`.
- `DeveloperNotes` are app-owned and intentionally excluded from parser merge.
- Parser structure such as ordinal, source kind, physical lineage, and semantic source can still be synchronized as background structure when applying parser-shaped rows.

Current choice rules:

- Added rows auto-use parsed metadata because no saved row exists.
- Removed rows are informational; downstream sync/upsert behavior owns removal handling.
- Changed and conflict rows default to keeping saved metadata.
- Unchanged rows do not need a decision.

Composed-view policy:

- Composed views may be inheritance targets, so the dialog should not be treated as final policy for composed-view ownership.
- For inherited/pass-through columns, the surface is closer to inheritance review than merge.
- For composed-owned expression columns, parsed SQL comments may still be meaningful bootstrap metadata.
- Keep this distinction explicit before expanding composed-view write behavior.

Navigation model:

- The dialog uses a one-column-at-a-time review model with search, status counts, and previous/next navigation.
- This avoids a second selection surface inside the modal and keeps the visible area dedicated to field comparison and effective result preview.
- Reconsider a left-hand candidate list only if users need random access across many changed rows more than they need the wider comparison table.
