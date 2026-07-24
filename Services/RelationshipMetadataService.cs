using System;
using System.Collections.Generic;
using System.Linq;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Data;

namespace SchemaStudioWebViewer.Services;

/// <summary>
/// Pure, UI-independent construction, SQL/join-expression and parsing helpers for database
/// relationship definitions. Extracted from DatabaseRelationshipsPanel so the logic is
/// unit-testable and reusable (e.g. by Base View generation). Stateless — safe as a singleton.
/// </summary>
public sealed class RelationshipMetadataService
{
    public const string DiscoveryManual = "Manual";
    public const string DiscoveryManualChild = "ManualChild";
    public const string DiscoverySchemaLookup = "SchemaLookup";
    public const string DiscoverySchemaChild = "SchemaChild";
    // COLOOKUP is the shared code/lookup table; its lookups always project the DES1 description column.
    public const string ColookupTargetSchema = "dbo";
    public const string ColookupTargetTable = "COLOOKUP";
    public const string ColookupKeyColumn = "Id";
    public const string ColookupFilterColumn = "Name";
    public const string ColookupDisplayColumn = "DES1";

    public DatabaseRelationshipDefinition BuildFromSourceLookup(
        int databaseId,
        TableSchemaDetails details,
        TableSchemaRelationshipInfo sourceRelationship)
    {
        if (sourceRelationship.Columns.Count == 0)
        {
            throw new InvalidOperationException($"Relationship {sourceRelationship.ForeignKeyName} has no source-to-target column pairs.");
        }

        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = details.SchemaName,
            SourceTableName = details.TableName,
            TargetSchemaName = sourceRelationship.ReferencedSchemaName,
            TargetTableName = sourceRelationship.ReferencedTableName,
            JoinType = sourceRelationship.SelectedJoinType,
            DiscoverySource = DiscoverySchemaLookup,
            SourceConstraintName = Truncate(sourceRelationship.ForeignKeyName, 128),
            JoinExpression = BuildJoinExpression(details.TableName, sourceRelationship),
            IncludeLookupByDefault = sourceRelationship.Include && !string.IsNullOrWhiteSpace(sourceRelationship.DisplayColumnName),
            DisplayColumnName = Truncate(sourceRelationship.DisplayColumnName, 128),
            FilterColumnName = Truncate(sourceRelationship.LookupFilterColumnName, 128),
            FilterValue = Truncate(sourceRelationship.LookupFilterValue, 128),
            Columns = sourceRelationship.Columns
                .Select((column, index) => new DatabaseRelationshipColumnDefinition
                {
                    OrdinalPosition = index + 1,
                    SourceColumnName = Truncate(BareColumnName(column.LocalColumnName), 128) ?? "",
                    TargetColumnName = Truncate(BareColumnName(column.ReferencedColumnName), 128) ?? ""
                })
                .ToList()
        };
    }

    public DatabaseRelationshipDefinition BuildFromChildRelationship(
        int databaseId,
        TableSchemaDetails details,
        TableSchemaChildRelationshipInfo childRelationship)
    {
        if (childRelationship.Columns.Count == 0)
        {
            throw new InvalidOperationException($"Child relationship {childRelationship.ForeignKeyName} has no parent-to-child column pairs.");
        }

        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = details.SchemaName,
            SourceTableName = details.TableName,
            TargetSchemaName = childRelationship.ChildSchemaName,
            TargetTableName = childRelationship.ChildTableName,
            JoinType = "LEFT JOIN",
            DiscoverySource = DiscoverySchemaChild,
            SourceConstraintName = Truncate(childRelationship.ForeignKeyName, 128),
            JoinExpression = BuildChildJoinExpression(details.TableName, childRelationship),
            IncludeLookupByDefault = false,
            DisplayColumnName = null,
            FilterColumnName = null,
            FilterValue = null,
            Columns = childRelationship.Columns
                .Select((column, index) => new DatabaseRelationshipColumnDefinition
                {
                    OrdinalPosition = index + 1,
                    SourceColumnName = Truncate(BareColumnName(column.ParentColumnName), 128) ?? "",
                    TargetColumnName = Truncate(BareColumnName(column.ChildColumnName), 128) ?? ""
                })
                .ToList()
        };
    }

    public DatabaseRelationshipDefinition BuildColookupLookupRelationship(
        int databaseId,
        string sourceSchema,
        string sourceTable,
        string sourceColumn,
        string lookupName)
    {
        var columns = new List<DatabaseRelationshipColumnDefinition>
        {
            new()
            {
                OrdinalPosition = 1,
                SourceColumnName = Truncate(sourceColumn, 128) ?? "",
                TargetColumnName = ColookupKeyColumn
            }
        };

        return new DatabaseRelationshipDefinition
        {
            DatabaseId = databaseId,
            SourceSchemaName = sourceSchema,
            SourceTableName = sourceTable,
            TargetSchemaName = ColookupTargetSchema,
            TargetTableName = ColookupTargetTable,
            JoinType = "LEFT JOIN",
            DiscoverySource = DiscoverySchemaLookup,
            SourceConstraintName = Truncate($"LOOKUP_{ColookupTargetTable}_{sourceTable}_{sourceColumn}", 128),
            JoinExpression = BuildJoinExpression(sourceTable, ColookupTargetTable, columns),
            IncludeLookupByDefault = true,
            DisplayColumnName = ColookupDisplayColumn,
            FilterColumnName = ColookupFilterColumn,
            FilterValue = Truncate(lookupName, 128),
            Columns = columns
        };
    }

    public string BuildJoinExpression(string sourceTableName, TableSchemaRelationshipInfo relationship) =>
        string.Join(
            " AND ",
            relationship.Columns.Select(column =>
                $"{QuoteIdentifier(sourceTableName)}.{QuoteIdentifier(BareColumnName(column.LocalColumnName))} = {QuoteIdentifier(relationship.ReferencedTableName)}.{QuoteIdentifier(BareColumnName(column.ReferencedColumnName))}"));

    public string BuildChildJoinExpression(string sourceTableName, TableSchemaChildRelationshipInfo relationship) =>
        string.Join(
            " AND ",
            relationship.Columns.Select(column =>
                $"{QuoteIdentifier(sourceTableName)}.{QuoteIdentifier(BareColumnName(column.ParentColumnName))} = {QuoteIdentifier(relationship.ChildTableName)}.{QuoteIdentifier(BareColumnName(column.ChildColumnName))}"));

    public string BuildJoinExpression(
        string sourceTableName,
        string targetTableName,
        IEnumerable<DatabaseRelationshipColumnDefinition> columns) =>
        string.Join(
            " AND ",
            columns
                .OrderBy(column => column.OrdinalPosition)
                .Select(column =>
                    $"{QuoteIdentifier(sourceTableName)}.{QuoteIdentifier(BareColumnName(column.SourceColumnName))} = {QuoteIdentifier(targetTableName)}.{QuoteIdentifier(BareColumnName(column.TargetColumnName))}"));

    public void SynchronizeColumnPairsFromJoinExpression(DatabaseRelationshipDefinition relationship)
    {
        var parsedColumns = ParseJoinExpressionColumnPairs(
            relationship.JoinExpression,
            relationship.SourceTableName,
            relationship.TargetTableName);

        if (parsedColumns.Count > 0)
        {
            relationship.Columns = parsedColumns;
        }
    }

    public List<DatabaseRelationshipColumnDefinition> ParseJoinExpressionColumnPairs(
        string joinExpression,
        string sourceTableName,
        string targetTableName)
    {
        if (string.IsNullOrWhiteSpace(joinExpression) ||
            string.IsNullOrWhiteSpace(sourceTableName) ||
            string.IsNullOrWhiteSpace(targetTableName))
        {
            return new List<DatabaseRelationshipColumnDefinition>();
        }

        var columns = new List<DatabaseRelationshipColumnDefinition>();
        var comparisons = joinExpression.Split(
            [" AND ", "\r\nAND ", "\nAND ", "\tAND "],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var comparison in comparisons)
        {
            var equalsParts = comparison.Split('=', 2, StringSplitOptions.TrimEntries);
            if (equalsParts.Length != 2)
            {
                continue;
            }

            var left = ParseQualifiedColumnReference(equalsParts[0]);
            var right = ParseQualifiedColumnReference(equalsParts[1]);
            if (left is null || right is null)
            {
                continue;
            }

            var sourceOnLeft = IsTableToken(left.Value.TableToken, sourceTableName) &&
                IsTableToken(right.Value.TableToken, targetTableName);
            var sourceOnRight = IsTableToken(right.Value.TableToken, sourceTableName) &&
                IsTableToken(left.Value.TableToken, targetTableName);

            if (!sourceOnLeft && !sourceOnRight)
            {
                continue;
            }

            var sourceColumn = sourceOnLeft ? left.Value.ColumnName : right.Value.ColumnName;
            var targetColumn = sourceOnLeft ? right.Value.ColumnName : left.Value.ColumnName;
            columns.Add(new DatabaseRelationshipColumnDefinition
            {
                OrdinalPosition = columns.Count + 1,
                SourceColumnName = BareColumnName(sourceColumn),
                TargetColumnName = BareColumnName(targetColumn)
            });
        }

        return columns;
    }

    public void NormalizeRelationshipForSave(DatabaseRelationshipDefinition relationship)
    {
        if (IsChildRelationship(relationship))
        {
            relationship.DiscoverySource = string.IsNullOrWhiteSpace(relationship.DiscoverySource)
                ? DiscoveryManualChild
                : relationship.DiscoverySource;
            relationship.IncludeLookupByDefault = false;
            relationship.DisplayColumnName = null;
            relationship.FilterColumnName = null;
            relationship.FilterValue = null;
            return;
        }

        relationship.DiscoverySource = string.IsNullOrWhiteSpace(relationship.DiscoverySource)
            ? DiscoveryManual
            : relationship.DiscoverySource;
    }

    private static bool IsChildRelationship(DatabaseRelationshipDefinition? relationship) =>
        relationship is not null &&
        relationship.DiscoverySource.Contains("Child", StringComparison.OrdinalIgnoreCase);

    private static (string TableToken, string ColumnName)? ParseQualifiedColumnReference(string value)
    {
        var trimmed = value.Trim().TrimEnd(';').Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var dotIndex = trimmed.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex >= trimmed.Length - 1)
        {
            return null;
        }

        var tableToken = trimmed[..dotIndex].Trim().Trim('[', ']');
        var columnName = BareColumnName(trimmed[(dotIndex + 1)..]);
        if (string.IsNullOrWhiteSpace(tableToken) || string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        return (BareColumnName(tableToken), columnName);
    }

    private static bool IsTableToken(string tableToken, string tableName) =>
        string.Equals(BareColumnName(tableToken), BareColumnName(tableName), StringComparison.OrdinalIgnoreCase);

    private static string BareColumnName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            var openBracket = trimmed.LastIndexOf('[', trimmed.Length - 1);
            if (openBracket >= 0 && openBracket < trimmed.Length - 1)
            {
                return trimmed[(openBracket + 1)..^1];
            }
        }

        var dot = trimmed.LastIndexOf('.');
        return dot >= 0 && dot < trimmed.Length - 1
            ? trimmed[(dot + 1)..].Trim('[', ']')
            : trimmed.Trim('[', ']');
    }

    public string QuoteIdentifier(string value) =>
        $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength
            ? value
            : value[..maxLength];
}
