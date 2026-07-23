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
    public string ActiveWorkspaceTab { get; set; } = "tree";
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
    public List<TableSchemaColumnInfo> Columns { get; set; } = new();
    public List<TableSchemaRelationshipInfo> AllRelationships { get; set; } = new();
    public List<TableSchemaRelationshipInfo> Relationships { get; set; } = new();
    public List<TableSchemaChildRelationshipInfo> ChildRelationships { get; set; } = new();
    public List<DatabaseRelationshipDefinition> SavedLookupRelationships { get; set; } = new();
    public HashSet<string> SavedLookupRelationshipKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SelectedLookupRelationshipKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> BootstrapLookupDisplayColumns { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, IReadOnlyList<string>> LookupDisplayColumnOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
