using Radzen;
using SchemaStudioWebViewer.Components;
using SchemaStudio.AIHelpers;
using SchemaStudioWebViewer.Configuration;
using SchemaStudioWebViewer.Utils;

namespace SchemaStudioWebViewer
{
    [FileVersion("1.0")]
    [AIFileContext("Program.cs", "Bootstraps the SchemaStudioWebViewer web app, initializes configuration, registers services, and maps the Razor and MCP endpoints.")]
    [AIChange("1.0", "2026-04-13 02:37 PM CDT workflow header test: added the initial file header metadata, version marker, and visible compare marker for Program.cs.", AICommandStatus.Pending)]
    // 2026-04-13 02:37 PM CDT AI v1.0 workflow header test marker: added the initial file header and pending metadata for Program.cs review.
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
