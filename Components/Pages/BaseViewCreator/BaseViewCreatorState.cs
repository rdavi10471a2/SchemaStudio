using System;
using System.Collections.Generic;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.Models;

namespace SchemaStudioWebViewer.Components.Pages.BaseViewCreator;

/// <summary>
/// Scoped state container that lets the Base View Creator page retain its work across navigation.
/// </summary>
public sealed class BaseViewCreatorState
{
    /// <summary>True once the page has run its one-time initial load; guards against resetting restored state.</summary>
    public bool IsInitialized { get; set; }

    public bool IgnoreSelfJoins { get; set; } = true;
    public string OutputShape { get; set; } = "Standard";
    public string StatusMessage { get; set; } = "";
    public string RelationshipTelemetryMessage { get; set; } = "";
    public string SelectedDatabaseName { get; set; } = "";
    public string SelectedSchemaName { get; set; } = "";
    public string SelectedTableName { get; set; } = "";
    public string BaseAlias { get; set; } = "";
    public string TargetDatabaseName { get; set; } = "VVGBI_Integrations";
    public string TargetSchemaName { get; set; } = "dbo";
    public string TargetViewName { get; set; } = "";
    public string ColumnFilter { get; set; } = "";
    public string GeneratedSql { get; set; } = "";
    public string GeneratedCreateTableSql { get; set; } = "";
    public string TargetTableName { get; set; } = "";
    public Dictionary<string, (string DataType, bool IsNullable)> DisplayColumnTypeCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string GeneratedMergeSql { get; set; } = "";
    public string MergeUnavailableReason { get; set; } = "";
    public string MergeCountryDb { get; set; } = "";
    public string MergeSourceDb { get; set; } = "";
    public string MergeSourceSchema { get; set; } = "dbo";
    public string MergeDestinationDb { get; set; } = "VVG_Silver";
    public string MergeDestinationSchema { get; set; } = "dbo";
    public string MergeDestinationTable { get; set; } = "";
    public string MergeDateFilterColumn { get; set; } = "";
    public string MergeDateFilterRange { get; set; } = "priorMonthStart"; // matches BaseViewCreator.DateRangePriorMonth
    /// <summary>When true (default) the MERGE requires the TS rowversion column and guards updates with src.TS &gt; tgt.TS. When false the TS column is not required and every matched row is updated.</summary>
    public bool UseRowVersionColumn { get; set; } = true;
    public string GeneratedBatchMergeSql { get; set; } = "";
    public string BatchMergeUnavailableReason { get; set; } = "";
    // Error surfaced when the VVG_Silver control table (ReplicatedExcedeSources) fails to load.
    public string ReplicatedSourcesError { get; set; } = "";
    public string ActiveWorkspaceTab { get; set; } = "tree";
    /// <summary>When true the Single Merge tab is hidden entirely (not rendered). On by default.</summary>
    public bool HideSingleMergeTab { get; set; } = true;
    public string ActiveSourceInfoTab { get; set; } = "options";
    public bool MergeControlsExpanded { get; set; } = true;
    public bool SourceGroupExpanded { get; set; } = true;
    public bool SourcePanelCollapsed { get; set; }
    public int SqlRenderVersion { get; set; }
    public int SourcePanelWidth { get; set; } = 400;
    public int ProjectionPaneHeight { get; set; } = 50;
    public bool SqlDirty { get; set; }
    public HashSet<string> CollapsedRelationshipGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<DatabaseDefinition> Databases { get; set; } = new();
    // Control rows from VVG_Silver.dbo.ReplicatedExcedeSources, loaded once per circuit.
    public List<ReplicatedExcedeSource> ReplicatedSources { get; set; } = new();
    public List<TableSchemaColumnInfo> Columns { get; set; } = new();
    public List<TableSchemaRelationshipInfo> AllRelationships { get; set; } = new();
    public List<TableSchemaRelationshipInfo> Relationships { get; set; } = new();
    public List<TableSchemaChildRelationshipInfo> ChildRelationships { get; set; } = new();
    public List<DatabaseRelationshipDefinition> SavedLookupRelationships { get; set; } = new();
    public HashSet<string> SavedLookupRelationshipKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedLookupRelationshipKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BootstrapLookupDisplayColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IReadOnlyList<string>> LookupDisplayColumnOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, (string BusinessName, string BusinessDescription)> ImportedColumnComments = new(StringComparer.OrdinalIgnoreCase);

    // Surrogate key (Power BI single-column join) generation. Auto-detected primary-key and
    // foreign-key candidates are seeded on table load; the operator opts each one in and can tweak
    // the expression, output type and length -- or add manual keys -- in the surrogate-key dialog.
    public List<SurrogateKeyDefinition> SurrogateKeys = new();
    public bool SurrogateKeyDialogOpen;
}

/// <summary>Origin of a surrogate key candidate.</summary>
public enum SurrogateKeyOrigin
{
    PrimaryKey,
    ForeignKey,
    Manual
}

/// <summary>
/// One surrogate key (Power BI single-column join) definition. PrimaryKey/ForeignKey rows are
/// auto-seeded from the loaded schema; Manual rows are added by the operator. Expression is
/// pre-filled from the source columns but freely editable; SqlType/Length drive the CREATE TABLE type.
/// </summary>
public sealed class SurrogateKeyDefinition
{
    public SurrogateKeyOrigin Origin;
    public string Key = "";
    public bool Include;
    public string OutputName = "";
    public List<string> ColumnNames = new();
    public string Expression = "";
    public string SqlType = "nvarchar";
    public int Length = 400;
    public string BusinessDescription = "";

    public bool IsManual => Origin == SurrogateKeyOrigin.Manual;
}
