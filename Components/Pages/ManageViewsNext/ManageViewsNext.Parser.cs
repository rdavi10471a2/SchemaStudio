using Radzen;
using SchemaStudio.AIHelpers;
using SchemaStudio.Data.Models;
using SchemaStudioWebViewer.Components.Dialogs;
using SchemaStudioWebViewer.Components.Pages.ManageViews;
using SchemaStudioWebViewer.WEBSemanticModel.Model;

namespace SchemaStudioWebViewer.Components.Pages.ManageViewsNext;

[AIChange("1.0", "2026-04-30 03:37 PM CDT added parser refresh, SQL, view-details, and where-used actions for the Manage Views Next prototype.", AICommandStatus.Pending)]
public partial class ManageViewsNext
{
    // 2026-04-30 03:37 PM CDT AI v1.0 manage-views-next marker: parser actions mirror ManageViews while keeping the prototype page separately testable.
    private async Task ParseAndBuildReviewAsync(IEnumerable<SchemaObjectColumnDefinition> existingColumns)
    {
        if (EditableObject == null)
        {
            CurrentParsedView = null;
            ReviewRows.Clear();
            return;
        }

        CurrentParsedView = ParserService.ParseView(
            EditableObject.SourceDatabaseName ?? string.Empty,
            EditableObject.SourceSchemaName,
            EditableObject.SourceObjectName);

        var parsedColumns = CurrentParsedView?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
        ReviewRows = BuildColumnReviewRows(parsedColumns, existingColumns).ToList();
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshCurrentViewAsync()
    {
        if (EditableObject == null)
        {
            return;
        }

        IsBusy = true;

        try
        {
            CurrentParsedView = ParserService.ReloadView(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName);

            var parsedColumns = CurrentParsedView?.Columns.ToViewColumnDtos() ?? new List<ViewColumnDto>();
            ReviewRows = BuildColumnReviewRows(parsedColumns, SavedColumns).ToList();
            NotificationService.Notify(NotificationSeverity.Success, "View refreshed", "The selected view SQL and parser review state were refreshed.", 2500);
            await InvokeAsync(StateHasChanged);
        }
        catch (Exception ex)
        {
            NotifyFailure("View refresh failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ShowSqlAsync()
    {
        if (EditableObject == null)
        {
            return;
        }

        try
        {
            ParserService.ClearCache();
            var sqlContent = ParserService.GetViewSql(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(sqlContent))
            {
                NotificationService.Notify(NotificationSeverity.Warning, "Parser SQL unavailable", "The parser service did not return SQL for the selected view.");
                return;
            }

            await DialogService.OpenAsync<ViewSQLDialog>(
                $"Parser SQL: {EditableObject.SourceObjectName}",
                new Dictionary<string, object?>
                {
                    { "SqlContent", sqlContent },
                    { "IsMock", false }
                },
                new DialogOptions
                {
                    Width = "1200px",
                    Height = "820px",
                    Resizable = true,
                    Draggable = true
                });
        }
        catch (Exception ex)
        {
            NotifyFailure("Parser SQL failed", ex);
        }
    }

    private async Task ShowParsedDependenciesAsync()
    {
        if (CurrentParsedView == null || EditableObject == null)
        {
            return;
        }

        await DialogService.OpenAsync<ResolvedDependencyChainDialog>(
            $"View Details: {EditableObject.SourceObjectName}",
            new Dictionary<string, object?>
            {
                { "ParsedView", CurrentParsedView },
                { "SqlContent", CurrentParsedView.SourceQuery ?? string.Empty }
            },
            new DialogOptions
            {
                Width = "min(1320px, calc(100vw - 96px))",
                Height = "calc(100vh - 120px)",
                Resizable = true,
                Draggable = false,
                CloseDialogOnOverlayClick = false
            });
    }

    private async Task ShowWhereUsedAsync()
    {
        if (EditableObject == null)
        {
            return;
        }

        try
        {
            var dependencies = await SqlObjectDependencyRepository.GetWhereUsedAsync(
                EditableObject.SourceDatabaseName ?? string.Empty,
                EditableObject.SourceSchemaName,
                EditableObject.SourceObjectName);

            if (dependencies.Count == 0)
            {
                NotificationService.Notify(NotificationSeverity.Info, "No where-used rows", "SQL Server dependency metadata does not list any objects using the selected object.", 3000);
                return;
            }

            await DialogService.OpenAsync<SqlObjectDependenciesDialog>(
                $"Where Used: {EditableObject.SourceObjectName}",
                new Dictionary<string, object?>
                {
                    { "Dependencies", dependencies }
                },
                new DialogOptions
                {
                    Width = "1100px",
                    Height = "620px",
                    Resizable = true,
                    Draggable = true
                });
        }
        catch (Exception ex)
        {
            NotifyFailure("Where-used lookup failed", ex);
        }
    }
}
