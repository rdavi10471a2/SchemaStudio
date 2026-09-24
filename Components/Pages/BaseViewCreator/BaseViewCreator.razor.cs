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
    [Inject] private ReplicatedExcedeSourceRepository ReplicatedExcedeSourceRepository { get; set; } = default!;
    [Inject] private SchemaObjectRepository SchemaObjectRepository { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private DialogService DialogService { get; set; } = default!;
    [Inject] private NotificationService NotificationService { get; set; } = default!;

    [Inject] private ReadOnlyViewDefinitionRepository ViewDefinitionRepository { get; set; } = default!;
    [Inject] private SqlScriptExecutionRepository ScriptExecutionRepository { get; set; } = default!;

    [Inject] private SchemaStudioWebViewer.WEBSemanticModel.Services.ViewParsingService ViewParser { get; set; } = default!;
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
    private string MergeDateFilterColumn { get => State.MergeDateFilterColumn; set => State.MergeDateFilterColumn = value; }
    private string MergeDateFilterRange { get => State.MergeDateFilterRange; set => State.MergeDateFilterRange = value; }
    private bool UseRowVersionColumn { get => State.UseRowVersionColumn; set => State.UseRowVersionColumn = value; }
    private string GeneratedBatchMergeSql { get => State.GeneratedBatchMergeSql; set => State.GeneratedBatchMergeSql = value; }
    private string BatchMergeUnavailableReason { get => State.BatchMergeUnavailableReason; set => State.BatchMergeUnavailableReason = value; }
    private string ReplicatedSourcesError { get => State.ReplicatedSourcesError; set => State.ReplicatedSourcesError = value; }
    private string ActiveWorkspaceTab { get => State.ActiveWorkspaceTab; set => State.ActiveWorkspaceTab = value; }
    private bool HideSingleMergeTab { get => State.HideSingleMergeTab; set => State.HideSingleMergeTab = value; }
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
    private List<ReplicatedExcedeSource> ReplicatedSources { get => State.ReplicatedSources; set => State.ReplicatedSources = value; }
    private List<TableSchemaColumnInfo> Columns { get => State.Columns; set => State.Columns = value; }
    private List<TableSchemaRelationshipInfo> AllRelationships { get => State.AllRelationships; set => State.AllRelationships = value; }
    private List<TableSchemaRelationshipInfo> Relationships { get => State.Relationships; set => State.Relationships = value; }
    private List<TableSchemaChildRelationshipInfo> ChildRelationships { get => State.ChildRelationships; set => State.ChildRelationships = value; }
    private List<DatabaseRelationshipDefinition> SavedLookupRelationships { get => State.SavedLookupRelationships; set => State.SavedLookupRelationships = value; }
    private HashSet<string> SavedLookupRelationshipKeys { get => State.SavedLookupRelationshipKeys; set => State.SavedLookupRelationshipKeys = value; }
    private HashSet<string> SelectedLookupRelationshipKeys { get => State.SelectedLookupRelationshipKeys; set => State.SelectedLookupRelationshipKeys = value; }
    private Dictionary<string, string> BootstrapLookupDisplayColumns { get => State.BootstrapLookupDisplayColumns; set => State.BootstrapLookupDisplayColumns = value; }
    private Dictionary<string, IReadOnlyList<string>> LookupDisplayColumnOptions { get => State.LookupDisplayColumnOptions; set => State.LookupDisplayColumnOptions = value; }
    private Dictionary<string, (string BusinessName, string BusinessDescription)> ImportedColumnComments { get => State.ImportedColumnComments; set => State.ImportedColumnComments = value; }
    private List<SurrogateKeyDefinition> SurrogateKeys { get => State.SurrogateKeys; set => State.SurrogateKeys = value; }
    private bool SurrogateKeyDialogOpen { get => State.SurrogateKeyDialogOpen; set => State.SurrogateKeyDialogOpen = value; }

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
            1); // every relationship renders one lookup display row (placeholder when no display chosen)

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

        await LoadReplicatedSourcesAsync();

        State.IsInitialized = true;
    }

    // Loads the VVG_Silver control table (ReplicatedExcedeSources) that backs the merge Country/Source
    // dropdowns and the batch loop. On failure the dropdowns stay empty and the error is surfaced in
    // the app (there is no hardcoded fallback).
    private async Task LoadReplicatedSourcesAsync()
    {
        try
        {
            ReplicatedSources = (await ReplicatedExcedeSourceRepository.GetAllAsync()).ToList();
            ReplicatedSourcesError = ReplicatedSources.Count == 0
                ? "No rows returned from VVG_Silver.dbo.ReplicatedExcedeSources."
                : "";
        }
        catch (Exception ex)
        {
            ReplicatedSources = new List<ReplicatedExcedeSource>();
            ReplicatedSourcesError = $"Failed to load VVG_Silver.dbo.ReplicatedExcedeSources: {ex.Message}";
        }
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

        // The Batch Merge tab has no Generate button, so build (or refresh) its SQL on arrival.
        // BuildBatchMergeSql sets an unavailable reason when inputs are incomplete, which the tab
        // surfaces as an alert instead of a blank preview.
        if (ActiveWorkspaceTab == "batch")
        {
            RegenerateBatchMerge();
        }

        QueueSqlHighlight();
    }

    // "Generate" on the Select Projection tab: (re)builds the SQL and jumps to the Generated SQL tab.
    private Task GenerateSqlAndShow()
    {
        RegenerateSql();
        return SetWorkspaceTab("sql");
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

    // Hides/shows the Single Merge tab entirely (the tab header is not rendered when hidden). When the
    // user hides it while it is the active tab, fall back to the Batch Merge tab so the workspace is
    // never left pointing at a tab with no header.
    private void OnHideSingleMergeTabChanged(ChangeEventArgs args)
    {
        HideSingleMergeTab = ToBool(args.Value);
        if (HideSingleMergeTab && ActiveWorkspaceTab == "merge")
        {
            ActiveWorkspaceTab = "batch";
        }
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
        if (!await ConfirmSourceTableNotAlreadyMappedAsync())
        {
            return;
        }

        await LoadTableDetailsAsync();
    }

    // Before loading, check whether the source table is already claimed by a base schema object
    // (only one base view can claim a table per database). If it is, ask the user to confirm they
    // want to load it anyway rather than silently remapping an already-mapped table. Returns true
    // when loading should proceed. A failed lookup does not block the user; it proceeds and surfaces
    // a warning so the check never becomes a hard gate on unrelated infrastructure errors.
    private async Task<bool> ConfirmSourceTableNotAlreadyMappedAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDatabaseName) ||
            string.IsNullOrWhiteSpace(SelectedTableName))
        {
            return true;
        }

        var selectedDatabase = Databases.FirstOrDefault(database =>
            string.Equals(database.DatabaseName, SelectedDatabaseName, StringComparison.OrdinalIgnoreCase));
        if (selectedDatabase is null || selectedDatabase.DatabaseId == 0)
        {
            return true;
        }

        SchemaObjectDefinition? existingBaseObject;
        try
        {
            existingBaseObject = await SchemaObjectRepository.GetBaseObjectBySourceTableAsync(
                selectedDatabase.DatabaseId,
                SelectedTableName);
        }
        catch (Exception ex)
        {
            NotificationService.Notify(
                NotificationSeverity.Warning,
                "Could not check existing base object mappings.",
                ex.Message,
                6000);
            return true;
        }

        if (existingBaseObject is null)
        {
            return true;
        }

        var existingBaseTableName = string.IsNullOrWhiteSpace(existingBaseObject.SourceTableName)
            ? "(none)"
            : existingBaseObject.SourceTableName;

        var confirmed = await DialogService.Confirm(
            $"Table '{SelectedTableName}' is already mapped to base object '{existingBaseObject.SourceObjectName}' (base table '{existingBaseTableName}'). Load it anyway?",
            "Table Already Mapped",
            new ConfirmOptions { OkButtonText = "Load Anyway", CancelButtonText = "Cancel" });

        return confirmed == true;
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
            SeedSurrogateKeyCandidates();
            CollapseJoinGroups();
            DisplayColumnTypeCache.Clear();
            MergeCountryDb = "";
            // Default the date filter to DateUpdate when the source table exposes it; otherwise leave it
            // blank (the UI flags the empty required field red until the user picks a date column).
            MergeDateFilterColumn = MergeDateFilterColumnOptions
                .FirstOrDefault(column => string.Equals(column, PreferredDateFilterColumn, StringComparison.OrdinalIgnoreCase))
                ?? "";
            MergeDateFilterRange = DateRangePriorMonth;
            MergeSourceDb = "";
            MergeSourceSchema = DefaultMergeSchemaName;
            MergeDestinationDb = DefaultMergeDestinationDatabaseName;
            MergeDestinationSchema = DefaultMergeSchemaName;
            MergeDestinationTable = string.IsNullOrWhiteSpace(TargetTableName) ? "" : TargetTableName;
            GeneratedMergeSql = "";
            MergeUnavailableReason = "";
            // Pull previously-authored comments from the deployed base view into the projection so
            // they show in Select Projection and flow into the regenerated SQL below.
            await ImportExistingViewCommentsAsync();
            RegenerateSql();
            await EnsureDisplayColumnTypesAsync();
            StatusMessage = BuildLoadedStatusMessage();
        }, $"Failed to load table details from [{SelectedDatabaseName}].[{SelectedSchemaName}].[{SelectedTableName}].");
    }

    private void ApplyRelationshipFilters()
    {
        // Show every FK/lookup relationship, including those with no display column chosen, so the user
        // can override the default from the Lookups section. No-display relationships still emit no join
        // or projection (the selection engine requires a chosen display column), and their projection-tree
        // row stays unchecked until a display column is picked.
        IEnumerable<TableSchemaRelationshipInfo> filteredRelationships = AllRelationships;

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

    // Clears the currently loaded source table / existing view and every artifact derived from it
    // (columns, relationships, imported comments, generated SQL, merge inputs) so the workspace
    // returns to the pre-load state. The selected source database and schema are kept as the working
    // context; once-per-circuit reference data (database list, replicated sources) is left intact.
    private void ClearLoadedView()
    {
        SelectedTableName = "";
        BaseAlias = "";
        ColumnFilter = "";

        Columns.Clear();
        AllRelationships.Clear();
        Relationships.Clear();
        ChildRelationships.Clear();
        BootstrapLookupDisplayColumns.Clear();
        ClearLookupRelationshipState();
        ImportedColumnComments.Clear();
        DisplayColumnTypeCache.Clear();
        CollapsedRelationshipGroups.Clear();
        SurrogateKeys.Clear();

        GeneratedSql = "";
        GeneratedCreateTableSql = "";
        GeneratedMergeSql = "";
        GeneratedBatchMergeSql = "";
        MergeUnavailableReason = "";
        BatchMergeUnavailableReason = "";

        // Reset the merge inputs to the same defaults a fresh table load applies.
        MergeCountryDb = "";
        MergeSourceDb = "";
        MergeSourceSchema = DefaultMergeSchemaName;
        MergeDestinationDb = DefaultMergeDestinationDatabaseName;
        MergeDestinationSchema = DefaultMergeSchemaName;
        MergeDestinationTable = "";
        MergeDateFilterColumn = "";
        MergeDateFilterRange = DateRangePriorMonth;

        // BaseAlias/SelectedTableName are now empty, so this clears TargetViewName and TargetTableName.
        UpdateTargetViewNameFromBaseAlias();

        RelationshipTelemetryMessage = "";
        SqlDirty = false;
        SqlRenderVersion++;

        StatusMessage = string.IsNullOrWhiteSpace(SelectedDatabaseName)
            ? "Cleared. Select a source database, type a table name, and click Load."
            : $"Cleared. Type a table name for [{SelectedDatabaseName}].[{SelectedSchemaName}] and click Load.";
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

    private void OnUseRowVersionColumnChanged(ChangeEventArgs args)
    {
        UseRowVersionColumn = ToBool(args.Value);
        RegenerateMergeAfterOptionChange();
    }

    // Shared by the batch-merge option row (Date Filter, Date Range, Use TS column) so changing any of
    // them re-renders the SQL: the single merge always, and the batch merge when it has been generated.
    private void RegenerateMergeAfterOptionChange()
    {
        if (CanRegenerateSql)
        {
            RegenerateSql();
        }
        else
        {
            MarkSqlDirty();
        }

        // The batch merge has no Generate button, so keep it live whenever its inputs are satisfied.
        if (CanRegenerateBatchMerge)
        {
            RegenerateBatchMerge();
        }
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

        // Output table defaults to the source table name (e.g. SVSLS -> SVSLS).
        TargetTableName = string.IsNullOrWhiteSpace(viewNameToken)
            ? ""
            : viewNameToken;
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

    // ---- Surrogate keys (Power BI single-column joins) ----

    private int IncludedSurrogateKeyCount =>
        SurrogateKeys.Count(key => key.Include);

    private IReadOnlyList<string> SurrogateKeyColumnOptions =>
        Columns.Select(column => column.ColumnName).ToList();

    // Rebuilds the auto-detected PK/FK candidates for the freshly-loaded table. Each is opt-in (unchecked)
    // with a pre-filled, editable expression. Manual keys are table-specific, so a fresh load starts clean.
    private void SeedSurrogateKeyCandidates()
    {
        SurrogateKeys.Clear();

        var pkColumns = Columns.Where(column => column.IsPrimaryKey).Select(column => column.ColumnName).ToList();
        if (pkColumns.Count > 0)
        {
            var pkName = $"{SanitizeAliasToken(SelectedTableName)}_{SanitizeAliasToken(string.Join("_", pkColumns))}{SurrogateKeySuffix}";
            SurrogateKeys.Add(new SurrogateKeyDefinition
            {
                Origin = SurrogateKeyOrigin.PrimaryKey,
                Key = "__PK__",
                OutputName = pkName,
                ColumnNames = pkColumns,
                Expression = BuildSurrogateKeyExpressionForColumns(pkColumns),
                Length = BuildSurrogateKeyLengthForColumns(pkColumns),
                BusinessDescription = $"Conformed PBI surrogate primary key for [{SelectedTableName}]. Single-column join key = [SourceDB] + ({string.Join(", ", pkColumns)}), collapsed to one column. Fact tables that reference this key expose an identically-named [{pkName}] surrogate and join to it directly in Power BI."
            });
        }

        // Role-playing detection: when this table has more than one FK to the SAME referenced dimension
        // key, naming both after the target key alone would collide, so those get the local role column
        // appended. Everything else is named after the referenced key so it matches the dimension's own
        // surrogate (self-evident join).
        var targetBaseCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in Relationships)
        {
            if (relationship.Columns.Count == 0)
            {
                continue;
            }

            var baseName = BuildSurrogateTargetBase(relationship);
            targetBaseCounts[baseName] = targetBaseCounts.TryGetValue(baseName, out var existing) ? existing + 1 : 1;
        }

        foreach (var relationship in Relationships)
        {
            var localColumns = relationship.Columns.Select(pair => pair.LocalColumnName).ToList();
            if (localColumns.Count == 0)
            {
                continue;
            }

            var targetBase = BuildSurrogateTargetBase(relationship);
            var dimensionSurrogate = $"{targetBase}{SurrogateKeySuffix}";
            var isRolePlaying = targetBaseCounts.TryGetValue(targetBase, out var baseCount) && baseCount > 1;
            var outputName = isRolePlaying
                ? $"{targetBase}_{SanitizeAliasToken(string.Join("_", localColumns))}{SurrogateKeySuffix}"
                : dimensionSurrogate;
            var roleNote = isRolePlaying ? $" (role column [{string.Join(", ", localColumns)}])" : "";

            SurrogateKeys.Add(new SurrogateKeyDefinition
            {
                Origin = SurrogateKeyOrigin.ForeignKey,
                Key = relationship.ForeignKeyName,
                OutputName = outputName,
                ColumnNames = localColumns,
                Expression = BuildSurrogateKeyExpressionForColumns(localColumns),
                Length = BuildSurrogateKeyLengthForColumns(localColumns),
                BusinessDescription = $"PBI surrogate foreign key. Joins the [{relationship.ReferencedTableName}] dimension on its [{dimensionSurrogate}] surrogate{roleNote}. Single-column join key = [SourceDB] + ({string.Join(", ", localColumns)})."
            });
        }
    }

    // {ReferencedTable}_{ReferencedKeyColumns}: the base of the referenced dimension's own surrogate name,
    // so a fact FK pointing at that key can be named identically for a self-evident Power BI join.
    private static string BuildSurrogateTargetBase(TableSchemaRelationshipInfo relationship)
    {
        var referencedColumns = relationship.Columns
            .Select(pair => pair.ReferencedColumnName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        if (referencedColumns.Count == 0)
        {
            referencedColumns = relationship.Columns.Select(pair => pair.LocalColumnName).ToList();
        }

        return $"{SanitizeAliasToken(relationship.ReferencedTableName)}_{SanitizeAliasToken(string.Join("_", referencedColumns))}";
    }

    private void OpenSurrogateKeyDialog() => SurrogateKeyDialogOpen = true;

    private void CloseSurrogateKeyDialog()
    {
        SurrogateKeyDialogOpen = false;
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

    private async Task OpenMergeWithExistingViewAsync()
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TargetSchemaName)
            || string.IsNullOrWhiteSpace(TargetViewName))
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Target view not set",
                "Set the target schema and view name before merging.", 3500);
            return;
        }

        // Base views are always created in VVGBI_Integrations, so the existing view is looked up
        // there regardless of the editable Target Database field.
        var existingViewDatabase = DefaultTargetDatabaseName;

        ViewDefinitionResult? existing;
        try
        {
            existing = await ViewDefinitionRepository.GetViewDefinitionAsync(
                existingViewDatabase, TargetSchemaName, TargetViewName);
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Error, "Lookup failed", ex.Message, 6000);
            return;
        }

        if (existing is null || string.IsNullOrWhiteSpace(existing.Definition))
        {
            NotificationService.Notify(NotificationSeverity.Info, "No existing base view",
                $"{TargetSchemaName}.{TargetViewName} was not found in {existingViewDatabase}. Nothing to merge against.", 4500);
            return;
        }

        // Normalize both sides for an apples-to-apples base comparison: trim leading/trailing
        // blank lines and canonicalize the CREATE [OR ALTER] VIEW header (the stored definition
        // comes back as "CREATE VIEW", the generated SQL as "CREATE OR ALTER VIEW"). Comment
        // blocks and column/metadata differences are left intact — those are the real diff.
        var existingSql = NormalizeViewSqlForCompare(existing.Definition);
        var generatedSql = NormalizeViewSqlForCompare(GeneratedSql);

        var result = await DialogService.OpenAsync<BaseViewMergeDialog>(
            $"Merge with existing view — {TargetSchemaName}.{TargetViewName}",
            new Dictionary<string, object?>
            {
                { "ExistingSql", existingSql },
                { "GeneratedSql", generatedSql },
                { "ExistingModifyDate", existing.ModifyDate }
            },
            new DialogOptions
            {
                Width = "92vw",
                Height = "90vh",
                Resizable = true,
                Draggable = false,
                CloseDialogOnOverlayClick = false
            });

        if (result is string merged && !string.IsNullOrWhiteSpace(merged))
        {
            GeneratedSql = merged;
            SqlDirty = false;
            SqlRenderVersion++;
            QueueSqlHighlight();
            await InvokeAsync(StateHasChanged);
        }
    }

    private static string NormalizeViewSqlForCompare(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var trimmed = sql.Trim();
        return System.Text.RegularExpressions.Regex.Replace(
            trimmed,
            @"^\s*CREATE\s+(OR\s+ALTER\s+)?VIEW\b",
            "CREATE OR ALTER VIEW",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
            // No display chosen yet: show the source column(s) and the lookup table so the user can
            // reason about which display column to pick, instead of an opaque "(no display column selected)".
            var sourceColumns = string.Join(
                ", ",
                relationship.Columns.Select(pair => $"{QuoteIdentifier(BaseAlias)}.{QuoteIdentifier(pair.LocalColumnName)}"));
            return $"{sourceColumns} -> {QuoteIdentifier(relationship.ReferencedTableName)}";
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

    // An explicit user edit (a non-empty value) always wins; otherwise fall back to the comment
    // imported from the existing deployed view, matched by OUTPUT ALIAS. See ImportExistingViewCommentsAsync.
    private (string BusinessName, string BusinessDescription) ApplyImportedComment(
        string outputAlias, string? userBusinessName, string? userBusinessDescription)
    {
        var businessName = userBusinessName ?? "";
        var businessDescription = userBusinessDescription ?? "";
        if (!string.IsNullOrWhiteSpace(outputAlias)
            && ImportedColumnComments.TryGetValue(outputAlias, out var imported))
        {
            if (string.IsNullOrWhiteSpace(businessName))
            {
                businessName = imported.BusinessName;
            }

            if (string.IsNullOrWhiteSpace(businessDescription))
            {
                businessDescription = imported.BusinessDescription;
            }
        }

        return (businessName, businessDescription);
    }

    private string EffectiveBaseColumnBusinessName(TableSchemaColumnInfo column) =>
        MetadataDisplayValue(
            ApplyImportedComment(column.ColumnName, column.BusinessName, column.BusinessDescription).BusinessName,
            "No business name");

    private string EffectiveBaseColumnBusinessDescription(TableSchemaColumnInfo column) =>
        MetadataDisplayValue(
            ApplyImportedComment(column.ColumnName, column.BusinessName, column.BusinessDescription).BusinessDescription,
            "No business description");

    private string EffectiveRelationshipColumnBusinessName(TableSchemaColumnInfo column, TableSchemaRelationshipInfo relationship)
    {
        var effective = ApplyImportedComment($"{column.ColumnName}_FK", column.BusinessName, column.BusinessDescription).BusinessName;
        return RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(effective, column.ColumnName)
            : MetadataDisplayValue(effective, "No business name");
    }

    private string EffectiveRelationshipColumnBusinessDescription(TableSchemaColumnInfo column, TableSchemaRelationshipInfo relationship)
    {
        var effective = ApplyImportedComment($"{column.ColumnName}_FK", column.BusinessName, column.BusinessDescription).BusinessDescription;
        return RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(effective, column.ColumnName)
            : MetadataDisplayValue(effective, "No business description");
    }

    private string EffectiveRelationshipDisplayBusinessName(TableSchemaRelationshipInfo relationship)
    {
        var alias = BuildLookupProjectionAlias(relationship);
        var effective = ApplyImportedComment(alias, relationship.DisplayBusinessName, relationship.DisplayBusinessDescription).BusinessName;
        return RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(effective, alias)
            : MetadataDisplayValue(effective, "No business name");
    }

    private string EffectiveRelationshipDisplayBusinessDescription(TableSchemaRelationshipInfo relationship)
    {
        var alias = BuildLookupProjectionAlias(relationship);
        var effective = ApplyImportedComment(alias, relationship.DisplayBusinessName, relationship.DisplayBusinessDescription).BusinessDescription;
        return RelationshipHasLookupMetadataContext(relationship)
            ? MetadataDisplayValue(effective, alias)
            : MetadataDisplayValue(effective, "No business description");
    }

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

    // Preferred default date filter column when the source table exposes it.
    private const string PreferredDateFilterColumn = "DateUpdate";

    // All merge settings must be provided before the merge can be generated.
    private bool CanRegenerateMerge =>
        !string.IsNullOrWhiteSpace(MergeCountryDb) &&
        !string.IsNullOrWhiteSpace(MergeDateFilterColumn) &&
        !string.IsNullOrWhiteSpace(MergeSourceDb) &&
        !string.IsNullOrWhiteSpace(MergeSourceSchema) &&
        !string.IsNullOrWhiteSpace(MergeDestinationDb) &&
        !string.IsNullOrWhiteSpace(MergeDestinationSchema) &&
        !string.IsNullOrWhiteSpace(MergeDestinationTable);

    // Batch merge loops the control table for the source/country, so those two dropdowns are not
    // required; every other precondition of the single merge still applies.
    private bool CanRegenerateBatchMerge =>
        Columns.Count > 0 &&
        !string.IsNullOrWhiteSpace(SelectedTableName) &&
        !string.IsNullOrWhiteSpace(BaseAlias) &&
        !string.IsNullOrWhiteSpace(MergeDateFilterColumn) &&
        !string.IsNullOrWhiteSpace(MergeSourceSchema) &&
        !string.IsNullOrWhiteSpace(MergeDestinationDb) &&
        !string.IsNullOrWhiteSpace(MergeDestinationSchema) &&
        !string.IsNullOrWhiteSpace(MergeDestinationTable);

    // Source columns whose resolved SQL type is a date/time type, offered in the merge Date Filter dropdown.
    private IReadOnlyList<string> MergeDateFilterColumnOptions =>
        Columns.Where(IsDateColumn).Select(column => column.ColumnName).ToList();

    private static bool IsDateColumn(TableSchemaColumnInfo column)
    {
        var resolved = UdtTypeResolver.ResolveByName(column.DataType);
        return resolved.StartsWith("date", StringComparison.OrdinalIgnoreCase) ||
               resolved.StartsWith("smalldatetime", StringComparison.OrdinalIgnoreCase);
    }

    // Resolves the CountryDB code for a source (replication) database from the loaded control table.
    private string ResolveCountryCode(string? database)
    {
        if (string.IsNullOrWhiteSpace(database))
        {
            return "";
        }

        var trimmed = database.Trim();
        return ReplicatedSources
            .FirstOrDefault(source => string.Equals(source.ReplicatedDBName, trimmed, StringComparison.OrdinalIgnoreCase))
            ?.CountryDB ?? "";
    }

    // Country DB and Source DB are linked both ways through the loaded control table: setting either
    // field fills the other. (Programmatic assignment here does not retrigger the partner field's
    // handler, so there is no feedback loop.)
    private void OnMergeSourceDbChanged()
    {
        var code = ResolveCountryCode(MergeSourceDb);
        if (!string.IsNullOrEmpty(code))
        {
            MergeCountryDb = code;
        }
    }

    private void OnMergeCountryDbChanged()
    {
        var match = ReplicatedSources
            .FirstOrDefault(source => string.Equals(source.CountryDB, MergeCountryDb, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !string.IsNullOrWhiteSpace(match.ReplicatedDBName))
        {
            MergeSourceDb = match.ReplicatedDBName;
        }
    }

    // Options for the linked Country DB / Source DB dropdowns, sourced from
    // VVG_Silver.dbo.ReplicatedExcedeSources (loaded once per circuit; empty if the load failed).
    private IReadOnlyList<string> CountryCodeOptions =>
        ReplicatedSources
            .Select(source => source.CountryDB)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<string> SourceDatabaseOptions =>
        ReplicatedSources
            .Select(source => source.ReplicatedDBName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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

    // The generated SQL is stored GO-free (a single batch); the USE [db]; GO wrapper here is applied only
    // for display and Copy so the script is SSMS-ready. Apply runs the stored core directly (see
    // ApplyGeneratedSqlAsync), connecting straight to the target database, so it never needs GO handling.
    private string GeneratedSqlDisplay => FrameWithUse(TargetDatabaseName, GeneratedSql, trailingGo: false);
    private string GeneratedCreateTableSqlDisplay => FrameWithUse(MergeDestinationDb, GeneratedCreateTableSql, trailingGo: false);
    private string GeneratedBatchMergeSqlDisplay => FrameWithUse(MergeDestinationDb, GeneratedBatchMergeSql, trailingGo: true);

    private static string FrameWithUse(string database, string coreSql, bool trailingGo)
    {
        if (string.IsNullOrWhiteSpace(coreSql))
        {
            return coreSql;
        }

        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(database))
        {
            builder.Append("USE ").Append(QuoteIdentifier(database.Trim())).Append(';').Append(Environment.NewLine);
            builder.Append("GO").Append(Environment.NewLine);
        }

        builder.Append(coreSql);
        if (trailingGo)
        {
            builder.Append(Environment.NewLine).Append("GO");
        }

        return builder.ToString();
    }

    private bool CanApplyViewSql =>
        !IsBusy && !SqlDirty && !string.IsNullOrWhiteSpace(GeneratedSql) && !string.IsNullOrWhiteSpace(TargetDatabaseName);

    private bool CanApplyCreateTableSql =>
        !IsBusy && !string.IsNullOrWhiteSpace(GeneratedCreateTableSql) && !string.IsNullOrWhiteSpace(MergeDestinationDb);

    private bool CanApplyBatchMergeSql =>
        !IsBusy && string.IsNullOrWhiteSpace(BatchMergeUnavailableReason) && !string.IsNullOrWhiteSpace(GeneratedBatchMergeSql) && !string.IsNullOrWhiteSpace(MergeDestinationDb);

    private async Task CopySqlAsync()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedSql))
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedSqlDisplay);
            NotificationService.Notify(NotificationSeverity.Success, "SQL copied.", "", 3000);
        }
    }

    private async Task CopyCreateTableSqlAsync()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedCreateTableSql))
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedCreateTableSqlDisplay);
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

    private async Task CopyBatchMergeSqlAsync()
    {
        if (!string.IsNullOrWhiteSpace(GeneratedBatchMergeSql))
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", GeneratedBatchMergeSqlDisplay);
            NotificationService.Notify(NotificationSeverity.Success, "Batch merge copied.", "", 3000);
        }
    }

    private Task ApplyViewSqlAsync() =>
        ApplyGeneratedSqlAsync($"view [{TargetSchemaName}].[{TargetViewName}]", TargetDatabaseName, GeneratedSql);

    private Task ApplyCreateTableSqlAsync() =>
        ApplyGeneratedSqlAsync("CREATE TABLE script", MergeDestinationDb, GeneratedCreateTableSql);

    private Task ApplyBatchMergeSqlAsync() =>
        ApplyGeneratedSqlAsync("batch merge procedure", MergeDestinationDb, GeneratedBatchMergeSql);

    // Confirms, then executes the GO-free core against the shared connection with its default database set
    // to the target (see SqlScriptExecutionRepository). Runs through RunPageOperationAsync for busy state
    // and error surfacing.
    private async Task ApplyGeneratedSqlAsync(string label, string database, string coreSql)
    {
        if (string.IsNullOrWhiteSpace(coreSql))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            NotifyError("Set the target database before applying.");
            return;
        }

        var confirmed = await DialogService.Confirm(
            $"Run the {label} against [{database}] on the shared connection? This writes to the live database.",
            "Apply to database",
            new ConfirmOptions { OkButtonText = "Apply", CancelButtonText = "Cancel" });
        if (confirmed != true)
        {
            return;
        }

        await RunPageOperationAsync(async () =>
        {
            await ScriptExecutionRepository.ExecuteAsync(database, coreSql);
            StatusMessage = $"Applied {label} to [{database}].";
            NotificationService.Notify(NotificationSeverity.Success, $"Applied to [{database}].", label, 4000);
        }, $"Failed to apply {label} to [{database}].");
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

    private static string NormalizeImportedComment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        return string.Equals(trimmed, "--Not Specified--", StringComparison.OrdinalIgnoreCase)
            ? ""
            : trimmed;
    }

    private async Task ImportExistingViewCommentsAsync()
    {
        // Fresh import: drop any comments carried over from a previously loaded view.
        ImportedColumnComments.Clear();

        if (Columns.Count == 0
            || string.IsNullOrWhiteSpace(TargetSchemaName)
            || string.IsNullOrWhiteSpace(TargetViewName))
        {
            return;
        }

        try
        {
            var existing = await ViewDefinitionRepository.GetViewDefinitionAsync(
                DefaultTargetDatabaseName, TargetSchemaName, TargetViewName);
            if (existing is null || string.IsNullOrWhiteSpace(existing.Definition))
            {
                return;
            }

            var definition = existing.Definition
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", "\r\n");

            var parsed = ViewParser.ParseSql(definition, DefaultTargetDatabaseName, TargetSchemaName, TargetViewName);
            var parsedColumns = parsed?.Columns;
            if (parsedColumns is null || parsedColumns.Count == 0)
            {
                return;
            }

            // Align selection with the deployed view before recording comments: a projection the view
            // does NOT contain (including a column commented out in the SQL, which ScriptDom drops from
            // the parsed projection list) must not reappear in the regenerated output.
            var viewOutputAliases = parsedColumns
                .Select(parsedColumn => parsedColumn.ColumnName)
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ApplyExistingViewSelection(viewOutputAliases);

            // Record every parsed column's business metadata keyed by its OUTPUT ALIAS
            // (base column name, {col}_FK, or the lookup display alias). This is re-applied as a
            // fallback in BuildProjectionSpecs and the projection-tree helpers on every regenerate,
            // so imported comments for FK-ref and lookup-display columns survive Include toggling.
            foreach (var parsedColumn in parsedColumns)
            {
                if (string.IsNullOrWhiteSpace(parsedColumn.ColumnName))
                {
                    continue;
                }

                var businessName = NormalizeImportedComment(parsedColumn.BusinessName);
                var businessDescription = NormalizeImportedComment(parsedColumn.BusinessDescription);
                if (string.IsNullOrWhiteSpace(businessName) && string.IsNullOrWhiteSpace(businessDescription))
                {
                    continue;
                }

                ImportedColumnComments[parsedColumn.ColumnName] = (businessName, businessDescription);
            }

            var importedColumns = ImportedColumnComments.Count;

            if (importedColumns > 0)
            {
                NotificationService.Notify(
                    NotificationSeverity.Info,
                    "Existing comments imported",
                    $"Imported business metadata for {importedColumns} column(s) from {TargetSchemaName}.{TargetViewName}.",
                    3500);
            }
        }
        catch (Exception ex)
        {
            NotificationService.Notify(NotificationSeverity.Warning, "Could not import existing comments", ex.Message, 4000);
        }
    }

    // Drives Include selection from the deployed view's live projection so loading an existing view
    // regenerates the columns the view actually has — a column the view omits (e.g. commented out in
    // the SQL) is deselected rather than reappearing because every source column defaults to Include.
    // Matching uses the SAME output aliases the generator emits, so selection and generation stay
    // consistent by construction: base column -> ColumnName, foreign-key value -> {local}_FK,
    // lookup display -> {local}_FK_Description (BuildLookupProjectionAlias).
    private void ApplyExistingViewSelection(IReadOnlySet<string> viewOutputAliases)
    {
        // A base source column is present in the view either as a plain projection ([col]) or as the
        // foreign-key value of an active lookup ([col]_FK); either keeps it selected.
        foreach (var column in Columns)
        {
            column.Include =
                viewOutputAliases.Contains(column.ColumnName) ||
                viewOutputAliases.Contains($"{column.ColumnName}_FK");
        }

        // A relationship contributes its lookup display iff that display alias is in the view. Mirror
        // the display-include toggle's coupling: relationship.Include follows IncludeDisplayColumn and
        // only holds when the relationship actually has a display column to project.
        foreach (var relationship in Relationships)
        {
            var displayInView =
                HasDisplayColumn(relationship) &&
                viewOutputAliases.Contains(BuildLookupProjectionAlias(relationship));

            relationship.IncludeDisplayColumn = displayInView;
            relationship.Include = displayInView;
        }
    }
}
