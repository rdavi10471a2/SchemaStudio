using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Radzen;
using Radzen.Blazor;
using SchemaStudio.Data.Models;
using SchemaStudio.Data.Repositories;
using SchemaStudioWebViewer.Components.HelpSystem;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.Models;
using SchemaStudioWebViewer.Utils;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

public partial class BaseViewCreator : ComponentBase
{
    private const string DefaultTargetDatabaseName = "VVGBI_Integrations";
    private const string DefaultTargetSchemaName = "dbo";
    private const string DefaultMergeDestinationDatabaseName = "VVG_Silver";
    private const string DefaultMergeSchemaName = "dbo";
    private const string OutputShapeCte = "CTE";
    private const string OutputShapeStandard = "Standard";
    private const int SourcePanelDefaultWidth = 400;
    private const int SourcePanelMinWidth = 300;
    private const int SourcePanelMaxWidth = 620;
    private const int ProjectionPaneDefaultHeight = 50;
    private const int ProjectionPaneMinHeight = 25;
    private const int ProjectionPaneMaxHeight = 75;

    // Circuit-scoped state: retains the user's work across navigation away from and back to this page.
    [Inject] private BaseViewCreatorState State { get; set; } = default!;

    // Services (previously @inject in the .razor; declared here so the code-behind resolves them).
    [Inject] private DatabaseRepository DatabaseRepository { get; set; } = default!;
    [Inject] private DatabaseRelationshipRepository RelationshipRepository { get; set; } = default!;
    [Inject] private TableSchemaSmoRepository TableSchemaRepository { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private NotificationService NotificationService { get; set; } = default!;
    [Inject] private TooltipService TooltipService { get; set; } = default!;

    // Transient interaction state that intentionally resets when the page is re-entered.
    private bool IsBusy;
    private bool HighlightSqlPending;
    private bool IsResizingSourcePanel;
    private bool IsResizingProjectionPane;
    private double ResizeStartX;
    private double ResizeStartY;
    private int ResizeStartWidth;
    private int ResizeStartHeight;
    private bool MetadataDialogOpen;
    private TableSchemaColumnInfo? MetadataEditColumn;
    private TableSchemaRelationshipInfo? MetadataEditRelationship;
    private string MetadataDialogTitle = "";
    private string MetadataEditBusinessName = "";
    private string MetadataEditBusinessDescription = "";

    private static readonly int MetadataBusinessNameMaxLength = GetMaxLength(nameof(SchemaObjectColumnDefinition.BusinessName)) ?? 128;
    private static readonly int MetadataBusinessDescriptionMaxLength = GetMaxLength(nameof(SchemaObjectColumnDefinition.BusinessDescription)) ?? 500;

    private BaseViewCreatorSelectionEngine SelectionEngine { get; } = new();

    // Forwarding properties backed by the scoped state container.
    private bool IgnoreSelfJoins { get => State.IgnoreSelfJoins; set => State.IgnoreSelfJoins = value; }
    private string OutputShape { get => State.OutputShape; set => State.OutputShape = value; }
    private string StatusMessage { get => State.StatusMessage; set => State.StatusMessage = value; }
    private string RelationshipTelemetryMessage { get => State.RelationshipTelemetryMessage; set => State.RelationshipTelemetryMessage = value; }
    private string SelectedDatabaseName { get => State.SelectedDatabaseName; set => State.SelectedDatabaseName = value; }
    private string SelectedSchemaName { get => State.SelectedSchemaName; set => State.SelectedSchemaName = value; }
    private string SelectedTableName { get => State.SelectedTableName; set => State.SelectedTableName = value; }
    private string BaseAlias { get => State.BaseAlias; set => State.BaseAlias = value; }
    private string TargetDatabaseName { get => State.TargetDatabaseName; set => State.TargetDatabaseName = value; }
    private string TargetSchemaName { get => State.TargetSchemaName; set => State.TargetSchemaName = value; }
    private string TargetViewName { get => State.TargetViewName; set => State.TargetViewName = value; }
    private string ColumnFilter { get => State.ColumnFilter; set => State.ColumnFilter = value; }
    private string GeneratedSql { get => State.GeneratedSql; set => State.GeneratedSql = value; }
    private string GeneratedCreateTableSql { get => State.GeneratedCreateTableSql; set => State.GeneratedCreateTableSql = value; }
    private string TargetTableName { get => State.TargetTableName; set => State.TargetTableName = value; }
    private Dictionary<string, (string DataType, bool IsNullable)> DisplayColumnTypeCache { get => State.DisplayColumnTypeCache; set => State.DisplayColumnTypeCache = value; }
    private string GeneratedMergeSql { get => State.GeneratedMergeSql; set => State.GeneratedMergeSql = value; }
    private string MergeUnavailableReason { get => State.MergeUnavailableReason; set => State.MergeUnavailableReason = value; }
    private string MergeCountryDb { get => State.MergeCountryDb; set => State.MergeCountryDb = value; }
    private string MergeSourceDb { get => State.MergeSourceDb; set => State.MergeSourceDb = value; }
    private string MergeSourceSchema { get => State.MergeSourceSchema; set => State.MergeSourceSchema = value; }
    private string MergeDestinationDb { get => State.MergeDestinationDb; set => State.MergeDestinationDb = value; }
    private string MergeDestinationSchema { get => State.MergeDestinationSchema; set => State.MergeDestinationSchema = value; }
    private string MergeDestinationTable { get => State.MergeDestinationTable; set => State.MergeDestinationTable = value; }
    private string ActiveWorkspaceTab { get => State.ActiveWorkspaceTab; set => State.ActiveWorkspaceTab = value; }
    private string ActiveSourceInfoTab { get => State.ActiveSourceInfoTab; set => State.ActiveSourceInfoTab = value; }
    private bool MergeControlsExpanded { get => State.MergeControlsExpanded; set => State.MergeControlsExpanded = value; }
    private bool SourceGroupExpanded { get => State.SourceGroupExpanded; set => State.SourceGroupExpanded = value; }
    private bool SourcePanelCollapsed { get => State.SourcePanelCollapsed; set => State.SourcePanelCollapsed = value; }
    private int SqlRenderVersion { get => State.SqlRenderVersion; set => State.SqlRenderVersion = value; }
    private int SourcePanelWidth { get => State.SourcePanelWidth; set => State.SourcePanelWidth = value; }
    private int ProjectionPaneHeight { get => State.ProjectionPaneHeight; set => State.ProjectionPaneHeight = value; }
    private bool SqlDirty { get => State.SqlDirty; set => State.SqlDirty = value; }
    private HashSet<string> CollapsedRelationshipGroups { get => State.CollapsedRelationshipGroups; set => State.CollapsedRelationshipGroups = value; }
    private List<DatabaseDefinition> Databases { get => State.Databases; set => State.Databases = value; }
    private List<TableSchemaColumnInfo> Columns { get => State.Columns; set => State.Columns = value; }
    private List<TableSchemaRelationshipInfo> AllRelationships { get => State.AllRelationships; set => State.AllRelationships = value; }
    private List<TableSchemaRelationshipInfo> Relationships { get => State.Relationships; set => State.Relationships = value; }
    private List<TableSchemaChildRelationshipInfo> ChildRelationships { get => State.ChildRelationships; set => State.ChildRelationships = value; }
    private List<DatabaseRelationshipDefinition> SavedLookupRelationships { get => State.SavedLookupRelationships; set => State.SavedLookupRelationships = value; }
    private HashSet<string> SavedLookupRelationshipKeys { get => State.SavedLookupRelationshipKeys; set => State.SavedLookupRelationshipKeys = value; }
    private HashSet<string> SelectedLookupRelationshipKeys { get => State.SelectedLookupRelationshipKeys; set => State.SelectedLookupRelationshipKeys = value; }
    private Dictionary<string, string> BootstrapLookupDisplayColumns { get => State.BootstrapLookupDisplayColumns; set => State.BootstrapLookupDisplayColumns = value; }
    private Dictionary<string, IReadOnlyList<string>> LookupDisplayColumnOptions { get => State.LookupDisplayColumnOptions; set => State.LookupDisplayColumnOptions = value; }

    private bool CanRegenerateSql =>
        !IsBusy &&
        Columns.Count > 0 &&
        !string.IsNullOrWhiteSpace(SelectedDatabaseName) &&
        !string.IsNullOrWhiteSpace(SelectedSchemaName) &&
        !string.IsNullOrWhiteSpace(SelectedTableName) &&
        !string.IsNullOrWhiteSpace(BaseAlias) &&
        !string.IsNullOrWhiteSpace(TargetSchemaName) &&
        !string.IsNullOrWhiteSpace(TargetViewName);

    private bool CanCopySql =>
        !SqlDirty && !string.IsNullOrWhiteSpace(GeneratedSql);

    private string RegenerateButtonClass =>
        SqlDirty ? "bvg-button primary" : "bvg-button secondary";

    private string LayoutClass =>
        (SourcePanelCollapsed, IsResizingSourcePanel) switch
        {
            (true, _) => "bvg-layout source-collapsed",
            (_, true) => "bvg-layout bvg-resizing",
            _ when IsResizingProjectionPane => "bvg-layout bvg-resizing-height",
            _ => "bvg-layout"
        };

    private string LayoutStyle =>
        $"{(SourcePanelCollapsed ? "" : $"--source-pane-width: {SourcePanelWidth}px;")} --projection-pane-height: {ProjectionPaneHeight}%;";

    private string TargetViewPreview =>
        string.IsNullOrWhiteSpace(TargetViewName)
            ? "Select a table to generate a target view name."
            : $"CREATE OR ALTER VIEW {TargetViewNameSql}";

    private string TargetViewNameSql =>
        QualifiedName(TargetSchemaName, TargetViewName);

    private string TargetTableNameSql =>
        QualifiedName(TargetSchemaName, string.IsNullOrWhiteSpace(TargetTableName) ? TargetViewName : TargetTableName);

    private string TargetTablePreview =>
        string.IsNullOrWhiteSpace(TargetViewName) && string.IsNullOrWhiteSpace(TargetTableName)
            ? ""
            : $"CREATE TABLE {TargetTableNameSql}";

    private static string DisplayColumnCacheKey(TableSchemaRelationshipInfo relationship) =>
        $"{relationship.ReferencedSchemaName}.{relationship.ReferencedTableName}.{relationship.DisplayColumnName}";

    private string GetDisplayColumnDataType(TableSchemaRelationshipInfo relationship) =>
        DisplayColumnTypeCache.TryGetValue(DisplayColumnCacheKey(relationship), out var cached) ? cached.DataType : "";

    private bool GetDisplayColumnIsNullable(TableSchemaRelationshipInfo relationship) =>
        !DisplayColumnTypeCache.TryGetValue(DisplayColumnCacheKey(relationship), out var cached) || cached.IsNullable;

    private string SelectedTableDisplayName =>
        string.IsNullOrWhiteSpace(SelectedTableName)
            ? "the selected table"
            : QualifiedName(SelectedDatabaseName, SelectedSchemaName, SelectedTableName);

    private IEnumerable<TableSchemaColumnInfo> FilteredColumns =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Columns
            : Columns.Where(ColumnMatchesFilter);

    private IEnumerable<TableSchemaColumnInfo> FilteredStandaloneColumns =>
        FilteredColumns.Where(column => !RelationshipOwnsEditorColumn(column.ColumnName));

    private IEnumerable<TableSchemaRelationshipInfo> FilteredRelationships =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Relationships
            : Relationships.Where(RelationshipMatchesFilter);

    private int FilteredSelectProjectionRowCount =>
        FilteredStandaloneColumns.Count() +
        FilteredRelationships.Sum(relationship =>
            (RelationshipOwnsEditorProjectionColumns(relationship) ? GetRelationshipColumns(relationship).Count() : 0) +
            (HasDisplayColumn(relationship) ? 1 : 0));

    private BaseViewCreatorSelectionPlan CurrentSelectionPlan =>
        SelectionEngine.Build(Columns, Relationships);

    private string FilterText =>
        ColumnFilter.Trim();

    private bool IsFiltering =>
        !string.IsNullOrWhiteSpace(FilterText);

    private IReadOnlyList<TableSchemaRelationshipInfo> LookupRelationshipCandidates =>
        AllRelationships
            .Where(CanReviewLookupRelationship)
            .OrderBy(relationship => relationship.LocalColumns, StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.ReferencedTableName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private string TabClass(string tabName) =>
        ActiveWorkspaceTab == tabName
            ? "bvg-tab active"
            : "bvg-tab";

    private string SourceInfoTabClass(string tabName) =>
        ActiveSourceInfoTab == tabName
            ? "bvg-mini-tab active"
            : "bvg-mini-tab";

    protected override async Task OnInitializedAsync()
    {
        // State is circuit-scoped: on a return visit it already holds the user's work, so skip the
        // one-time load (which would reset selections) and just re-render the restored state.
        if (State.IsInitialized)
        {
            // Returning to the page: state (including any generated SQL and the active tab) is
            // restored, so re-highlight the restored SQL after this render instead of leaving it plain.
            QueueSqlHighlight();
            return;
        }

        await RunPageOperationAsync(async () =>
        {
            Databases = (await DatabaseRepository.GetAllAsync()).ToList();
            if (Databases.Count == 0)
            {
                StatusMessage = "No SchemaStudio database definitions are available.";
                return;
            }

            SelectDatabase(Databases[0].DatabaseName);
        }, "Failed to load databases.");

        State.IsInitialized = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (HighlightSqlPending)
        {
            HighlightSqlPending = false;
            await JSRuntime.InvokeVoidAsync("highlightSql", 30);
        }
    }

    private async Task SetWorkspaceTab(string tabName)
    {
        ActiveWorkspaceTab = tabName == "projection" ? "tree" : tabName;

        if (ActiveWorkspaceTab == "table")
        {
            await EnsureDisplayColumnTypesAsync();
        }

        QueueSqlHighlight();
    }

    private async Task EnsureDisplayColumnTypesAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabaseName))
        {
            return;
        }

        var pending = Relationships
            .Where(relationship => relationship.Include &&
                relationship.IncludeDisplayColumn &&
                !string.IsNullOrWhiteSpace(relationship.DisplayColumnName) &&
                !DisplayColumnTypeCache.ContainsKey(DisplayColumnCacheKey(relationship)))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var relationship in pending)
        {
            try
            {
                var details = await TableSchemaRepository.GetTableDetailsAsync(
                    SelectedDatabaseName,
                    relationship.ReferencedSchemaName,
                    relationship.ReferencedTableName);
                var column = details.Columns.FirstOrDefault(candidate =>
                    string.Equals(candidate.ColumnName, relationship.DisplayColumnName, StringComparison.OrdinalIgnoreCase));
                if (column is not null)
                {
                    DisplayColumnTypeCache[DisplayColumnCacheKey(relationship)] = (UdtTypeResolver.ResolveByName(column.DataType), column.IsNullable);
                }
            }
            catch
            {
            }
        }

        RegenerateSql();
        await InvokeAsync(StateHasChanged);
    }

    private void SetSourceInfoTab(string tabName)
    {
        ActiveSourceInfoTab = tabName;
    }

    private void ToggleSourcePanel()
    {
        SourcePanelCollapsed = !SourcePanelCollapsed;
    }

    private void ToggleMergeControls()
    {
        MergeControlsExpanded = !MergeControlsExpanded;
    }

    private void BeginSourcePaneResize(PointerEventArgs args)
    {
        IsResizingSourcePanel = true;
        IsResizingProjectionPane = false;
        ResizeStartX = args.ClientX;
        ResizeStartWidth = SourcePanelWidth;
    }

    private void BeginProjectionPaneResize(PointerEventArgs args)
    {
        IsResizingProjectionPane = true;
        IsResizingSourcePanel = false;
        ResizeStartY = args.ClientY;
        ResizeStartHeight = ProjectionPaneHeight;
    }

    private void ResizePane(PointerEventArgs args)
    {
        if (!IsResizingSourcePanel && !IsResizingProjectionPane)
        {
            return;
        }

        if (IsResizingSourcePanel)
        {
            var delta = (int)Math.Round(args.ClientX - ResizeStartX);
            SourcePanelWidth = Math.Clamp(ResizeStartWidth + delta, SourcePanelMinWidth, SourcePanelMaxWidth);
            return;
        }

        var verticalDelta = (int)Math.Round(args.ClientY - ResizeStartY);
        var nextHeight = ResizeStartHeight + (verticalDelta / 8);
        ProjectionPaneHeight = Math.Clamp(nextHeight, ProjectionPaneMinHeight, ProjectionPaneMaxHeight);
    }

    private void EndPaneResize()
    {
        IsResizingSourcePanel = false;
        IsResizingProjectionPane = false;
    }

    private void ToggleSourceGroup()
    {
        SourceGroupExpanded = !SourceGroupExpanded;
    }

    private void ToggleRelationshipGroup(TableSchemaRelationshipInfo relationship)
    {
        var key = RelationshipGroupKey(relationship);
        if (!CollapsedRelationshipGroups.Add(key))
        {
            CollapsedRelationshipGroups.Remove(key);
        }
    }

    private void ExpandProjectionGroup()
    {
        SourceGroupExpanded = true;
    }

    private void CollapseProjectionGroup()
    {
        SourceGroupExpanded = false;
    }

    private void ExpandJoinGroups()
    {
        CollapsedRelationshipGroups.Clear();
    }

    private void CollapseJoinGroups()
    {
        CollapsedRelationshipGroups = Relationships
            .Select(RelationshipGroupKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool IsRelationshipGroupExpanded(TableSchemaRelationshipInfo relationship) =>
        !CollapsedRelationshipGroups.Contains(RelationshipGroupKey(relationship));

    private static string RelationshipGroupKey(TableSchemaRelationshipInfo relationship) =>
        string.IsNullOrWhiteSpace(relationship.ForeignKeyName)
            ? $"{relationship.ReferencedSchemaName}.{relationship.ReferencedTableName}.{relationship.LocalColumns}"
            : relationship.ForeignKeyName;

    private async Task OnDatabaseChangedAsync(object value)
    {
        SelectDatabase(value?.ToString() ?? "");
        await Task.CompletedTask;
    }

    private void SelectDatabase(string databaseName)
    {
        SelectedDatabaseName = databaseName;
        SelectedTableName = "";
        Columns.Clear();
        AllRelationships.Clear();
        BootstrapLookupDisplayColumns.Clear();
        Relationships.Clear();
        ChildRelationships.Clear();
        ClearLookupRelationshipState();
        GeneratedSql = "";
        SqlDirty = false;
        RelationshipTelemetryMessage = "";
        StatusMessage = "";

        var selectedDatabase = Databases.FirstOrDefault(database => string.Equals(database.DatabaseName, SelectedDatabaseName, StringComparison.OrdinalIgnoreCase));
        SelectedSchemaName = string.IsNullOrWhiteSpace(selectedDatabase?.DefaultSchema)
            ? DefaultTargetSchemaName
            : selectedDatabase.DefaultSchema;

        StatusMessage = string.IsNullOrWhiteSpace(SelectedDatabaseName)
            ? "Select a source database."
            : $"Selected [{SelectedDatabaseName}].[{SelectedSchemaName}]. Type a table name and click Load.";
    }

    private void OnSchemaNameChanged(ChangeEventArgs args)
    {
        SelectedSchemaName = args.Value?.ToString()?.Trim() ?? "";
        SelectedTableName = "";
        Columns.Clear();
        AllRelationships.Clear();
        BootstrapLookupDisplayColumns.Clear();
        Relationships.Clear();
        ChildRelationships.Clear();
        ClearLookupRelationshipState();
        GeneratedSql = "";
        SqlDirty = false;
        RelationshipTelemetryMessage = "";
        StatusMessage = string.IsNullOrWhiteSpace(SelectedSchemaName)
            ? "Select a source schema before loading a table."
            : $"Schema [{SelectedSchemaName}] selected. Type a table name and click Load.";
    }

    private void OnTableNameChanged(ChangeEventArgs args)
    {
        SelectedTableName = args.Value?.ToString()?.Trim() ?? "";
        BaseAlias = SelectedTableName;
        Columns.Clear();
        AllRelationships.Clear();
        BootstrapLookupDisplayColumns.Clear();
        Relationships.Clear();
        ChildRelationships.Clear();
        ClearLookupRelationshipState();
        GeneratedSql = "";
        SqlDirty = false;
        RelationshipTelemetryMessage = "";
        UpdateTargetViewNameFromBaseAlias();
        StatusMessage = string.IsNullOrWhiteSpace(SelectedTableName)
            ? "Type a table name and click Load."
            : $"Ready to check [{SelectedDatabaseName}].[{SelectedSchemaName}].[{SelectedTableName}].";
    }

    private async Task LoadSelectedTableAsync()
    {
        await LoadTableDetailsAsync();
    }

    private async Task RefreshMetadataAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedTableName))
        {
            StatusMessage = string.IsNullOrWhiteSpace(SelectedDatabaseName) || string.IsNullOrWhiteSpace(SelectedSchemaName)
                ? "Select a source database and schema, then type a table name."
                : $"Ready to check [{SelectedDatabaseName}].[{SelectedSchemaName}]. Type a table name and click Load.";
            return;
        }

        await LoadTableDetailsAsync();
    }

    private async Task LoadTableDetailsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabaseName) ||
            string.IsNullOrWhiteSpace(SelectedSchemaName) ||
            string.IsNullOrWhiteSpace(SelectedTableName))
        {
            return;
        }

        await RunPageOperationAsync(async () =>
        {
            var selectedDatabase = Databases.FirstOrDefault(database => string.Equals(database.DatabaseName, SelectedDatabaseName, StringComparison.OrdinalIgnoreCase));
            var details = await TableSchemaRepository.GetTableDetailsAsync(
                SelectedDatabaseName,
                SelectedSchemaName,
                SelectedTableName);
            Columns = details.Columns.ToList();
            AllRelationships = details.Relationships.ToList();
            BootstrapLookupDisplayColumns = AllRelationships
                .Where(CanReviewLookupRelationship)
                .ToDictionary(BuildRelationshipRegistryKey, relationship => relationship.DisplayColumnName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            ChildRelationships = details.ChildRelationships.ToList();
            await LoadSavedLookupRelationshipsAsync(selectedDatabase);
            ApplySavedLookupRelationships();
            ApplyRelationshipFilters();
            CollapseJoinGroups();
            DisplayColumnTypeCache.Clear();
            MergeCountryDb = "";
            MergeSourceDb = "";
            MergeSourceSchema = DefaultMergeSchemaName;
            MergeDestinationDb = DefaultMergeDestinationDatabaseName;
            MergeDestinationSchema = DefaultMergeSchemaName;
            MergeDestinationTable = string.IsNullOrWhiteSpace(TargetTableName) ? "" : TargetTableName;
            GeneratedMergeSql = "";
            MergeUnavailableReason = "";
            RegenerateSql();
            await EnsureDisplayColumnTypesAsync();
            StatusMessage = BuildLoadedStatusMessage();
        }, $"Failed to load table details from [{SelectedDatabaseName}].[{SelectedSchemaName}].[{SelectedTableName}].");
    }

    private void ApplyRelationshipFilters()
    {
        var filteredRelationships = AllRelationships
            .Where(ShouldShowLookupRelationship);

        if (IgnoreSelfJoins)
        {
            filteredRelationships = filteredRelationships.Where(relationship =>
                !string.Equals(relationship.ReferencedSchemaName, SelectedSchemaName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(relationship.ReferencedTableName, SelectedTableName, StringComparison.OrdinalIgnoreCase));
        }

        Relationships = filteredRelationships.ToList();
        RelationshipTelemetryMessage = BuildRelationshipTelemetryMessage();
    }

    private void ApplyRelationshipFiltersAndMarkDirty()
    {
        ApplyRelationshipFilters();
        MarkSqlDirty();

        if (Columns.Count > 0)
        {
            StatusMessage = BuildLoadedStatusMessage();
        }
    }

    private async Task LoadSavedLookupRelationshipsAsync(DatabaseDefinition? selectedDatabase)
    {
        ClearLookupRelationshipState();

        if (selectedDatabase is null || selectedDatabase.DatabaseId == 0)
        {
            return;
        }

        SavedLookupRelationships = (await RelationshipRepository.GetForDatabaseAsync(selectedDatabase.DatabaseId))
            .Where(relationship =>
                string.Equals(relationship.SourceSchemaName, SelectedSchemaName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationship.SourceTableName, SelectedTableName, StringComparison.OrdinalIgnoreCase) &&
                !IsChildRelationship(relationship))
            .ToList();
        SavedLookupRelationshipKeys = SavedLookupRelationships
            .Select(BuildRelationshipRegistryKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void ApplySavedLookupRelationships()
    {
        foreach (var savedRelationship in SavedLookupRelationships)
        {
            var existing = AllRelationships.FirstOrDefault(relationship => IsSameRelationship(relationship, savedRelationship));

            if (existing is not null)
            {
                if (!string.IsNullOrWhiteSpace(savedRelationship.DisplayColumnName))
                {
                    existing.DisplayColumnName = savedRelationship.DisplayColumnName;
                }

                existing.SelectedJoinType = savedRelationship.JoinType;
                existing.Include = savedRelationship.IncludeLookupByDefault &&
                    !string.IsNullOrWhiteSpace(existing.DisplayColumnName);
                existing.IncludeDisplayColumn = existing.Include;
                continue;
            }

            if (savedRelationship.Columns.Count == 0)
            {
                continue;
            }

            AllRelationships.Add(new TableSchemaRelationshipInfo(
                string.IsNullOrWhiteSpace(savedRelationship.SourceConstraintName)
                    ? $"REGISTERED_{savedRelationship.SourceTableName}_{savedRelationship.TargetTableName}"
                    : savedRelationship.SourceConstraintName,
                savedRelationship.TargetSchemaName,
                savedRelationship.TargetTableName,
                savedRelationship.DisplayColumnName,
                string.Equals(savedRelationship.JoinType, "INNER JOIN", StringComparison.OrdinalIgnoreCase),
                savedRelationship.JoinType,
                savedRelationship.Columns
                    .OrderBy(column => column.OrdinalPosition)
                    .Select(column => new TableSchemaForeignKeyColumnInfo(column.SourceColumnName, column.TargetColumnName))
                    .ToList(),
                savedRelationship.FilterColumnName,
                savedRelationship.FilterValue)
            {
                Include = savedRelationship.IncludeLookupByDefault &&
                    !string.IsNullOrWhiteSpace(savedRelationship.DisplayColumnName),
                IncludeDisplayColumn = savedRelationship.IncludeLookupByDefault &&
                    !string.IsNullOrWhiteSpace(savedRelationship.DisplayColumnName)
            });
        }
    }

    private void ClearLookupRelationshipState()
    {
        SavedLookupRelationships.Clear();
        SavedLookupRelationshipKeys.Clear();
        SelectedLookupRelationshipKeys.Clear();
        LookupDisplayColumnOptions.Clear();
    }

    private async Task EnsureLookupDisplayColumnOptionsAsync(TableSchemaRelationshipInfo relationship)
    {
        var targetKey = BuildLookupTargetKey(relationship);
        if (LookupDisplayColumnOptions.ContainsKey(targetKey))
        {
            return;
        }

        try
        {
            var lookupDetails = await TableSchemaRepository.GetTableDetailsAsync(
                SelectedDatabaseName,
                relationship.ReferencedSchemaName,
                relationship.ReferencedTableName);

            LookupDisplayColumnOptions[targetKey] = lookupDetails.Columns
                .Select(column => column.ColumnName)
                .OrderBy(columnName => columnName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            LookupDisplayColumnOptions[targetKey] = Array.Empty<string>();
        }
    }

    private void OnLookupRelationshipSelectedChanged(string key, ChangeEventArgs args)
    {
        if (ToBool(args.Value))
        {
            SelectedLookupRelationshipKeys.Add(key);
        }
        else
        {
            SelectedLookupRelationshipKeys.Remove(key);
        }
    }

    private void OnLookupRelationshipDisplayColumnChanged(TableSchemaRelationshipInfo relationship, object? value)
    {
        ApplyLookupRelationshipDisplayColumn(relationship, value?.ToString());
    }

    private void ApplyLookupRelationshipDisplayColumn(TableSchemaRelationshipInfo relationship, string? value)
    {
        relationship.DisplayColumnName = NormalizeOptionalString(value);
        relationship.IncludeDisplayColumn = !string.IsNullOrWhiteSpace(relationship.DisplayColumnName);

        var key = BuildRelationshipRegistryKey(relationship);
        if (CanSaveLookupRelationship(relationship) && ShouldSaveLookupRelationship(relationship))
        {
            SelectedLookupRelationshipKeys.Add(key);
        }
        else
        {
            SelectedLookupRelationshipKeys.Remove(key);
        }

        ApplyRelationshipFiltersAndMarkDirty();
    }

    private async Task SaveSelectedLookupRelationshipsAsync()
    {
        var selectedDatabase = Databases.FirstOrDefault(database => string.Equals(database.DatabaseName, SelectedDatabaseName, StringComparison.OrdinalIgnoreCase));
        if (selectedDatabase is null)
        {
            NotifyError("Select a database before saving lookup relationships.");
            return;
        }

        var relationshipsToSave = LookupRelationshipCandidates
            .Where(relationship => SelectedLookupRelationshipKeys.Contains(BuildRelationshipRegistryKey(relationship)))
            .Where(CanSaveLookupRelationship)
            .Where(ShouldSaveLookupRelationship)
            .ToList();

        var savedCount = 0;
        foreach (var relationship in relationshipsToSave)
        {
            await RelationshipRepository.UpsertAsync(BuildLookupRelationshipDefinition(selectedDatabase.DatabaseId, relationship));
            savedCount++;
        }

        await LoadSavedLookupRelationshipsAsync(selectedDatabase);
        SelectedLookupRelationshipKeys.Clear();
        NotificationService.Notify(NotificationSeverity.Success, $"Saved {savedCount} lookup relationship(s).", "", 3500);
    }

    private void OnIgnoreSelfJoinsChanged(ChangeEventArgs args)
    {
        IgnoreSelfJoins = ToBool(args.Value);
        ApplyRelationshipFiltersAndMarkDirty();
    }

    private void OnOutputShapeChanged(ChangeEventArgs args)
    {
        OutputShape = args.Value?.ToString() == OutputShapeStandard
            ? OutputShapeStandard
            : OutputShapeCte;

        if (CanRegenerateSql)
        {
            RegenerateSql();
            return;
        }

        MarkSqlDirty();
    }

    private void OnBaseAliasChanged(ChangeEventArgs args)
    {
        BaseAlias = args.Value?.ToString() ?? "";
        UpdateTargetViewNameFromBaseAlias();
        MarkSqlDirty();
    }

    private void UpdateTargetViewNameFromBaseAlias()
    {
        var viewNameToken = string.IsNullOrWhiteSpace(BaseAlias)
            ? SelectedTableName
            : BaseAlias;

        TargetViewName = string.IsNullOrWhiteSpace(SelectedDatabaseName) || string.IsNullOrWhiteSpace(viewNameToken)
            ? ""
            : $"SS_{SelectedDatabaseName}_{viewNameToken}";
    }

    private void OnTargetDatabaseChanged(ChangeEventArgs args)
    {
        TargetDatabaseName = args.Value?.ToString() ?? "";
        MarkSqlDirty();
    }

    private void OnTargetSchemaChanged(ChangeEventArgs args)
    {
        TargetSchemaName = args.Value?.ToString() ?? "";
        MarkSqlDirty();
    }

    private void OnTargetViewNameChanged(ChangeEventArgs args)
    {
        TargetViewName = args.Value?.ToString() ?? "";
        MarkSqlDirty();
    }

    private void OnTargetTableNameChanged(ChangeEventArgs args)
    {
        TargetTableName = args.Value?.ToString() ?? "";
        MarkSqlDirty();
    }

    private void OnRelationshipDisplayColumnChanged(TableSchemaRelationshipInfo relationship, ChangeEventArgs args)
    {
        relationship.DisplayColumnName = args.Value?.ToString();
        MarkSqlDirty();
    }

    private void OnColumnIncludeChanged(TableSchemaColumnInfo column, ChangeEventArgs args)
    {
        column.Include = ToBool(args.Value);
        SyncRelationshipIncludeFromProjectionRows(column);
        MarkSqlDirty();
    }

    private void OnRelationshipIncludeChanged(TableSchemaRelationshipInfo relationship, ChangeEventArgs args)
    {
        var include = ToBool(args.Value);
        relationship.Include = include;
        relationship.IncludeDisplayColumn = include && HasDisplayColumn(relationship);

        MarkSqlDirty();
    }

    private void OnRelationshipDisplayIncludeChanged(TableSchemaRelationshipInfo relationship, ChangeEventArgs args)
    {
        relationship.IncludeDisplayColumn = ToBool(args.Value);
        SyncRelationshipIncludeFromProjectionRows(relationship);
        MarkSqlDirty();
    }

    private void SyncRelationshipIncludeFromProjectionRows(TableSchemaColumnInfo changedColumn)
    {
        foreach (var relationship in Relationships.Where(RelationshipOwnsEditorProjectionColumns))
        {
            if (relationship.Columns.Any(pair => string.Equals(pair.LocalColumnName, changedColumn.ColumnName, StringComparison.OrdinalIgnoreCase)))
            {
                SyncRelationshipIncludeFromProjectionRows(relationship);
            }
        }
    }

    private void SyncRelationshipIncludeFromProjectionRows(TableSchemaRelationshipInfo relationship)
    {
        if (!RelationshipOwnsEditorProjectionColumns(relationship))
        {
            return;
        }

        relationship.Include = relationship.IncludeDisplayColumn && HasDisplayColumn(relationship);
    }

    private void OnRelationshipJoinTypeChanged(TableSchemaRelationshipInfo relationship, ChangeEventArgs args)
    {
        relationship.SelectedJoinType = args.Value?.ToString() == "LEFT JOIN"
            ? "LEFT JOIN"
            : "INNER JOIN";
        MarkSqlDirty();
    }

    private void OnColumnFilterChanged(ChangeEventArgs args)
    {
        ColumnFilter = args.Value?.ToString() ?? "";
    }

    private string BuildLoadedStatusMessage()
    {
        var generatedJoinCount = CurrentSelectionPlan.JoinDependencies.Count;
        var message = $"Loaded {Columns.Count} columns and {Relationships.Count} visible M-to-1 relationships from [{SelectedDatabaseName}].[{SelectedSchemaName}].[{SelectedTableName}]. SQL will generate {generatedJoinCount} join(s).";
        return string.IsNullOrWhiteSpace(RelationshipTelemetryMessage)
            ? message
            : $"{message} {RelationshipTelemetryMessage}";
    }

    private string BuildRelationshipTelemetryMessage()
    {
        var relationshipsWithoutDisplay = AllRelationships
            .Where(relationship => !HasDisplayColumn(relationship))
            .ToList();

        if (relationshipsWithoutDisplay.Count == 0)
        {
            return "";
        }

        var examples = relationshipsWithoutDisplay
            .Take(5)
            .Select(relationship => $"{relationship.LocalColumns}->{relationship.ReferencedTableName}")
            .ToList();

        var suffix = relationshipsWithoutDisplay.Count > examples.Count
            ? $", +{relationshipsWithoutDisplay.Count - examples.Count} more"
            : "";
        return $"Detected {relationshipsWithoutDisplay.Count} FK relationship(s) without display columns: {string.Join(", ", examples)}{suffix}.";
    }

    private static bool HasDisplayColumn(TableSchemaRelationshipInfo relationship) =>
        !string.IsNullOrWhiteSpace(relationship.DisplayColumnName);

    private bool IsJoinSuppressed(TableSchemaRelationshipInfo relationship) =>
        !HasDisplayColumn(relationship);

    private bool IsSuppressedRelationshipColumn(TableSchemaColumnInfo column) =>
        AllRelationships
            .Where(IsJoinSuppressed)
            .SelectMany(relationship => relationship.Columns)
            .Any(pair => string.Equals(pair.LocalColumnName, column.ColumnName, StringComparison.OrdinalIgnoreCase));

    private string ColumnKeyRoleLabel(TableSchemaColumnInfo column) =>
        column.KeyRole;

    private bool ShouldGenerateJoin(TableSchemaRelationshipInfo relationship) =>
        CurrentSelectionPlan.RelationshipHasLookupDisplayProjection(relationship);

    private bool CanUseRelationship(TableSchemaRelationshipInfo relationship) =>
        HasDisplayColumn(relationship);

    private static bool ShouldShowLookupRelationship(TableSchemaRelationshipInfo relationship) =>
        HasDisplayColumn(relationship);

    private string RelationshipStatusText(TableSchemaRelationshipInfo relationship)
    {
        if (IsJoinSuppressed(relationship))
        {
            return "Join disabled -- no display column chosen";
        }

        if (!relationship.Include)
        {
            return "Join disabled -- projection locked until join is enabled";
        }

        if (!relationship.IncludeDisplayColumn)
        {
            return "Lookup display off";
        }

        return "Ready";
    }

    private string RelationshipStatusClass(TableSchemaRelationshipInfo relationship)
    {
        if (IsJoinSuppressed(relationship))
        {
            return "bvg-status-error";
        }

        if (!relationship.Include || !relationship.IncludeDisplayColumn)
        {
            return "bvg-status-warning";
        }

        return "bvg-status-ok";
    }

    private void MarkSqlDirty()
    {
        if (!CanRegenerateSql)
        {
            GeneratedSql = "";
            GeneratedCreateTableSql = "";
            GeneratedMergeSql = "";
            MergeUnavailableReason = "";
            SqlRenderVersion++;
            SqlDirty = false;
            return;
        }

        SqlDirty = true;
    }

    private void QueueSqlHighlight()
    {
        HighlightSqlPending =
            (ActiveWorkspaceTab == "sql" && !string.IsNullOrWhiteSpace(GeneratedSql)) ||
            (ActiveWorkspaceTab == "table" && !string.IsNullOrWhiteSpace(GeneratedCreateTableSql)) ||
            (ActiveWorkspaceTab == "merge" && string.IsNullOrWhiteSpace(MergeUnavailableReason) && !string.IsNullOrWhiteSpace(GeneratedMergeSql));
    }

    private string ProjectionColumnRowClass(TableSchemaColumnInfo column) =>
        IsSuppressedRelationshipColumn(column)
            ? "bvg-output-cell bvg-muted-row"
            : "bvg-output-cell";

    private string ProjectionDisplayRowClass(TableSchemaRelationshipInfo relationship) =>
        relationship.Include && relationship.IncludeDisplayColumn
            ? "bvg-output-cell"
            : "bvg-output-cell bvg-muted-row";

    private string ProjectionRelationshipGroupClass(TableSchemaRelationshipInfo relationship)
    {
        if (IsJoinSuppressed(relationship))
        {
            return "bvg-projection-group bvg-muted-row";
        }

        return relationship.Include
            ? "bvg-projection-group"
            : "bvg-projection-group bvg-disabled-group";
    }

    private string TreeColumnRowClass(TableSchemaColumnInfo column) =>
        IsSuppressedRelationshipColumn(column)
            ? "bvg-tree-row bvg-muted-row"
            : "bvg-tree-row";

    private string TreeDisplayRowClass(TableSchemaRelationshipInfo relationship) =>
        relationship.Include
            ? "bvg-tree-row"
            : "bvg-tree-row bvg-disabled-group";

    private string TreeRelationshipNodeClass(TableSchemaRelationshipInfo relationship)
    {
        if (IsJoinSuppressed(relationship))
        {
            return "bvg-tree-node bvg-muted-row";
        }

        return relationship.Include
            ? "bvg-tree-node"
            : "bvg-tree-node bvg-disabled-group";
    }

    private string BuildBaseProjectionText(TableSchemaColumnInfo column) =>
        $"{QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(column.ColumnName)}";

    private string BuildDisplayProjectionText(TableSchemaRelationshipInfo relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship.DisplayColumnName))
        {
            return "(no display column selected)";
        }

        return $"{QuoteIdentifier(BuildLookupAlias(relationship))}.{QuoteIdentifier(relationship.DisplayColumnName)} AS {QuoteIdentifier(BuildLookupProjectionAlias(relationship))}";
    }

    private string BuildRelationshipSummaryText(TableSchemaRelationshipInfo relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship.DisplayColumnName))
        {
            return "-- No display projection";
        }

        return $"-- Projects {QuoteIdentifier(relationship.DisplayColumnName)} AS {QuoteIdentifier(BuildLookupProjectionAlias(relationship))}";
    }

    private string BuildJoinText(TableSchemaRelationshipInfo relationship)
    {
        var joinLine = BuildJoinLines(relationship).First();
        return joinLine
            .Replace(" ON ", $"{Environment.NewLine}ON ", StringComparison.Ordinal)
            .Replace(" AND ", $"{Environment.NewLine}AND ", StringComparison.Ordinal);
    }

    private bool IsColumnIncluded(string columnName) =>
        Columns.Any(column => column.Include && string.Equals(column.ColumnName, columnName, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<TableSchemaColumnInfo> GetRelationshipColumns(TableSchemaRelationshipInfo relationship) =>
        relationship.Columns
            .Select(pair => Columns.FirstOrDefault(column => string.Equals(column.ColumnName, pair.LocalColumnName, StringComparison.OrdinalIgnoreCase)))
            .Where(column => column is not null)
            .Select(column => column!);

    private bool RelationshipMatchesFilter(TableSchemaRelationshipInfo relationship)
    {
        var filter = FilterText;
        if (TextMatchesFilter(relationship.LocalColumns, filter) ||
            TextMatchesFilter(relationship.ReferencedColumns, filter) ||
            TextMatchesFilter(relationship.ReferencedTableName, filter) ||
            TextMatchesFilter(relationship.ReferencedSchemaName, filter) ||
            TextMatchesFilter(relationship.ForeignKeyName, filter) ||
            TextMatchesFilter(relationship.DisplayColumnName, filter) ||
            TextMatchesFilter(relationship.LookupFilterColumnName, filter) ||
            TextMatchesFilter(relationship.LookupFilterValue, filter) ||
            TextMatchesFilter(RelationshipStatusText(relationship), filter) ||
            TextMatchesFilter(BuildLookupAlias(relationship), filter) ||
            TextMatchesFilter(BuildLookupProjectionAlias(relationship), filter) ||
            TextMatchesFilter(BuildDisplayProjectionText(relationship), filter) ||
            TextMatchesFilter(BuildJoinText(relationship), filter) ||
            TextMatchesFilter(relationship.DisplayBusinessName, filter) ||
            TextMatchesFilter(relationship.DisplayBusinessDescription, filter))
        {
            return true;
        }

        return GetRelationshipColumns(relationship).Any(ColumnMatchesFilter);
    }

    private bool ColumnMatchesFilter(TableSchemaColumnInfo column)
    {
        var filter = FilterText;
        return TextMatchesFilter(column.ColumnName, filter) ||
            TextMatchesFilter(column.DataType, filter) ||
            TextMatchesFilter(column.KeyRole, filter) ||
            TextMatchesFilter(column.BusinessName, filter) ||
            TextMatchesFilter(column.BusinessDescription, filter) ||
            TextMatchesFilter(BuildBaseProjectionText(column), filter) ||
            TextMatchesFilter(ColumnKeyRoleLabel(column), filter);
    }

    private static bool TextMatchesFilter(string? value, string filter) =>
        !string.IsNullOrWhiteSpace(filter) &&
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static string MetadataDisplayValue(string? value, string emptyText) =>
        string.IsNullOrWhiteSpace(value) ? emptyText : value;

    private string EffectiveRelationshipColumnBusinessName(TableSchemaColumnInfo column, TableSchemaRelationshipInfo relationship) =>
        RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(column.BusinessName, column.ColumnName)
            : MetadataDisplayValue(column.BusinessName, "No business name");

    private string EffectiveRelationshipColumnBusinessDescription(TableSchemaColumnInfo column, TableSchemaRelationshipInfo relationship) =>
        RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(column.BusinessDescription, column.ColumnName)
            : MetadataDisplayValue(column.BusinessDescription, "No business description");

    private string EffectiveRelationshipDisplayBusinessName(TableSchemaRelationshipInfo relationship) =>
        RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(relationship.DisplayBusinessName, BuildLookupProjectionAlias(relationship))
            : MetadataDisplayValue(relationship.DisplayBusinessName, "No business name");

    private string EffectiveRelationshipDisplayBusinessDescription(TableSchemaRelationshipInfo relationship) =>
        RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(relationship.DisplayBusinessDescription, BuildLookupProjectionAlias(relationship))
            : MetadataDisplayValue(relationship.DisplayBusinessDescription, "No business description");

    private bool RelationshipOwnsProjectionColumns(TableSchemaRelationshipInfo relationship) =>
        CurrentSelectionPlan.RelationshipOwnsProjectionColumns(relationship);

    private bool RelationshipOwnsColumn(string columnName) =>
        CurrentSelectionPlan.ColumnHasLookupDisplayProjection(columnName);

    private bool RelationshipOwnsEditorProjectionColumns(TableSchemaRelationshipInfo relationship) =>
        CurrentSelectionPlan.RelationshipOwnsEditorProjectionColumns(relationship);

    private bool RelationshipOwnsEditorColumn(string columnName) =>
        CurrentSelectionPlan.ColumnBelongsToEditorRelationship(columnName);

    private bool RelationshipHasLookupDisplayProjection(TableSchemaRelationshipInfo relationship) =>
        CurrentSelectionPlan.RelationshipHasLookupDisplayProjection(relationship);

    private bool RelationshipHasLookupMetadataContext(TableSchemaRelationshipInfo relationship) =>
        relationship.Include &&
        CurrentSelectionPlan.RelationshipOwnsEditorProjectionColumns(relationship);

    private bool ColumnHasLookupDisplayProjection(string columnName) =>
        CurrentSelectionPlan.ColumnHasLookupDisplayProjection(columnName);

    private bool IsRelationshipColumn(TableSchemaColumnInfo column) =>
        Relationships
            .SelectMany(relationship => relationship.Columns)
            .Any(pair => string.Equals(pair.LocalColumnName, column.ColumnName, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> BuildJoinLines(TableSchemaRelationshipInfo relationship)
    {
        var alias = BuildLookupAlias(relationship);
        var predicates = relationship.Columns
            .Select(pair => $"{QuoteIdentifier(alias)}.{QuoteIdentifier(pair.ReferencedColumnName)} = {QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(pair.LocalColumnName)}");
        if (!string.IsNullOrWhiteSpace(relationship.LookupFilterColumnName) &&
            !string.IsNullOrWhiteSpace(relationship.LookupFilterValue))
        {
            predicates = predicates.Append($"{QuoteIdentifier(alias)}.{QuoteIdentifier(relationship.LookupFilterColumnName)} = {QuoteSqlLiteral(relationship.LookupFilterValue)}");
        }

        yield return $"{relationship.SelectedJoinType} {QualifiedName(SelectedDatabaseName, relationship.ReferencedSchemaName, relationship.ReferencedTableName)} AS {QuoteIdentifier(alias)} ON {string.Join(" AND ", predicates)}";
    }

    private string BuildChildJoinClause(TableSchemaChildRelationshipInfo relationship)
    {
        var childAlias = SanitizeAliasToken(relationship.ChildTableName);
        var predicates = relationship.Columns
            .Select(pair => $"{QuoteIdentifier(childAlias)}.{QuoteIdentifier(pair.ChildColumnName)} = {QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(pair.ParentColumnName)}");

        return $"JOIN {QualifiedName(SelectedDatabaseName, relationship.ChildSchemaName, relationship.ChildTableName)} AS {QuoteIdentifier(childAlias)} ON {string.Join(" AND ", predicates)}";
    }

    private string BuildRegistryJoinExpression(TableSchemaRelationshipInfo relationship)
    {
        var sourceAlias = SanitizeAliasToken(SelectedTableName);
        var targetAlias = SanitizeAliasToken(relationship.ReferencedTableName);
        var predicates = relationship.Columns
            .Select(pair => $"{QuoteIdentifier(sourceAlias)}.{QuoteIdentifier(pair.LocalColumnName)} = {QuoteIdentifier(targetAlias)}.{QuoteIdentifier(pair.ReferencedColumnName)}");

        if (!string.IsNullOrWhiteSpace(relationship.LookupFilterColumnName) &&
            !string.IsNullOrWhiteSpace(relationship.LookupFilterValue))
        {
            predicates = predicates.Append($"{QuoteIdentifier(targetAlias)}.{QuoteIdentifier(relationship.LookupFilterColumnName)} = {QuoteSqlLiteral(relationship.LookupFilterValue)}");
        }

        return string.Join(" AND ", predicates);
    }

    private string BuildLookupAlias(TableSchemaRelationshipInfo relationship)
    {
        var aliasesInUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(BaseAlias))
        {
            aliasesInUse.Add(SanitizeAliasToken(BaseAlias));
        }

        foreach (var candidate in Relationships.Where(RelationshipHasLookupDisplayProjection))
        {
            var alias = MakeUniqueAlias(BuildLookupAliasCandidate(candidate), aliasesInUse);

            if (ReferenceEquals(candidate, relationship))
            {
                return alias;
            }

            aliasesInUse.Add(alias);
        }

        return MakeUniqueAlias(BuildLookupAliasCandidate(relationship), aliasesInUse);
    }

    private string BuildLookupAliasCandidate(TableSchemaRelationshipInfo relationship)
    {
        var masterToken = SanitizeAliasToken(SelectedTableName);
        var tableToken = SanitizeAliasToken(relationship.ReferencedTableName);
        var localColumnToken = SanitizeAliasToken(string.Join("_", relationship.Columns.Select(column => column.LocalColumnName)));
        IEnumerable<string> aliasParts = OutputShape == OutputShapeCte
            ? new[] { masterToken, tableToken, localColumnToken }
            : new[] { tableToken, localColumnToken };

        aliasParts = aliasParts
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var alias = string.Join("_", aliasParts);

        return string.IsNullOrWhiteSpace(alias)
            ? "Lookup"
            : alias;
    }

    private static string MakeUniqueAlias(string preferredAlias, ISet<string> aliasesInUse)
    {
        var baseAlias = string.IsNullOrWhiteSpace(preferredAlias)
            ? "Lookup"
            : preferredAlias;
        var alias = baseAlias;
        var suffix = 2;

        while (aliasesInUse.Contains(alias))
        {
            alias = $"{baseAlias}_{suffix}";
            suffix++;
        }

        return alias;
    }

    private static string SanitizeAliasToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_'
                ? character
                : '_');
        }

        var alias = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(alias)
            ? string.Empty
            : alias;
    }

    private static string BuildLookupProjectionAlias(TableSchemaRelationshipInfo relationship)
    {
        var localColumn = relationship.Columns.FirstOrDefault()?.LocalColumnName ?? relationship.ReferencedTableName;
        return $"{localColumn}_FK_Description";
    }

    private static bool CanReviewLookupRelationship(TableSchemaRelationshipInfo relationship) =>
        relationship.Columns.Count == 1;

    private static bool CanSaveLookupRelationship(TableSchemaRelationshipInfo relationship) =>
        CanReviewLookupRelationship(relationship) &&
        !string.IsNullOrWhiteSpace(relationship.Columns[0].LocalColumnName) &&
        !string.IsNullOrWhiteSpace(relationship.Columns[0].ReferencedColumnName) &&
        !string.IsNullOrWhiteSpace(relationship.ReferencedSchemaName) &&
        !string.IsNullOrWhiteSpace(relationship.ReferencedTableName);

    private bool ShouldSaveLookupRelationship(TableSchemaRelationshipInfo relationship)
    {
        if (!CanSaveLookupRelationship(relationship))
        {
            return false;
        }

        return IsTemplateDiscoveredLookupRelationship(relationship) ||
            IsRegisteredLookupRelationship(relationship) ||
            HasDisplayColumnOverride(relationship);
    }

    private bool IsRegisteredLookupRelationship(TableSchemaRelationshipInfo relationship) =>
        SavedLookupRelationships.Any(savedRelationship => IsSameRelationship(relationship, savedRelationship));

    private static bool IsSameRelationship(
        TableSchemaRelationshipInfo relationship,
        DatabaseRelationshipDefinition savedRelationship)
    {
        if (!string.IsNullOrWhiteSpace(savedRelationship.SourceConstraintName) &&
            string.Equals(relationship.ForeignKeyName, savedRelationship.SourceConstraintName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(relationship.ReferencedSchemaName, savedRelationship.TargetSchemaName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(relationship.ReferencedTableName, savedRelationship.TargetTableName, StringComparison.OrdinalIgnoreCase) ||
            relationship.Columns.Count != savedRelationship.Columns.Count)
        {
            return false;
        }

        var relationshipColumns = relationship.Columns
            .Select(column => $"{BareRelationshipColumnName(column.LocalColumnName)}={BareRelationshipColumnName(column.ReferencedColumnName)}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var savedColumns = savedRelationship.Columns
            .Select(column => $"{BareRelationshipColumnName(column.SourceColumnName)}={BareRelationshipColumnName(column.TargetColumnName)}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

        return relationshipColumns.SequenceEqual(savedColumns, StringComparer.OrdinalIgnoreCase);
    }

    private static string BareRelationshipColumnName(string? columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return "";
        }

        var value = columnName.Trim();
        var dotIndex = value.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < value.Length - 1)
        {
            value = value[(dotIndex + 1)..];
        }

        return value.Trim().Trim('[', ']');
    }

    private static bool IsChildRelationship(DatabaseRelationshipDefinition relationship) =>
        relationship.DiscoverySource.Contains("Child", StringComparison.OrdinalIgnoreCase);

    private static bool IsTemplateDiscoveredLookupRelationship(TableSchemaRelationshipInfo relationship) =>
        !string.IsNullOrWhiteSpace(relationship.LookupFilterColumnName) ||
        relationship.ForeignKeyName.StartsWith("LOOKUP_", StringComparison.OrdinalIgnoreCase);

    private bool HasDisplayColumnOverride(TableSchemaRelationshipInfo relationship)
    {
        var key = BuildRelationshipRegistryKey(relationship);
        BootstrapLookupDisplayColumns.TryGetValue(key, out var bootstrapDisplayColumn);
        return !string.Equals(
            bootstrapDisplayColumn ?? string.Empty,
            relationship.DisplayColumnName ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string> GetLookupDisplayColumnOptions(TableSchemaRelationshipInfo relationship)
    {
        var targetKey = BuildLookupTargetKey(relationship);
        var columnNames = LookupDisplayColumnOptions.TryGetValue(targetKey, out var loadedColumnNames)
            ? loadedColumnNames
            : Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(relationship.DisplayColumnName) ||
            columnNames.Contains(relationship.DisplayColumnName, StringComparer.OrdinalIgnoreCase))
        {
            return columnNames;
        }

        return columnNames
            .Append(relationship.DisplayColumnName)
            .OrderBy(columnName => columnName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private DatabaseRelationshipDefinition BuildLookupRelationshipDefinition(int databaseId, TableSchemaRelationshipInfo relationship)
    {
        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = SelectedSchemaName,
            SourceTableName = SelectedTableName,
            TargetSchemaName = relationship.ReferencedSchemaName,
            TargetTableName = relationship.ReferencedTableName,
            JoinType = relationship.SelectedJoinType,
            JoinExpression = BuildRegistryJoinExpression(relationship),
            DiscoverySource = IsTemplateDiscoveredLookupRelationship(relationship) ? "SchemaLookup" : "Manual",
            SourceConstraintName = relationship.ForeignKeyName,
            IncludeLookupByDefault = relationship.Include && !string.IsNullOrWhiteSpace(relationship.DisplayColumnName),
            DisplayColumnName = relationship.DisplayColumnName,
            FilterColumnName = relationship.LookupFilterColumnName,
            FilterValue = relationship.LookupFilterValue,
            Columns = relationship.Columns
                .Select((column, index) => new DatabaseRelationshipColumnDefinition
                {
                    OrdinalPosition = index + 1,
                    SourceColumnName = column.LocalColumnName,
                    TargetColumnName = column.ReferencedColumnName
                })
                .ToList()
        };
    }

    private string BuildRelationshipRegistryKey(TableSchemaRelationshipInfo relationship)
    {
        if (relationship.Columns.Count == 0)
        {
            return "";
        }

        var column = relationship.Columns[0];
        return BuildRelationshipRegistryKey(
            SelectedSchemaName,
            SelectedTableName,
            column.LocalColumnName,
            relationship.ReferencedSchemaName,
            relationship.ReferencedTableName,
            column.ReferencedColumnName,
            relationship.LookupFilterColumnName,
            relationship.LookupFilterValue);
    }

    private static string BuildRelationshipRegistryKey(DatabaseRelationshipDefinition relationship)
    {
        var column = relationship.Columns.OrderBy(column => column.OrdinalPosition).FirstOrDefault();
        return BuildRelationshipRegistryKey(
            relationship.SourceSchemaName,
            relationship.SourceTableName,
            column?.SourceColumnName ?? "",
            relationship.TargetSchemaName,
            relationship.TargetTableName,
            column?.TargetColumnName ?? "",
            relationship.FilterColumnName,
            relationship.FilterValue);
    }

    private static string BuildRelationshipRegistryKey(
        string sourceSchemaName,
        string sourceTableName,
        string sourceColumnName,
        string lookupSchemaName,
        string lookupTableName,
        string lookupKeyColumnName,
        string? lookupFilterColumnName,
        string? lookupFilterValue) =>
        string.Join("|",
        [
            sourceSchemaName.Trim(),
            sourceTableName.Trim(),
            sourceColumnName.Trim(),
            lookupSchemaName.Trim(),
            lookupTableName.Trim(),
            lookupKeyColumnName.Trim(),
            lookupFilterColumnName?.Trim() ?? "",
            lookupFilterValue?.Trim() ?? ""
        ]);

    private static string BuildLookupFilterText(TableSchemaRelationshipInfo relationship) =>
        string.IsNullOrWhiteSpace(relationship.LookupFilterColumnName) || string.IsNullOrWhiteSpace(relationship.LookupFilterValue)
            ? "(none)"
            : $"{relationship.LookupFilterColumnName} = '{relationship.LookupFilterValue}'";

    private static string? BuildLookupValuesText(TableSchemaRelationshipInfo relationship) =>
        string.IsNullOrWhiteSpace(relationship.LookupFilterColumnName) || string.IsNullOrWhiteSpace(relationship.LookupFilterValue)
            ? null
            : $"[{relationship.LookupFilterColumnName}] = {relationship.LookupFilterValue}";

    private static string BuildLookupTargetText(TableSchemaRelationshipInfo relationship) =>
        $"{QualifiedName(relationship.ReferencedSchemaName, relationship.ReferencedTableName)}.{relationship.ReferencedColumns}";

    private static string BuildLookupTargetKey(TableSchemaRelationshipInfo relationship) =>
        $"{relationship.ReferencedSchemaName.Trim()}|{relationship.ReferencedTableName.Trim()}";

    private static string LookupRelationshipStatusText(TableSchemaRelationshipInfo relationship, bool saved, bool canSave, bool shouldSave)
    {
        if (!canSave)
        {
            return "Composite or incomplete";
        }

        if (!shouldSave)
        {
            return saved ? "Saved metadata default" : "FK metadata default";
        }

        return saved ? "Update override" : "Save override";
    }

    private string BuildSourceCteName()
    {
        var token = SanitizeAliasToken(SelectedTableName);
        return string.IsNullOrWhiteSpace(token)
            ? "Source"
            : $"{token}_Source";
    }

    private string BuildProjectionLine(string prefix, string projection, string outputColumnName, string businessName = "", string businessDescription = "", bool disableInheritance = false) =>
        $"{prefix}{projection}{BuildMetadataPlaceholder(prefix, projection, outputColumnName, businessName, businessDescription, disableInheritance)}";

    private static string ProjectionPrefix(bool first) =>
        first ? "      " : "    , ";

    private static string YesNo(bool value) =>
        value ? "Yes" : "No";

    private static string QualifiedName(params string?[] parts) =>
        string.Join(".", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => QuoteIdentifier(part!)));

    private static string QuoteIdentifier(string identifier) =>
        $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string QuoteSqlLiteral(string value)
    {
        var escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escapedValue}'";
    }

    private static int? GetMaxLength(string propertyName)
    {
        var property = typeof(SchemaObjectColumnDefinition)
            .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        var stringLength = property?.GetCustomAttribute<StringLengthAttribute>();
        if (stringLength is not null)
        {
            return stringLength.MaximumLength;
        }

        var maxLength = property?.GetCustomAttribute<MaxLengthAttribute>();
        return maxLength?.Length;
    }

    private RenderFragment HelpLabel(string label, string description) => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "bvg-field-label");
        builder.AddContent(2, label);
        builder.AddContent(3, HelpIcon(description));
        builder.CloseElement();
    };

    private RenderFragment HelpIcon(string description) => builder =>
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        builder.OpenComponent<RadzenIcon>(0);
        builder.AddAttribute(1, "Icon", "help_outline");
        builder.AddAttribute(2, "class", "bvg-help-icon");
        builder.AddAttribute(3, "MouseEnter", EventCallback.Factory.Create<ElementReference>(this, args => TooltipService.Open(args, description)));
        builder.CloseComponent();
    };

    private async Task OpenHelp(string id)
    {
        await DialogService.OpenAsync<HelpDialog>(
            "Schema Studio Help Console",
            new Dictionary<string, object?>
            {
                { "InitialSubjectId", id }
            },
            new DialogOptions
            {
                Width = "1200px",
                Height = "800px",
                Resizable = true,
                Draggable = true
            });
    }

    private static bool ToBool(object? value) =>
        value switch
        {
            bool boolValue => boolValue,
            string textValue => bool.TryParse(textValue, out var parsed) && parsed,
            _ => false
        };

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private void NotifyError(string summary)
    {
        NotificationService.Notify(NotificationSeverity.Error, summary, "", 5000);
    }

    private async Task CopySqlAsync()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedSql))
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedSql);
            NotificationService.Notify(NotificationSeverity.Success, "SQL copied.", "", 3000);
        }
    }

    private async Task CopyCreateTableSqlAsync()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedCreateTableSql))
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedCreateTableSql);
            NotificationService.Notify(NotificationSeverity.Success, "CREATE TABLE copied.", "", 3000);
        }
    }

    private async Task CopyMergeSqlAsync()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedMergeSql))
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedMergeSql);
            NotificationService.Notify(NotificationSeverity.Success, "Merge copied.", "", 3000);
        }
    }

    private async Task RunPageOperationAsync(Func<Task> operation, string failureMessage)
    {
        IsBusy = true;
        await InvokeAsync(StateHasChanged);
        await Task.Yield();

        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            StatusMessage = "";
            NotificationService.Notify(NotificationSeverity.Error, failureMessage, ex.Message, 6000);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
