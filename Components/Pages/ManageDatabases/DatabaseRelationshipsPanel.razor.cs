using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Data.SqlClient;
using Radzen;
using SchemaStudio.Data.Models;
using SchemaStudio.Data.Repositories;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.Services;

namespace SchemaStudioWebViewer.Components.Pages;

public partial class DatabaseRelationshipsPanel : ComponentBase
{
    [Inject] private DatabaseRelationshipRepository RelationshipRepository { get; set; } = default!;
    [Inject] private TableSchemaSmoRepository TableSchemaRepository { get; set; } = default!;
    [Inject] private NotificationService NotificationService { get; set; } = default!;
    [Inject] private TooltipService TooltipService { get; set; } = default!;
    [Inject] private RelationshipMetadataService RelationshipMetadata { get; set; } = default!;

    [Parameter]
    public DatabaseDefinition? Database { get; set; }

    private int? loadedDatabaseId;
    private string schemaName = "dbo";
    private string selectedTableName = "";
    private List<TableSchemaTableInfo> tables = new();
    private List<DatabaseRelationshipDefinition> relationships = new();
    private List<DatabaseRelationshipDefinition> childRelationships = new();
    private IList<DatabaseRelationshipDefinition> selectedRelationships = new List<DatabaseRelationshipDefinition>();
    private DatabaseRelationshipDefinition? selectedRelationship;
    private DatabaseRelationshipDefinition? editRelationship;
    private List<string> targetColumnOptions = new();
    private string targetColumnOptionsKey = "";
    private bool isLoading;
    private bool isGuideExpanded = false;
    private string statusText = "";
    private AlertStyle statusStyle = AlertStyle.Info;
    private bool isResizingRelationshipList;
    private double resizeStartX;
    private int resizeStartWidth;
    private int relationshipListWidth = RelationshipListDefaultWidth;
    private string relationshipListMode = SourceListMode;
    private string relationshipSearch = "";

    private const int RelationshipListDefaultWidth = 340;
    private const int RelationshipListMinWidth = 260;
    private const int RelationshipListMaxWidth = 560;
    private const string SourceListMode = "source";
    private const string ChildrenListMode = "children";
    private const string BaseViewActionUseLookup = "Include lookup by default";
    private const string BaseViewActionIgnore = "Do not include by default";
    private static readonly string[] joinTypes = ["LEFT JOIN", "INNER JOIN", "LEFT OUTER JOIN", "JOIN"];
    private static readonly string[] baseViewActions = [BaseViewActionUseLookup, BaseViewActionIgnore];
    private static readonly Dictionary<int, RelationshipPanelState> stateByDatabaseId = new();

    private string RelationshipsPanelClass =>
        isResizingRelationshipList ? "relationships-panel relationship-resizing" : "relationships-panel";

    private string RelationshipsPanelStyle =>
        $"--relationship-list-width: {relationshipListWidth}px;display:flex;flex-direction:column;height:100%;max-height:calc(95vh - 4rem);min-height:0;overflow:hidden;padding:1rem;box-sizing:border-box;";

    private List<DatabaseRelationshipDefinition> VisibleRelationships =>
        relationshipListMode.Equals(ChildrenListMode, StringComparison.OrdinalIgnoreCase)
            ? childRelationships
            : relationships
                .OrderByDescending(IsColookupLookup)
                .ThenByDescending(IsDefaultLookup)
                .ThenBy(RelationshipOtherTable, StringComparer.OrdinalIgnoreCase)
                .ToList();

    // Rows actually rendered: the visible tab, narrowed by the table-name search box.
    private List<DatabaseRelationshipDefinition> DisplayedRelationships
    {
        get
        {
            var visible = VisibleRelationships;
            if (string.IsNullOrWhiteSpace(relationshipSearch))
            {
                return visible;
            }

            var term = relationshipSearch.Trim();
            return visible
                .Where(relationship => RelationshipOtherTable(relationship).Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    // Default lookups (a display column detected and included by default) sort above plain joins.
    private static bool IsDefaultLookup(DatabaseRelationshipDefinition relationship) =>
        !IsChildRelationship(relationship) &&
        !string.IsNullOrWhiteSpace(relationship.DisplayColumnName) &&
        relationship.IncludeLookupByDefault;

    // COLOOKUP shared-code lookups get their own pill and sort to the very top of the list.
    // Discriminate ONLY by the shared COLOOKUP target table: FK-imported lookups also carry
    // DiscoverySource = SchemaLookup (see FromSourceLookupAsync), so keying on DiscoverySource
    // would mark every source-imported lookup as a colookup.
    private static bool IsColookupLookup(DatabaseRelationshipDefinition relationship) =>
        !IsChildRelationship(relationship) &&
        relationship.TargetTableName.Equals(RelationshipMetadataService.ColookupTargetTable, StringComparison.OrdinalIgnoreCase);

    private string RelationshipCountLabel =>
        relationshipListMode.Equals(ChildrenListMode, StringComparison.OrdinalIgnoreCase)
            ? $"{childRelationships.Count} existing children referencing {selectedTableName}"
            : $"{relationships.Count} existing lookup target(s) for {selectedTableName}";

    private string EmptyRelationshipListText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(relationshipSearch))
            {
                return $"No relationships match “{relationshipSearch.Trim()}”.";
            }

            return relationshipListMode.Equals(ChildrenListMode, StringComparison.OrdinalIgnoreCase)
                ? "No existing child relationships are stored for this table."
                : "No existing lookup relationships are stored for this table.";
        }
    }

    private bool CanImport =>
        Database is not null &&
        !isLoading &&
        !string.IsNullOrWhiteSpace(selectedTableName);

    private bool CanLoadRelationships =>
        CanImport &&
        !string.IsNullOrWhiteSpace(schemaName);

    private bool CanSaveAllRelationships =>
        Database is not null &&
        !isLoading &&
        relationships.Concat(childRelationships).Any(IsPersistableRelationship);

    private bool HasStoredRelationships =>
        relationships.Concat(childRelationships).Any(relationship => relationship.DatabaseRelationshipId != 0);

    private string LoadRelationshipsButtonText =>
        HasStoredRelationships ? "Refresh Existing" : "Load Existing";

    private string LoadRelationshipsButtonTitle =>
        HasStoredRelationships
            ? "Reload the stored relationships for the selected table, discarding any unsaved preview rows."
            : "Load the stored relationships for the selected table.";

    private string RelationshipStatusIcon => statusStyle switch
    {
        AlertStyle.Success => "check_circle",
        AlertStyle.Warning => "warning",
        AlertStyle.Danger => "error",
        _ => "info"
    };

    private string GuideToggleIcon => isGuideExpanded ? "expand_less" : "expand_more";

    private string GuideToggleText => isGuideExpanded ? "Hide" : "Show";

    private string RelationshipStatusStyle
    {
        get
        {
            var (background, border, color) = statusStyle switch
            {
                AlertStyle.Success => ("#e7f6ec", "#b7e0c4", "#1e7d43"),
                AlertStyle.Warning => ("#fdf4e3", "#f3ddab", "#8a6100"),
                AlertStyle.Danger => ("#fdecec", "#f3bcbc", "#b42318"),
                _ => ("#eef4fd", "#c9dcf7", "#1c4f9c")
            };

            return $"display:flex;align-items:center;gap:0.5rem;margin-top:0.6rem;padding:0.5rem 0.7rem;border-radius:6px;font-size:0.85rem;background:{background};border:1px solid {border};color:{color};";
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Database is null)
        {
            loadedDatabaseId = null;
            tables.Clear();
            relationships.Clear();
            childRelationships.Clear();
            selectedTableName = "";
            selectedRelationship = null;
            selectedRelationships.Clear();
            editRelationship = null;
            return;
        }

        if (loadedDatabaseId == Database.DatabaseId)
        {
            return;
        }

        loadedDatabaseId = Database.DatabaseId;
        schemaName = string.IsNullOrWhiteSpace(Database.DefaultSchema) ? "dbo" : Database.DefaultSchema;
        selectedTableName = "";
        selectedRelationship = null;
        selectedRelationships.Clear();
        editRelationship = null;
        targetColumnOptions.Clear();
        targetColumnOptionsKey = "";
        relationshipListMode = SourceListMode;

        if (stateByDatabaseId.TryGetValue(Database.DatabaseId, out var cachedState) &&
            HasUsableCachedState(cachedState))
        {
            RestoreState(cachedState);
            return;
        }

        await LoadTablesAsync();
    }

    private async Task LoadTablesAsync()
    {
        await LoadTablesAsync(keepSelectedTable: false);
    }

    private async Task LoadTablesAsync(bool keepSelectedTable)
    {
        if (Database is null || string.IsNullOrWhiteSpace(schemaName))
        {
            return;
        }

        isLoading = true;
        SetStatus("", AlertStyle.Info);
        await InvokeAsync(StateHasChanged);

        try
        {
            tables = (await TableSchemaRepository.GetTablesAsync(Database.DatabaseName, schemaName.Trim())).ToList();

            if (keepSelectedTable && tables.Any(table => table.TableName.Equals(selectedTableName, StringComparison.OrdinalIgnoreCase)))
            {
                // Keep current selection.
            }
            else
            {
                selectedTableName = tables.FirstOrDefault()?.TableName ?? "";
            }

            await LoadRelationshipsAsync(setLoading: false);
            CacheState();
        }
        catch (Exception ex)
        {
            tables.Clear();
            relationships.Clear();
            childRelationships.Clear();
            selectedRelationship = null;
            selectedRelationships.Clear();
            editRelationship = null;
            SetStatus(FriendlyError(ex), AlertStyle.Danger);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadRelationshipsAsync()
    {
        await LoadRelationshipsAsync(setLoading: true);
    }

    private async Task LoadRelationshipsAsync(bool setLoading)
    {
        if (Database is null || string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(selectedTableName))
        {
            relationships.Clear();
            childRelationships.Clear();
            selectedRelationship = null;
            selectedRelationships.Clear();
            editRelationship = null;
            CacheState();
            return;
        }

        if (setLoading)
        {
            isLoading = true;
            SetStatus("", AlertStyle.Info);
            await InvokeAsync(StateHasChanged);
        }

        try
        {
            var selectedSchema = schemaName.Trim();
            var selectedTable = selectedTableName.Trim();
            // Anchor classification to the selected table as the relationship Source only.
            // Each table's own import stores both its parent/lookup rows (Source = this table,
            // pointing to a referenced table) and its child rows (Source = this table, pointing
            // to a table that references it), so Source == selected fully covers this table's
            // relationships with the join oriented [thisTable] = [otherTable]. Rows where the
            // selected table is only the Target belong to the other table's registration and are
            // intentionally not shown here (they would otherwise appear with an inverted pill and
            // a backwards ON clause).
            var ownedRelationships = (await RelationshipRepository.GetForDatabaseAsync(Database.DatabaseId))
                .Where(relationship => IsSelectedSource(relationship, selectedSchema, selectedTable))
                .ToList();

            relationships = ownedRelationships
                .Where(relationship => !IsChildRelationship(relationship))
                .ToList();
            childRelationships = ownedRelationships
                .Where(relationship => IsChildRelationship(relationship))
                .ToList();

            selectedRelationship = VisibleRelationships.FirstOrDefault();
            selectedRelationships = selectedRelationship is null
                ? new List<DatabaseRelationshipDefinition>()
                : new List<DatabaseRelationshipDefinition> { selectedRelationship };
            editRelationship = selectedRelationship is null ? null : CloneRelationship(selectedRelationship);
            targetColumnOptions.Clear();
            targetColumnOptionsKey = "";

            if (setLoading)
            {
                SetStatus(ExistingRelationshipStatusMessage(selectedTableName), AlertStyle.Info);
            }
            CacheState();
        }
        catch (Exception ex)
        {
            relationships.Clear();
            childRelationships.Clear();
            selectedRelationship = null;
            selectedRelationships.Clear();
            editRelationship = null;
            CacheState();
            SetStatus(FriendlyError(ex), AlertStyle.Danger);
        }
        finally
        {
            if (setLoading)
            {
                isLoading = false;
            }
        }
    }

    private async Task ImportForeignKeysAsync()
    {
        if (Database is null || string.IsNullOrWhiteSpace(selectedTableName))
        {
            return;
        }

        isLoading = true;
        SetStatus("", AlertStyle.Info);
        await InvokeAsync(StateHasChanged);

        try
        {
            var details = await TableSchemaRepository.GetTableDetailsAsync(
                Database.DatabaseName,
                schemaName.Trim(),
                selectedTableName.Trim());

            var imported = 0;
            var childImported = 0;
            var failed = 0;
            string? firstFailure = null;
            foreach (var sourceRelationship in details.Relationships)
            {
                try
                {
                    var relationship = RelationshipMetadata.BuildFromSourceLookup(Database.DatabaseId, details, sourceRelationship);
                    StageRelationship(relationships, relationship);
                    imported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    firstFailure ??= FriendlyError(ex);
                }
            }

            foreach (var childRelationship in details.ChildRelationships)
            {
                try
                {
                    var relationship = RelationshipMetadata.BuildFromChildRelationship(Database.DatabaseId, details, childRelationship);
                    StageRelationship(childRelationships, relationship);
                    childImported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    firstFailure ??= FriendlyError(ex);
                }
            }

            var lookupImported = 0;
            try
            {
                foreach (var lookup in await BuildColookupLookupPreviewAsync())
                {
                    StageRelationship(relationships, CloneRelationship(lookup));
                    lookupImported++;
                }
            }
            catch (Exception ex)
            {
                failed++;
                firstFailure ??= FriendlyError(ex);
            }

            var message = failed == 0
                ? $"Loaded {imported} foreign-key relationship(s), {lookupImported} COLOOKUP lookup(s), and {childImported} child relationship(s) for {selectedTableName}. Select a row and Save Relationship (or Save All) to persist."
                : $"Loaded {imported} foreign-key relationship(s), {lookupImported} COLOOKUP lookup(s), and {childImported} child relationship(s) for {selectedTableName}; {failed} item(s) were skipped. Select a row and Save Relationship (or Save All) to persist.";
            if (!string.IsNullOrWhiteSpace(firstFailure))
            {
                message = $"{message} First failure: {firstFailure}";
            }
            SetStatus(message, failed == 0 ? AlertStyle.Success : AlertStyle.Warning);
            Notify(failed == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning, message);
            relationshipListMode = SourceListMode;
            selectedRelationship = relationships.FirstOrDefault();
            selectedRelationships = selectedRelationship is null
                ? new List<DatabaseRelationshipDefinition>()
                : new List<DatabaseRelationshipDefinition> { selectedRelationship };
            editRelationship = selectedRelationship is null ? null : CloneRelationship(selectedRelationship);
            CacheState();
        }
        catch (Exception ex)
        {
            SetStatus(FriendlyError(ex), AlertStyle.Danger);
            Notify(NotificationSeverity.Error, FriendlyError(ex));
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task<List<DatabaseRelationshipDefinition>> BuildColookupLookupPreviewAsync()
    {
        var schema = schemaName.Trim();
        var table = selectedTableName.Trim();
        var candidateNames = await TableSchemaRepository.GetLookupCandidateNamesAsync(
            Database!.DatabaseName,
            schema,
            table,
            Database.SQLLookupString);

        // Read the source table's columns so the COLOOKUP join type follows the same nullability
        // rule as FK lookups: INNER when the code column is non-nullable, else LEFT. An unknown
        // column is treated as nullable (LEFT) so a source row is never silently dropped.
        var details = await TableSchemaRepository.GetTableDetailsAsync(Database.DatabaseName, schema, table);
        var columnNullability = details.Columns.ToDictionary(
            column => column.ColumnName,
            column => column.IsNullable,
            StringComparer.OrdinalIgnoreCase);

        var prefix = $"{table}_";
        var built = new List<DatabaseRelationshipDefinition>();
        foreach (var name in candidateNames)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceColumn = name[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(sourceColumn))
            {
                continue;
            }

            var sourceColumnIsNullable = !columnNullability.TryGetValue(sourceColumn, out var isNullable) || isNullable;
            built.Add(RelationshipMetadata.BuildColookupLookupRelationship(Database.DatabaseId, schema, table, sourceColumn, name, sourceColumnIsNullable));
        }

        return built;
    }

    private void SelectRelationship(DatabaseRelationshipDefinition relationship)
    {
        selectedRelationship = relationship;
        selectedRelationships = new List<DatabaseRelationshipDefinition> { relationship };
        editRelationship = CloneRelationship(relationship);
        targetColumnOptions.Clear();
        targetColumnOptionsKey = "";
        CacheState();
    }

    private void NewRelationship()
    {
        if (Database is null)
        {
            return;
        }

        var isChildMode = relationshipListMode.Equals(ChildrenListMode, StringComparison.OrdinalIgnoreCase);
        var relationship = new DatabaseRelationshipDefinition
        {
            DatabaseId = Database.DatabaseId,
            SourceSchemaName = schemaName.Trim(),
            SourceTableName = selectedTableName.Trim(),
            TargetSchemaName = schemaName.Trim(),
            TargetTableName = "",
            JoinType = "LEFT JOIN",
            JoinExpression = "",
            DiscoverySource = isChildMode ? RelationshipMetadataService.DiscoveryManualChild : RelationshipMetadataService.DiscoveryManual,
            IncludeLookupByDefault = false
        };
        if (isChildMode)
        {
            childRelationships.Insert(0, relationship);
        }
        else
        {
            relationships.Insert(0, relationship);
        }
        selectedRelationship = relationship;
        selectedRelationships = new List<DatabaseRelationshipDefinition> { relationship };
        editRelationship = relationship;
        targetColumnOptions.Clear();
        targetColumnOptionsKey = "";
        SetStatus("New relationship started. Fill the target table and ON clause, then save.", AlertStyle.Info);
        CacheState();
    }

    private async Task SaveRelationshipAsync()
    {
        if (Database is null || editRelationship is null)
        {
            return;
        }

        try
        {
            editRelationship.DatabaseId = Database.DatabaseId;
            editRelationship.JoinExpression = editRelationship.JoinExpression?.Trim() ?? "";
            RelationshipMetadata.SynchronizeColumnPairsFromJoinExpression(editRelationship);
            if (!IsConstrainedLookupTarget(editRelationship))
            {
                editRelationship.FilterColumnName = null;
                editRelationship.FilterValue = null;
            }
            RelationshipMetadata.NormalizeRelationshipForSave(editRelationship);
            editRelationship.DatabaseRelationshipId = await RelationshipRepository.UpsertAsync(editRelationship);
            await LoadRelationshipsAsync(setLoading: false);
            selectedRelationship = relationships.FirstOrDefault(relationship =>
                relationship.DatabaseRelationshipId == editRelationship.DatabaseRelationshipId);
            selectedRelationship ??= childRelationships.FirstOrDefault(relationship =>
                relationship.DatabaseRelationshipId == editRelationship.DatabaseRelationshipId);
            selectedRelationship ??= relationships.Concat(childRelationships).FirstOrDefault(relationship =>
                string.Equals(relationship.SourceSchemaName, editRelationship.SourceSchemaName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.SourceTableName, editRelationship.SourceTableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.TargetSchemaName, editRelationship.TargetSchemaName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.TargetTableName, editRelationship.TargetTableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.JoinExpression, editRelationship.JoinExpression, StringComparison.OrdinalIgnoreCase));
            if (selectedRelationship is not null)
            {
                selectedRelationships = new List<DatabaseRelationshipDefinition> { selectedRelationship };
                editRelationship = CloneRelationship(selectedRelationship);
            }

            CacheState();
            SetStatus($"Saved relationship {RelationshipTitle(editRelationship)}.", AlertStyle.Success);
            Notify(NotificationSeverity.Success, "Relationship saved.");
        }
        catch (Exception ex)
        {
            SetStatus(FriendlyError(ex), AlertStyle.Danger);
            Notify(NotificationSeverity.Error, FriendlyError(ex));
        }
    }

    private async Task SaveAllRelationshipsAsync()
    {
        if (Database is null)
        {
            return;
        }

        SyncEditedRelationshipIntoList();

        var relationshipsToSave = relationships
            .Concat(childRelationships)
            .Where(IsPersistableRelationship)
            .Select(CloneRelationship)
            .ToList();

        if (relationshipsToSave.Count == 0)
        {
            SetStatus("No relationships are ready to save.", AlertStyle.Info);
            return;
        }

        isLoading = true;
        SetStatus("", AlertStyle.Info);
        await InvokeAsync(StateHasChanged);

        var savedCount = 0;
        var failedCount = 0;
        string? firstFailure = null;

        foreach (var relationship in relationshipsToSave)
        {
            try
            {
                relationship.DatabaseId = Database.DatabaseId;
                relationship.JoinExpression = relationship.JoinExpression?.Trim() ?? "";
                RelationshipMetadata.SynchronizeColumnPairsFromJoinExpression(relationship);
                RelationshipMetadata.NormalizeRelationshipForSave(relationship);
                await RelationshipRepository.UpsertAsync(relationship);
                savedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                firstFailure ??= FriendlyError(ex);
            }
        }

        await LoadRelationshipsAsync(setLoading: false);
        isLoading = false;

        var message = failedCount == 0
            ? $"Saved {savedCount} relationship(s)."
            : $"Saved {savedCount} relationship(s); {failedCount} failed. First failure: {firstFailure}";
        SetStatus(message, failedCount == 0 ? AlertStyle.Success : AlertStyle.Warning);
        Notify(failedCount == 0 ? NotificationSeverity.Success : NotificationSeverity.Warning, message);
    }

    private void SyncEditedRelationshipIntoList()
    {
        if (editRelationship is null)
        {
            return;
        }

        var list = IsChildRelationship(editRelationship) ? childRelationships : relationships;
        var index = list.FindIndex(relationship =>
            ReferenceEquals(relationship, selectedRelationship) ||
            (relationship.DatabaseRelationshipId != 0 &&
                relationship.DatabaseRelationshipId == editRelationship.DatabaseRelationshipId) ||
            (
                relationship.DatabaseRelationshipId == 0 &&
                string.Equals(relationship.SourceSchemaName, editRelationship.SourceSchemaName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.SourceTableName, editRelationship.SourceTableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.TargetSchemaName, editRelationship.TargetSchemaName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.TargetTableName, editRelationship.TargetTableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.SourceConstraintName, editRelationship.SourceConstraintName, StringComparison.OrdinalIgnoreCase)
            ));

        if (index >= 0)
        {
            list[index] = CloneRelationship(editRelationship);
        }
    }

    private void SetRelationshipListMode(string mode)
    {
        relationshipListMode = mode;
        selectedRelationship = VisibleRelationships.FirstOrDefault();
        selectedRelationships = selectedRelationship is null
            ? new List<DatabaseRelationshipDefinition>()
            : new List<DatabaseRelationshipDefinition> { selectedRelationship };
        editRelationship = selectedRelationship is null ? null : CloneRelationship(selectedRelationship);
        targetColumnOptions.Clear();
        targetColumnOptionsKey = "";
        CacheState();
    }

    private string RelationshipListTabClass(string mode) =>
        relationshipListMode.Equals(mode, StringComparison.OrdinalIgnoreCase)
            ? "relationship-list-tab active"
            : "relationship-list-tab";

    private string RelationshipEditorTitle =>
        editRelationship is not null && IsUnsavedRelationship(editRelationship)
            ? "New Relationship"
            : "Relationship Detail";

    private string RelationshipEditorSubtitle =>
        editRelationship is null
            ? "Select or create a relationship."
            : IsUnsavedRelationship(editRelationship)
                ? "Unsaved staged relationship. Fill the target table and ON clause, then save it."
                : IsChildRelationship(editRelationship)
                    ? "Captured 1-to-M child join metadata under the selected table."
                    : "Captured M-to-1 parent/lookup join metadata for the selected table.";

    private async Task DeleteRelationshipAsync()
    {
        if (selectedRelationship is null || selectedRelationship.DatabaseRelationshipId == 0)
        {
            return;
        }

        try
        {
            await RelationshipRepository.DeleteAsync(selectedRelationship.DatabaseRelationshipId);
            await LoadRelationshipsAsync(setLoading: false);
            CacheState();
            SetStatus("Deleted relationship.", AlertStyle.Success);
            Notify(NotificationSeverity.Success, "Relationship deleted.");
        }
        catch (Exception ex)
        {
            SetStatus(FriendlyError(ex), AlertStyle.Danger);
            Notify(NotificationSeverity.Error, FriendlyError(ex));
        }
    }

    private void ResetRelationshipEdit()
    {
        editRelationship = selectedRelationship is null ? null : CloneRelationship(selectedRelationship);
        targetColumnOptions.Clear();
        targetColumnOptionsKey = "";
    }

    private void BeginPaneResize(PointerEventArgs args)
    {
        isResizingRelationshipList = true;
        resizeStartX = args.ClientX;
        resizeStartWidth = relationshipListWidth;
    }

    private void ResizePane(PointerEventArgs args)
    {
        if (!isResizingRelationshipList)
        {
            return;
        }

        var delta = (int)Math.Round(args.ClientX - resizeStartX);
        relationshipListWidth = Math.Clamp(resizeStartWidth + delta, RelationshipListMinWidth, RelationshipListMaxWidth);
    }

    private void EndPaneResize()
    {
        isResizingRelationshipList = false;
    }

    private async Task LoadTargetColumnOptionsAsync()
    {
        if (Database is null || editRelationship is null ||
            string.IsNullOrWhiteSpace(editRelationship.TargetSchemaName) ||
            string.IsNullOrWhiteSpace(editRelationship.TargetTableName))
        {
            targetColumnOptions.Clear();
            targetColumnOptionsKey = "";
            return;
        }

        var key = string.Join("|", Database.DatabaseName, editRelationship.TargetSchemaName, editRelationship.TargetTableName);
        if (string.Equals(targetColumnOptionsKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var details = await TableSchemaRepository.GetTableDetailsAsync(
                Database.DatabaseName,
                editRelationship.TargetSchemaName.Trim(),
                editRelationship.TargetTableName.Trim());
            targetColumnOptions = details.Columns.Select(column => column.ColumnName).ToList();
            targetColumnOptionsKey = key;
        }
        catch (Exception ex)
        {
            targetColumnOptions.Clear();
            targetColumnOptionsKey = "";
            SetStatus(FriendlyError(ex), AlertStyle.Warning);
        }
    }

    private string RelationshipRowClass(DatabaseRelationshipDefinition relationship) =>
        IsSelectedRelationship(relationship)
            ? "relationship-row selected"
            : "relationship-row";

    private bool IsSelectedRelationship(DatabaseRelationshipDefinition relationship) =>
        selectedRelationship is not null &&
        (
            ReferenceEquals(selectedRelationship, relationship) ||
            (selectedRelationship.DatabaseRelationshipId != 0 &&
                selectedRelationship.DatabaseRelationshipId == relationship.DatabaseRelationshipId)
        );

    private void StageRelationship(List<DatabaseRelationshipDefinition> relationshipList, DatabaseRelationshipDefinition relationship)
    {
        var existing = relationshipList.FirstOrDefault(candidate =>
            string.Equals(candidate.SourceSchemaName, relationship.SourceSchemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.SourceTableName, relationship.SourceTableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.TargetSchemaName, relationship.TargetSchemaName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.TargetTableName, relationship.TargetTableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.DiscoverySource, relationship.DiscoverySource, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.SourceConstraintName, relationship.SourceConstraintName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            relationshipList.Add(relationship);
            return;
        }

        var index = relationshipList.IndexOf(existing);
        relationship.DatabaseRelationshipId = existing.DatabaseRelationshipId;
        relationshipList[index] = relationship;
    }

    private static bool IsSelectedSource(
        DatabaseRelationshipDefinition relationship,
        string selectedSchema,
        string selectedTable) =>
        relationship.SourceSchemaName.Equals(selectedSchema, StringComparison.OrdinalIgnoreCase) &&
        relationship.SourceTableName.Equals(selectedTable, StringComparison.OrdinalIgnoreCase);

    private string ExistingRelationshipStatusMessage(string tableName) =>
        $"Found {relationships.Count} existing lookup relationship(s) and {childRelationships.Count} existing child relationship(s) for {tableName}. Use Load From Source to preview relationships detected in the source system, then Save All to store them.";

    private static string RelationshipStatusText(DatabaseRelationshipDefinition relationship)
    {
        if (IsUnsavedRelationship(relationship))
        {
            return "Unsaved";
        }

        if (IsChildRelationship(relationship))
        {
            return "Child";
        }

        if (IsColookupLookup(relationship))
        {
            return "COLOOKUP";
        }

        if (string.IsNullOrWhiteSpace(relationship.DisplayColumnName))
        {
            return "Join Only";
        }

        return relationship.IncludeLookupByDefault ? "Default Lookup" : "Join Only";
    }

    private static string RelationshipStatusChipClass(DatabaseRelationshipDefinition relationship) =>
        IsUnsavedRelationship(relationship)
            ? "relationship-chip relationship-chip-unsaved"
            :
        IsColookupLookup(relationship)
            ? "relationship-chip relationship-chip-colookup"
            :
        IsChildRelationship(relationship) || !relationship.IncludeLookupByDefault
            ? "relationship-chip relationship-chip-warning"
            : "relationship-chip";

    private static bool IsUnsavedRelationship(DatabaseRelationshipDefinition relationship) =>
        relationship.DatabaseRelationshipId == 0;

    private static bool IsImportedRelationship(DatabaseRelationshipDefinition relationship) =>
        !IsUnsavedRelationship(relationship) &&
        (
            relationship.DiscoverySource.Equals(RelationshipMetadataService.DiscoverySchemaLookup, StringComparison.OrdinalIgnoreCase) ||
            relationship.DiscoverySource.Equals(RelationshipMetadataService.DiscoverySchemaChild, StringComparison.OrdinalIgnoreCase)
        );

    private static bool IsPersistableRelationship(DatabaseRelationshipDefinition relationship) =>
        !string.IsNullOrWhiteSpace(relationship.SourceTableName) &&
        !string.IsNullOrWhiteSpace(relationship.TargetTableName) &&
        !string.IsNullOrWhiteSpace(relationship.JoinExpression);

    private static bool IsChildRelationship(DatabaseRelationshipDefinition? relationship) =>
        relationship is not null &&
        relationship.DiscoverySource.Contains("Child", StringComparison.OrdinalIgnoreCase);

    private static bool CanEditLookupDefaults(DatabaseRelationshipDefinition? relationship) =>
        relationship is not null && !IsChildRelationship(relationship);

    private static bool IsIgnoredForBaseView(DatabaseRelationshipDefinition relationship) =>
        !relationship.IncludeLookupByDefault;

    private static bool IsConstrainedLookupTarget(DatabaseRelationshipDefinition relationship) =>
        relationship.TargetTableName.Equals("COLOOKUP", StringComparison.OrdinalIgnoreCase);

    private static string BaseViewActionFor(DatabaseRelationshipDefinition relationship) =>
        IsIgnoredForBaseView(relationship) ? BaseViewActionIgnore : BaseViewActionUseLookup;

    private void SetBaseViewAction(object? value)
    {
        if (editRelationship is null)
        {
            return;
        }

        var action = value?.ToString();
        if (string.Equals(action, BaseViewActionIgnore, StringComparison.OrdinalIgnoreCase))
        {
            editRelationship.IncludeLookupByDefault = false;
            RelationshipMetadata.NormalizeRelationshipForSave(editRelationship);
            SyncSelectedRelationshipStatus();
            return;
        }

        editRelationship.IncludeLookupByDefault = true;
        SyncSelectedRelationshipStatus();
    }

    private void OnDisplayColumnChanged(object? value)
    {
        if (editRelationship is null)
        {
            return;
        }

        editRelationship.DisplayColumnName = value?.ToString();
        RelationshipMetadata.NormalizeRelationshipForSave(editRelationship);
        SyncSelectedRelationshipStatus();
    }

    private void SyncSelectedRelationshipStatus()
    {
        if (selectedRelationship is null || editRelationship is null)
        {
            return;
        }

        selectedRelationship.IncludeLookupByDefault = editRelationship.IncludeLookupByDefault;
        selectedRelationship.DisplayColumnName = editRelationship.DisplayColumnName;
        selectedRelationship.FilterColumnName = editRelationship.FilterColumnName;
        selectedRelationship.FilterValue = editRelationship.FilterValue;
        selectedRelationship.DiscoverySource = editRelationship.DiscoverySource;
    }

    private static string RelationshipTitle(DatabaseRelationshipDefinition relationship)
    {
        if (!string.IsNullOrWhiteSpace(relationship.SourceConstraintName))
        {
            return relationship.SourceConstraintName;
        }

        return $"{relationship.SourceTableName} to {relationship.TargetTableName}";
    }

    private string RelationshipOtherTable(DatabaseRelationshipDefinition relationship)
    {
        if (relationship.SourceTableName.Equals(selectedTableName, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(relationship.TargetTableName)
                ? "New relationship"
                : relationship.TargetTableName;
        }

        if (relationship.TargetTableName.Equals(selectedTableName, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(relationship.SourceTableName)
                ? "New relationship"
                : relationship.SourceTableName;
        }

        return RelationshipTitle(relationship);
    }

    private static DatabaseRelationshipDefinition CloneRelationship(DatabaseRelationshipDefinition source) =>
        new()
        {
            DatabaseRelationshipId = source.DatabaseRelationshipId,
            DatabaseId = source.DatabaseId,
            SourceSchemaName = source.SourceSchemaName,
            SourceTableName = source.SourceTableName,
            TargetSchemaName = source.TargetSchemaName,
            TargetTableName = source.TargetTableName,
            JoinType = source.JoinType,
            JoinExpression = source.JoinExpression,
            DiscoverySource = source.DiscoverySource,
            SourceConstraintName = source.SourceConstraintName,
            IncludeLookupByDefault = source.IncludeLookupByDefault,
            DisplayColumnName = source.DisplayColumnName,
            FilterColumnName = source.FilterColumnName,
            FilterValue = source.FilterValue,
            DeveloperNotes = source.DeveloperNotes,
            CreatedOn = source.CreatedOn,
            UpdatedOn = source.UpdatedOn,
            Columns = source.Columns
                .Select(column => new DatabaseRelationshipColumnDefinition
                {
                    DatabaseRelationshipColumnId = column.DatabaseRelationshipColumnId,
                    DatabaseRelationshipId = column.DatabaseRelationshipId,
                    OrdinalPosition = column.OrdinalPosition,
                    SourceColumnName = column.SourceColumnName,
                    TargetColumnName = column.TargetColumnName
                })
                .ToList()
        };

    private void CacheState()
    {
        if (Database is null)
        {
            return;
        }

        stateByDatabaseId[Database.DatabaseId] = new RelationshipPanelState(
            schemaName,
            selectedTableName,
            tables.ToList(),
            relationships.ToList(),
            childRelationships.ToList(),
            selectedRelationship?.DatabaseRelationshipId ?? 0,
            relationshipListMode,
            statusText,
            statusStyle);
    }

    private void RestoreState(RelationshipPanelState state)
    {
        schemaName = state.SchemaName;
        selectedTableName = state.SelectedTableName;
        tables = state.Tables.ToList();
        relationships = state.Relationships.ToList();
        childRelationships = state.ChildRelationships.ToList();
        relationshipListMode = state.RelationshipListMode;
        selectedRelationship = relationships.Concat(childRelationships).FirstOrDefault(relationship =>
            relationship.DatabaseRelationshipId == state.SelectedRelationshipId);
        selectedRelationships = selectedRelationship is null
            ? new List<DatabaseRelationshipDefinition>()
            : new List<DatabaseRelationshipDefinition> { selectedRelationship };
        editRelationship = selectedRelationship is null ? null : CloneRelationship(selectedRelationship);
        statusText = state.StatusText;
        statusStyle = state.StatusStyle;
    }

    private static bool HasUsableCachedState(RelationshipPanelState state) =>
        !string.IsNullOrWhiteSpace(state.SelectedTableName) &&
        state.Tables.Count > 0;

    private void SetStatus(string message, AlertStyle style)
    {
        statusText = message;
        statusStyle = style;
    }

    private void Notify(NotificationSeverity severity, string summary)
    {
        NotificationService.Notify(new NotificationMessage
        {
            Severity = severity,
            Summary = summary,
            Duration = 3500
        });
    }

    private static string FriendlyError(Exception ex) =>
        ex is InvalidOperationException or SqlException
            ? ex.Message
            : $"Unexpected error: {ex.Message}";

    private sealed record RelationshipPanelState(
        string SchemaName,
        string SelectedTableName,
        List<TableSchemaTableInfo> Tables,
        List<DatabaseRelationshipDefinition> Relationships,
        List<DatabaseRelationshipDefinition> ChildRelationships,
        int SelectedRelationshipId,
        string RelationshipListMode,
        string StatusText,
        AlertStyle StatusStyle);
}
