using SchemaStudio.Data.Models;
using SchemaStudio.AIHelpers;

namespace SchemaStudioWebViewer.WEBSemanticModel.Model;

[FileVersion("1.0")]
[AIFileContext("WEBSemanticModel/Model/ExportMappers.cs", "Maps parser model objects into Schema Studio DTOs consumed by UI review and save workflows.", Responsibilities = "Preserves parser-owned column shape, lineage, semantic source, and metadata flags when converting ViewSourcedColumnDefinition to ViewColumnDto.", Nuances = "Keep DTO mapping in sync with parsed column fields used by Manage Views initial-save and review flows; missing fields can make Save All reason from stale or default metadata.", RelatedFiles = "ViewSourcedColumnDefinition; SchemaObjectDtos; Components/Pages/ManageViewsNext/ManageViewsNext.Parser.cs; Components/Pages/ManageViewsNext/ManageViewsNext.Columns.cs", LastReviewed = "2026-05-11")]
public static class ExportMappers
{
    public static List<SourceTableDto> ToSourceTableDtos(this IEnumerable<SourceTable>? sourceTables)
    {
        if (sourceTables == null)
        {
            return new List<SourceTableDto>();
        }

        return sourceTables
            .Select(source => new SourceTableDto
            {
                Kind = source.Kind.ToString(),
                Database = source.Database,
                Schema = source.Schema,
                Table = source.Table,
                Alias = source.Alias,
                ParentAlias = source.ParentAlias,
                JoinType = source.JoinType,
                ResolvedOrder = source.ResolvedOrder,
                JoinExpression = source.JoinExpression,
                IsBaseTable = source.Kind == SourceKind.NamedObject,
                JoinKeys = source.JoinKeys?
                    .Select(key => new JoinKeyDto
                    {
                        LocalColumn = key.LocalColumn,
                        RemoteExpression = key.RemoteExpression,
                        Cardinality = key.Cardinality.ToString()
                    })
                    .ToList() ?? new List<JoinKeyDto>()
            })
            .ToList();
    }

    public static List<ViewColumnDto> ToViewColumnDtos(this IEnumerable<ViewSourcedColumnDefinition>? columns)
    {
        if (columns == null)
        {
            return new List<ViewColumnDto>();
        }

        return columns
            .Select(column => new ViewColumnDto
            {
                ColumnId = column.ColumnId,
                TableId = column.TableId,
                OrdinalPosition = column.OrdinalPosition,
                ColumnName = column.ColumnName,
                Database = column.Database,
                Schema = column.Schema,
                Table = column.Table,
                ColumnKind = column.ColumnKind.ToString(),
                BaseDatabase = column.BaseDatabase,
                BaseSchema = column.BaseSchema,
                BaseTable = column.BaseTable,
                BaseColumn = column.BaseColumn,
                SemanticDatabase = column.SemanticDatabase,
                SemanticSchema = column.SemanticSchema,
                SemanticObject = column.SemanticObject,
                SemanticColumn = column.SemanticColumn,
                DisableInheritance = column.DisableInheritance,
                BusinessName = column.BusinessName,
                BusinessDescription = column.BusinessDescription,
                DeveloperNotes = column.DeveloperNotes,
                Comment = column.Comment,
                LastSynced = column.LastSynced,
                IsDirty = column.isDirty
            })
            .ToList();
    }
}
