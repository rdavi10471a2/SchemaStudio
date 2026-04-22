using Radzen;
using SchemaStudio.Data.Repositories;
using SchemaStudioWebViewer.Components;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Configuration;
using SchemaStudioWebViewer.Utils;
using SchemaStudioWebViewer.WEBSemanticModel.Services;

namespace SchemaStudioWebViewer
{
    [FileVersion("1.5")]
    [AIFileContext("Program.cs", "Bootstraps the SchemaStudioWebViewer web app, initializes configuration, registers services, and maps the Razor and MCP endpoints.")]
    [AIChange("1.5", "2026-04-22 06:20 PM CDT registered SQL Server dependency metadata repository for ParserLab where-used lookups.", AICommandStatus.Pending)]
    [AIChange("1.4", "2026-04-22 04:27 PM CDT registered schema object repositories from the new data layer so object metadata can be loaded and saved asynchronously.", AICommandStatus.Pending)]
    [AIChange("1.0", "2026-04-13 02:37 PM CDT workflow header test: added the initial file header metadata, version marker, and visible compare marker for Program.cs.", AICommandStatus.Pending)]
    [AIChange("1.1", "2026-04-15 03:00 PM CDT registered the WEBSemanticModel ViewParsingService so Home.razor can request SQL through the parser DLL.", AICommandStatus.Pending)]
    [AIChange("1.2", "2026-04-15 03:10 PM CDT removed the temporary parser DI registration after moving parser testing onto the dedicated ParserLab page.", AICommandStatus.Pending)]
    [AIChange("1.3", "2026-04-15 03:46 PM CDT restored scoped ViewParsingService registration so ParserLab can use DI for cache-backed parser actions.", AICommandStatus.Pending)]
    // 2026-04-13 02:37 PM CDT AI v1.0 workflow header test marker: added the initial file header and pending metadata for Program.cs review.
    // 2026-04-15 03:00 PM CDT AI v1.1 parser button marker: registered the parser DLL service for Home view SQL retrieval.
    // 2026-04-15 03:10 PM CDT AI v1.2 parser cleanup marker: ParserLab now instantiates its parser service directly, so Program no longer carries the temporary parser DI registration.
    // 2026-04-15 03:46 PM CDT AI v1.3 parser DI marker: ParserLab now resolves the parser through DI so cache-backed parse, reload, and clear-cache actions share one scoped service instance.
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
                builder.Services.AddMcpServer().WithHttpTransport(options =>
                {
                    options.Stateless = true;
                });
            }

            // 2. IMPORTANT: Remove the manual AddScoped<DialogService> lines.
            // builder.Services.AddRadzenComponents() handles all of these
            // registrations internally. Having both causes the UI to stop responding.
            builder.Services.AddRadzenComponents();
            builder.Services.AddScoped<AttributeService>();
            builder.Services.AddScoped<ViewParsingService>(_ =>
                new ViewParsingService(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new DatabaseRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new DatabaseDomainRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            // 2026-04-22 04:27 PM CDT AI v1.4 data-layer marker: register async schema object repositories for the imported object metadata layer.
            builder.Services.AddScoped(_ =>
                new SchemaObjectRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            builder.Services.AddScoped(_ =>
                new SchemaObjectColumnRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));
            // 2026-04-22 06:20 PM CDT AI v1.5 dependency-repository marker: ParserLab can ask SQL Server metadata which views use the selected object.
            builder.Services.AddScoped(_ =>
                new SqlObjectDependencyRepository(AppConfig.Current.ConnectionStrings.DefaultConnection));

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

            app.Run();
        }
    }
}
