namespace SchemaStudio.Data.Models;

/// <summary>
/// One control row from VVG_Silver.dbo.ReplicatedExcedeSources: a replicated Excede source
/// database and the CountryDB code it maps to. Drives the Base View Creator merge dropdowns and
/// the batch (looped) merge generation.
/// </summary>
public sealed class ReplicatedExcedeSource
{
    /// <summary>Country code written to the CountryDB column (e.g. "US", "CA").</summary>
    public string CountryDB { get; set; } = "";

    /// <summary>Physical replicated source database name (e.g. "ExcedeReplicationUS").</summary>
    public string ReplicatedDBName { get; set; } = "";
}
