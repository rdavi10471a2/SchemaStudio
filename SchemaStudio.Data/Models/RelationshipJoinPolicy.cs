namespace SchemaStudio.Data.Models;

/// <summary>
/// Single source of truth for how a foreign-key lookup relationship's default SQL join type is
/// chosen. Shared by the Relationships Dialog (TableSchemaSmoRepository) and the RelationshipLoader
/// so both classify identically: a lookup gets an INNER JOIN only when every many-side (child/local)
/// FK column is non-nullable; otherwise a LEFT JOIN preserves rows whose foreign key is unset.
/// </summary>
public static class RelationshipJoinPolicy
{
    public const string InnerJoin = "INNER JOIN";
    public const string LeftJoin = "LEFT JOIN";

    /// <summary>
    /// Returns <see cref="InnerJoin"/> when there is at least one many-side FK column and every one
    /// is non-nullable; otherwise <see cref="LeftJoin"/>.
    /// </summary>
    /// <param name="manySideColumnNullability">
    /// One flag per many-side (child/local) FK column: <c>true</c> when that column is nullable.
    /// </param>
    public static string ForLookup(IEnumerable<bool> manySideColumnNullability)
    {
        var hasColumns = false;
        foreach (var isNullable in manySideColumnNullability)
        {
            hasColumns = true;
            if (isNullable)
            {
                return LeftJoin;
            }
        }

        return hasColumns ? InnerJoin : LeftJoin;
    }
}
