using Radzen;
using SchemaStudioWebViewer.Components;
using SchemaStudioWebViewer.Configuration;
using SchemaStudioWebViewer.Utils;

namespace SchemaStudioWebViewer
{
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