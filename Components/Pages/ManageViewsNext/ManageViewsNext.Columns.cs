using Radzen;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.ColumnReconciliation;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

public partial class ManageViewsNext
{
    private int? SelectedColumnId;
    private string ColumnFilter = string.Empty;

    private List<SchemaObjectColumnDefinition> FilteredColumns
    {
        get
        {
            var query = SavedColumns
                .OrderBy(column => column.MergeState == SchemaObjectColumnMergeState.DetectedRemove ? int.MaxValue - 1 : column.OrdinalPosition)
                .ThenBy(column => column.SourceColumnName, StringComparer.OrdinalIgnoreCase)
                .AsEnumerable();
            if (!string.IsNullOrWhiteSpace(ColumnFilter))
            {
                query = query.Where(ColumnMatchesFilter);
            }

            return query.ToList();
        }
    }

    private SchemaObjectColumnDefinition? SelectedColumn =>
        SelectedColumnId == null
            ? null
            : SavedColumns.FirstOrDefault(column => GetColumnKey(column) == SelectedColumnId.Value);

    private bool SelectedColumnMetadataIsInherited =>
        SelectedColumn != null &&
        EditableObject?.IsBaseObject != true &&
        !SelectedColumn.DisableInheritance;

    private string? SelectedColumnInheritedFrom =>
        SelectedColumnMetadataIsInherited
            ? FormatQualifiedName(
                SelectedColumn?.SemanticDatabase,
                SelectedColumn?.SemanticSchema,
                SelectedColumn?.SemanticObject,
                SelectedColumn?.SemanticColumn)
            : null;

    private void EnsureSelectedColumn()
    {
        if (SelectedColumn != null && FilteredColumns.Any(column => GetColumnKey(column) == SelectedColumnId))
        {
            return;
        }

        SelectedColumnId = FilteredColumns.FirstOrDefault() is { } first
            ? GetColumnKey(first)
            : null;
    }

    private void SelectColumn(SchemaObjectColumnDefinition column)
    {
        SelectedColumnId = GetColumnKey(column);
    }

    private async Task ResetSelectedColumnAsync()
    {
        if (SelectedColumn == null)
        {
            return;
        }

        var selectedKey = GetColumnKey(SelectedColumn);
        var original = OriginalSavedColumns.FirstOrDefault(column => GetColumnKey(column) == selectedKey);
        if (original == null)
        {
            return;
        }

        SelectedColumn.BusinessName = original.BusinessName;
        SelectedColumn.BusinessDescription = original.BusinessDescription;
        SelectedColumn.DeveloperNotes = original.DeveloperNotes;
        SelectedColumn.DisableInheritance = original.DisableInheritance;
        SelectedColumn.ClearDirty();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OpenColumnMergeReviewAsync()
    {
        if (EditableObject == null || SelectedViewItem == null)
        {
            return;
        }

        if (EditableObject.SchemaObjectId <= 0)
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Save view first", "Save the view definition before reviewing column metadata merge choices.", 3500);
            return;
        }

        if (CurrentParsedView == null)
        {
            NotificationService.Notify(NotificationSeverity.Info, "No parser result", "Refresh the parsed view before reviewing column metadata merge choices.", 3000);
            return;
        }

        var result = await DialogService.OpenAsync<ColumnReconciliationDialog>(
            $"{SelectedViewItem.DisplayName} - Column Merge Review",
            new Dictionary<string, object?>
            {
                { "ViewDisplayName", SelectedViewItem.DisplayName },
                { "IsBaseView", EditableObject.IsBaseObject },
                { "SchemaObjectId", EditableObject.SchemaObjectId },
                { "ParsedView", CurrentParsedView },
                { "ParsedColumns", CurrentParsedView.Columns },
                { "SavedColumns", SavedColumns }
            },
            new DialogOptions
            {
                Width = "90vw",
                Height = "90vh",
                Resizable = true,
                Draggable = false,
                CloseDialogOnOverlayClick = false
            });

        if (result is true)
        {
            EnsureSelectedColumn();
            NotificationService.Notify(NotificationSeverity.Success, "Merge applied", "Column merge choices were staged. Use Save All Changes to commit them.", 3000);
            await InvokeAsync(StateHasChanged);
        }
    }

    private string GetColumnSelectorClass(SchemaObjectColumnDefinition column) =>
        SelectedColumn != null && GetColumnKey(column) == GetColumnKey(SelectedColumn)
            ? "mvn-column-item selected"
            : "mvn-column-item";

    private static string GetColumnStateChipText(SchemaObjectColumnDefinition column) =>
        column.MergeState switch
        {
            SchemaObjectColumnMergeState.DetectedAdd or SchemaObjectColumnMergeState.PendingAdd => "Added",
            SchemaObjectColumnMergeState.DetectedRemove or SchemaObjectColumnMergeState.PendingRemove => "Removed",
            SchemaObjectColumnMergeState.PendingUpdate => "Edited",
            _ => "Edited"
        };

    private static string GetColumnStateChipClass(SchemaObjectColumnDefinition column) =>
        column.MergeState switch
        {
            SchemaObjectColumnMergeState.DetectedAdd or SchemaObjectColumnMergeState.PendingAdd => "mvn-state-chip added",
            SchemaObjectColumnMergeState.DetectedRemove or SchemaObjectColumnMergeState.PendingRemove => "mvn-state-chip removed",
            SchemaObjectColumnMergeState.PendingUpdate => "mvn-state-chip edited",
            _ => "mvn-state-chip edited"
        };

    private bool ColumnMatchesFilter(SchemaObjectColumnDefinition column)
    {
        var filter = ColumnFilter.Trim();
        return Contains(column.SourceColumnName, filter)
            || Contains(column.BusinessName, filter)
            || Contains(column.BusinessDescription, filter)
            || Contains(column.DeveloperNotes, filter);
    }

    private static bool Contains(string? value, string filter) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static int GetColumnKey(SchemaObjectColumnDefinition column) =>
        column.SchemaObjectColumnId > 0
            ? column.SchemaObjectColumnId
            : HashCode.Combine(column.SchemaObjectId, column.OrdinalPosition, column.SourceColumnName?.ToUpperInvariant());

    private static string BuildColumnTooltip(SchemaObjectColumnDefinition column)
    {
        if (!string.IsNullOrWhiteSpace(column.BusinessDescription))
        {
            return column.BusinessDescription;
        }

        if (!string.IsNullOrWhiteSpace(column.BusinessName))
        {
            return column.BusinessName;
        }

        return column.SourceColumnName;
    }

    private static string? FormatQualifiedName(string? database, string? schema, string? objectName, string? columnName)
    {
        var parts = new[] { database, schema, objectName, columnName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();

        return parts.Count == 0 ? null : string.Join(".", parts);
    }

    private static List<SchemaObjectColumnDefinition> CloneColumns(IEnumerable<SchemaObjectColumnDefinition> columns)
    {
        return columns
            .OrderBy(x => x.OrdinalPosition)
            .ThenBy(x => x.SourceColumnName, StringComparer.OrdinalIgnoreCase)
            .Select(CloneColumn)
            .ToList();
    }

    private static SchemaObjectColumnDefinition CloneColumn(SchemaObjectColumnDefinition source)
    {
        var clone = new SchemaObjectColumnDefinition
        {
            SchemaObjectColumnId = source.SchemaObjectColumnId,
            SchemaObjectId = source.SchemaObjectId,
            OrdinalPosition = source.OrdinalPosition,
            SourceColumnName = source.SourceColumnName,
            SourceColumnKind = source.SourceColumnKind,
            BaseDatabaseName = source.BaseDatabaseName,
            BaseSchemaName = source.BaseSchemaName,
            BaseObjectName = source.BaseObjectName,
            BaseColumnName = source.BaseColumnName,
            SemanticDatabase = source.SemanticDatabase,
            SemanticSchema = source.SemanticSchema,
            SemanticObject = source.SemanticObject,
            SemanticColumn = source.SemanticColumn,
            IsBaseDefinition = source.IsBaseDefinition,
            DisableInheritance = source.DisableInheritance,
            BusinessName = source.BusinessName,
            BusinessDescription = source.BusinessDescription,
            DeveloperNotes = source.DeveloperNotes,
            LastSynced = source.LastSynced,
            MergeState = source.MergeState
        };

        clone.ClearDirty();
        return clone;
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
