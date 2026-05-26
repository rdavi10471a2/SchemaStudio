using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.ColumnReconciliation;

public enum ReconciliationStatus
{
    Added,
    Removed,
    NeedsReview,
    Unchanged
}

[FileVersion("1.0")]
[AIFileContext(
    "Components/ColumnReconciliation/ReconciliationStatusEvaluator.cs",
    "Shared reconciliation classifier used by ColumnReconciliationDialog and the Manage Views Next outer Column Synchronization chip.",
    Responsibilities = "Computes ReconciliationStatus from raw ViewSourcedColumnDefinition and SchemaObjectColumnDefinition pairs so dialog chips and outer page summaries stay in sync without routing through ViewColumnDto.",
    Nuances = "Reads parsed values straight from ViewSourcedColumnDefinition (including DisableInheritance) to avoid the DTO-conversion gap that previously produced false 'changed' counts on freshly parsed base views.",
    RelatedFiles = "Components/ColumnReconciliation/ColumnReconciliationDialog.razor; Components/Pages/ManageViewsNext/ManageViewsNext.razor.cs",
    LastReviewed = "2026-05-26")]
public static class ReconciliationStatusEvaluator
{
    public static ReconciliationStatus DetermineStatus(
        ViewSourcedColumnDefinition? parsed,
        SchemaObjectColumnDefinition? saved,
        bool isBaseView)
    {
        if (saved?.MergeState is SchemaObjectColumnMergeState.DetectedAdd or SchemaObjectColumnMergeState.PendingAdd)
        {
            return ReconciliationStatus.Added;
        }

        if (saved?.MergeState is SchemaObjectColumnMergeState.DetectedRemove or SchemaObjectColumnMergeState.PendingRemove)
        {
            return ReconciliationStatus.Removed;
        }

        if (parsed != null && saved == null)
        {
            return ReconciliationStatus.Added;
        }

        if (parsed == null && saved != null)
        {
            return ReconciliationStatus.Removed;
        }

        if (parsed == null || saved == null)
        {
            return ReconciliationStatus.Unchanged;
        }

        if (!SourceRowsMatch(parsed, saved))
        {
            return ReconciliationStatus.NeedsReview;
        }

        if (!isBaseView)
        {
            return ReconciliationStatus.Unchanged;
        }

        var nameChanged = !string.Equals(NormalizeNullableText(parsed.BusinessName), NormalizeNullableText(saved.BusinessName), StringComparison.Ordinal);
        var descriptionChanged = !string.Equals(NormalizeNullableText(parsed.BusinessDescription), NormalizeNullableText(saved.BusinessDescription), StringComparison.Ordinal);
        var inheritanceChanged = parsed.DisableInheritance != saved.DisableInheritance;

        return nameChanged || descriptionChanged || inheritanceChanged
            ? ReconciliationStatus.NeedsReview
            : ReconciliationStatus.Unchanged;
    }

    private static bool SourceRowsMatch(ViewSourcedColumnDefinition parsed, SchemaObjectColumnDefinition saved) =>
        parsed.OrdinalPosition == saved.OrdinalPosition
        && string.Equals(NormalizeNullableText(parsed.ColumnKind.ToString()), NormalizeNullableText(saved.SourceColumnKind), StringComparison.Ordinal)
        && string.Equals(FormatQualifiedName(parsed.BaseDatabase, parsed.BaseSchema, parsed.BaseTable, parsed.BaseColumn), FormatQualifiedName(saved.BaseDatabaseName, saved.BaseSchemaName, saved.BaseObjectName, saved.BaseColumnName), StringComparison.Ordinal)
        && string.Equals(FormatQualifiedName(parsed.SemanticDatabase, parsed.SemanticSchema, parsed.SemanticObject, parsed.SemanticColumn), FormatQualifiedName(saved.SemanticDatabase, saved.SemanticSchema, saved.SemanticObject, saved.SemanticColumn), StringComparison.Ordinal);

    private static string? FormatQualifiedName(string? database, string? schema, string? objectName, string? columnName)
    {
        var parts = new[] { database, schema, objectName, columnName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();
        return parts.Count == 0 ? null : string.Join(".", parts);
    }

    private static string? NormalizeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
