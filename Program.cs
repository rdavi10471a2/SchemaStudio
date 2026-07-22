using Radzen;
using SchemaStudio.Data.Repositories;
using SchemaStudioWebViewer.Components;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Configuration;
using SchemaStudioWebViewer.Data;
using SchemaStudioWebViewer.McpTools;
using SchemaStudioWebViewer.Utils;
using SchemaStudioWebViewer.WEBSemanticModel.Services;
using System.Text.Json;

namespace SchemaStudioWebViewer
{
    [FileVersion("1.15")]
    [AIFileContext("Program.cs", "Bootstraps the SchemaStudioWebViewer web app, initializes configuration, registers services, and maps the Razor and MCP endpoints.")]
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            AppConfig.Initialize(builder.Configuration);

            // 1. Register Razor Components once
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            if (AppConfig.Current.Mcp.Enabled)
            {
                builder.Services
                    .AddMcpServer()
                    .WithHttpTransport(options =>
                    {
                        options.Stateless = true;
                    })
                    .WithTools<SchemaCatalogMcpTools>();
            }

            // 2. IMPORTANT: Remove the manual AddScoped<DialogService> lines.
            // builder.Services.AddRadzenComponents() handles all of these
            // registrations internally. Having both causes the UI to stop responding.
            builder.Services.AddRadzenComponents();
            builder.Services.AddScoped<AttributeService>();
            builder.Services.AddScoped(_ =>
                new SchemaMCPRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped<SchemaCatalogMcpTools>();
            builder.Services.AddSingleton<TableDisplayColumnPolicy>();
            builder.Services.AddScoped(sp =>
                new TableSchemaSmoRepository(
                    AppConfig.Current.ConnectionStrings.DefaultConnection,
                    sp.GetRequiredService<TableDisplayColumnPolicy>()));
            builder.Services.AddScoped<ViewParsingService>(_ =>
                new ViewParsingService(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new DatabaseRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new DatabaseRelationshipRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new DatabaseDomainRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new SourceViewRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new ReadOnlyViewDefinitionRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped<SchemaStudioWebViewer.Components.Pages.DomainObjectEditor.CteFieldParser>();
            builder.Services.AddScoped(_ =>
                new SchemaObjectRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new SchemaObjectColumnRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new SqlObjectDependencyRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));

            // Circuit-scoped state so the Base View Creator page retains work across navigation.
            builder.Services.AddScoped<SchemaStudioWebViewer.Components.Pages.BaseViewCreator.BaseViewCreatorState>();

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            // Note: UseAntiforgery must come before MapStaticAssets/MapRazorComponents
            app.UseAntiforgery();

            app.MapStaticAssets();

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            if (AppConfig.Current.Mcp.Enabled)
            {
                app.MapMcp(AppConfig.Current.Mcp.EffectiveRoute);
            }

            app.MapGet("/tool-lab/api/run/{toolName}", async (
                string toolName,
                int? databaseId,
                string? domain,
                int? schemaObjectId,
                int? schemaObjectColumnId,
                bool? cleanMetadataComments,
                bool? includeNext,
                int? top,
                string? search,
                SchemaCatalogMcpTools tools) =>
            {
                object response = toolName switch
                {
                    "schema_list_databases" => await tools.ListDatabasesAsync(includeNext, top, search),
                    "schema_list_domains" => databaseId is int selectedDatabaseId
                        ? await tools.ListDomainsAsync(selectedDatabaseId, includeNext, top, search)
                        : MissingToolLabParameter("databaseId"),
                    "schema_list_objects" => databaseId is int selectedDatabaseId
                        ? await tools.ListSchemaObjectsAsync(selectedDatabaseId, domain, includeNext, top, search)
                        : MissingToolLabParameter("databaseId"),
                    "schema_describe_object" => schemaObjectId is int selectedSchemaObjectId
                        ? await tools.DescribeSchemaObjectAsync(selectedSchemaObjectId, includeNext)
                        : MissingToolLabParameter("schemaObjectId"),
                    "schema_get_view_sql" => schemaObjectId is int selectedSchemaObjectId
                        ? await tools.GetViewSqlAsync(selectedSchemaObjectId, cleanMetadataComments ?? false, includeNext)
                        : MissingToolLabParameter("schemaObjectId"),
                    "schema_list_fields" => schemaObjectId is int selectedSchemaObjectId
                        ? await tools.ListFieldsAsync(selectedSchemaObjectId, includeNext, top, search)
                        : MissingToolLabParameter("schemaObjectId"),
                    "schema_describe_field" => schemaObjectColumnId is int selectedSchemaObjectColumnId
                        ? await tools.DescribeFieldAsync(selectedSchemaObjectColumnId, includeNext)
                        : MissingToolLabParameter("schemaObjectColumnId"),
                    _ => Results.NotFound(new
                    {
                        ok = false,
                        data = (object?)null,
                        error = new
                        {
                            code = "tool_not_found",
                            message = $"Tool '{toolName}' is not registered in the Tool Lab browser test endpoint."
                        }
                    })
                };

                return response is IResult result
                    ? result
                    : Results.Json(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        WriteIndented = true
                    });
            });

            app.Run();
        }

        private static object MissingToolLabParameter(string parameterName) => new
        {
            ok = false,
            data = (object?)null,
            error = new
            {
                code = "missing_input",
                message = $"Required query string parameter '{parameterName}' is missing."
            }
        };
    }
}
