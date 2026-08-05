using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using QuestPDF.Infrastructure;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Shared.Components;

// QuestPDF community licence (free for revenue < ₹8 Cr)
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ==================================================================
// Module registry — load modules.json, discover implementations,
// build the filtered enabled list. Everything downstream reads this.
// ==================================================================
var modulesConfig = ModuleRegistry.LoadConfiguration(builder.Environment);
var discoveredModules = ModuleRegistry.DiscoverModules();
var moduleRegistry = new ModuleRegistry(modulesConfig, discoveredModules);
builder.Services.AddSingleton(moduleRegistry);

// ==================================================================
// Authentication — Microsoft Entra ID (OpenID Connect)
// ==================================================================
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization(options =>
{
    var allowedOids = builder.Configuration
        .GetSection("Authorization:AllowedUserObjectIds").Get<string[]>() ?? Array.Empty<string>();

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
        {
            if (allowedOids.Length == 0) return true;
            var oid = ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                   ?? ctx.User.FindFirst("oid")?.Value;
            return oid != null && allowedOids.Contains(oid, StringComparer.OrdinalIgnoreCase);
        })
        .Build();
});

// ==================================================================
// Data access
// ==================================================================
builder.Services.AddDbContextFactory<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ==================================================================
// Core services
// ==================================================================
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<IAuditService, AuditService>();

// ==================================================================
// Module services — each enabled module registers what it needs
// ==================================================================
foreach (var module in moduleRegistry.EnabledModules)
{
    module.RegisterServices(builder.Services);
}

// ==================================================================
// Blazor Server + auth UI endpoints
// ==================================================================
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();

// ==================================================================
// General-purpose HttpClient (available for any future outbound calls)
// ==================================================================
builder.Services.AddHttpClient();

// ==================================================================
// Reverse-proxy awareness (IIS in front of Kestrel)
// ==================================================================
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(opt =>
{
    opt.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapControllers();

// ==================================================================
// Startup diagnostics — log which modules are enabled, record in DB
// ==================================================================
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var enabledNames = string.Join(", ", moduleRegistry.EnabledModules.Select(m => m.Name));
var disabledNames = string.Join(", ", moduleRegistry.DisabledModules);
logger.LogInformation("Module registry initialized. Enabled: [{Enabled}]. Filtered: [{Disabled}]. Deployment tier: {Tier}.",
    string.IsNullOrEmpty(enabledNames) ? "none" : enabledNames,
    string.IsNullOrEmpty(disabledNames) ? "none" : disabledNames,
    modulesConfig.Deployment.LicenseTier);

// Record module state to DB (best-effort — don't crash startup if DB isn't ready)
try
{
    using var scope = app.Services.CreateScope();
    var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbf.CreateDbContext();
    foreach (var (name, cfg) in modulesConfig.Modules)
    {
        var existing = db.ModuleState.Find(name);
        if (existing == null)
        {
            db.ModuleState.Add(new ModuleStateRecord
            {
                ModuleName = name,
                IsEnabled = moduleRegistry.IsEnabled(name),
                LicenseTier = cfg.LicenseTier
            });
        }
        else
        {
            existing.IsEnabled = moduleRegistry.IsEnabled(name);
            existing.LicenseTier = cfg.LicenseTier;
            existing.LastStartedAtUtc = DateTime.UtcNow;
        }
    }
    db.SaveChanges();
}
catch (Exception ex)
{
    logger.LogWarning(ex, "Could not record module state to database. App will continue.");
}

app.Run();
