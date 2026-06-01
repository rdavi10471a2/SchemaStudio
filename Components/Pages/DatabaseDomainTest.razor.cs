using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using SchemaStudio.Data.Models;
using SchemaStudio.Data.Repositories;

namespace SchemaStudioWebViewer.Components.Pages;

public partial class DatabaseDomainTest : ComponentBase
{
    [Inject] private DatabaseRepository DatabaseRepository { get; set; } = default!;
    [Inject] private DatabaseDomainRepository DatabaseDomainRepository { get; set; } = default!;

    private IReadOnlyList<DatabaseDefinition> Databases { get; set; } = new List<DatabaseDefinition>();
    private IReadOnlyList<DatabaseDomainDefinition> Domains { get; set; } = new List<DatabaseDomainDefinition>();
    private int? SelectedDatabaseId { get; set; }
    private string StatusMessage { get; set; } = "Select a database and run the repository test.";
    private bool IsRunning { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Databases = await DatabaseRepository.GetAllAsync();

        if (Databases.Count > 0)
        {
            SelectedDatabaseId = Databases[0].DatabaseId;
        }
        else
        {
            StatusMessage = "No metadata databases are configured.";
        }
    }

    private async Task RunTestAsync()
    {
        if (SelectedDatabaseId is null)
        {
            StatusMessage = "Select a database first.";
            return;
        }

        IsRunning = true;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            Domains = await DatabaseDomainRepository.GetByDatabaseIdAsync(SelectedDatabaseId.Value);
            stopwatch.Stop();

            StatusMessage = $"OK - GetByDatabaseIdAsync returned {Domains.Count} domain(s) in {stopwatch.ElapsedMilliseconds} ms.";
        }
        catch (Exception ex)
        {
            Domains = new List<DatabaseDomainDefinition>();
            StatusMessage = $"FAILED - {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
