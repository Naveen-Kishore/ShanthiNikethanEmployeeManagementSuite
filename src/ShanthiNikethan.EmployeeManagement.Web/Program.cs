using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using QuestPDF.Infrastructure;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Shared.Components;

// QuestPDF community licence (free for revenue < ₹8 Cr)
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ==================================================================
// Azure Key Vault — pulls secrets (currently just AzureAd:ClientSecret)
// into the same configuration tree everything else already reads from,
// so no other code needs to know whether a value came from Key Vault,
// appsettings.json, or User Secrets. Authenticates as the VM's Managed
// Identity — no credential of any kind is stored anywhere for this to
// work, Azure handles that behind the scenes.
//
// Only activates when "KeyVaultUri" is actually set in configuration.
// Your local dev machine has no such setting, so this block is skipped
// entirely there and User Secrets keeps working exactly as before —
// this is purely additive for the VM, nothing changes for local dev.
// ==================================================================
var keyVaultUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

// ==================================================================
// Module registry — load modules.json, discover implementations,
// build the filtered enabled list. Everything downstream reads this.
// ==================================================================
var modulesConfig = ModuleRegistry.LoadConfiguration(builder.Environment);
var discoveredModules = ModuleRegistry.DiscoverModules();
var moduleRegistry = new ModuleRegistry(modulesConfig, discoveredModules);
builder.Services.AddSingleton(moduleRegistry);

// ==================================================================
// Authentication — Microsoft Entra ID (OpenID Connect) as the primary
// path, plus a local-credential cookie scheme as an emergency fallback
// for exactly two pre-created accounts (Global Admin + Office Admin).
//
// AddMicrosoftIdentityWebApp registers its own cookie scheme ("Cookies")
// to hold the Entra session, but leaves the app's *default* authenticate
// scheme as "OpenIdConnect" — a redirect-only handler that never reads a
// cookie. That's invisible for normal Entra sign-in (the OIDC challenge
// flow itself sets the "Cookies" cookie), but it means a request
// authenticated only under "LocalAuth" was never actually recognized:
// the default scheme couldn't see the LocalAuth cookie, so every request
// after a successful local sign-in looked unauthenticated and immediately
// got challenged straight back to Microsoft's login page.
//
// The "SmartAuth" policy scheme below fixes this: it inspects each
// request for the local-auth cookie and forwards authentication to
// whichever scheme actually applies, while always challenging genuinely
// unauthenticated users through Entra ID by default, unchanged.
// ==================================================================
const string LocalAuthCookieName = "SNM.LocalAuth";

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .Services.AddAuthentication()
    .AddCookie("LocalAuth", options =>
    {
        options.LoginPath = "/signin";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = LocalAuthCookieName;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    })
    .AddPolicyScheme("SmartAuth", "Local or Entra ID", options =>
    {
        // Authenticate/SignIn/SignOut/Forbid all follow whichever cookie is
        // actually present on the request — this is what makes LocalAuth
        // sessions visible to the rest of the app.
        options.ForwardDefaultSelector = context =>
            context.Request.Cookies.ContainsKey(LocalAuthCookieName)
                ? "LocalAuth"
                : CookieAuthenticationDefaults.AuthenticationScheme;

        // Challenge (i.e. "you're not signed in, go log in") always goes
        // to Microsoft Entra ID by default — unauthenticated users should
        // still land on the normal Entra flow, not the local-fallback form.
        options.ForwardChallenge = OpenIdConnectDefaults.AuthenticationScheme;
    });

// AddMicrosoftIdentityWebApp's own "Cookies" scheme defaults AccessDeniedPath
// to Microsoft.Identity.Web.UI's built-in AccountController route — which
// 404s here, since that page needs MVC Razor Views and this project
// deliberately has none (same reason LocalAccountController hand-writes its
// HTML rather than using a .cshtml view). Redirect it to our own page instead,
// same pattern as everywhere else in this codebase.
builder.Services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    options.AccessDeniedPath = "/access-denied";
});

// PostConfigure runs after AddMicrosoftIdentityWebApp's own internal scheme
// setup, so this reliably wins and makes "SmartAuth" the scheme actually
// used to populate HttpContext.User on every request, regardless of what
// Microsoft.Identity.Web configured internally.
builder.Services.PostConfigure<AuthenticationOptions>(options =>
{
    options.DefaultScheme = "SmartAuth";
    options.DefaultAuthenticateScheme = "SmartAuth";
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
});

builder.Services.AddAuthorization(options =>
{
    var allowedOids = builder.Configuration
        .GetSection("Authorization:AllowedUserObjectIds").Get<string[]>() ?? Array.Empty<string>();

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
        {
            // Local-auth users are already tightly controlled — only the
            // two hand-created accounts exist, no self-service signup —
            // so authenticating via this scheme at all is sufficient.
            if (ctx.User.Identity?.AuthenticationType == "LocalAuth") return true;

            // Entra ID users: exact same allowlist check as always, unchanged.
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

// ==================================================================
// First-admin bootstrap — same pattern as Keycloak's KEYCLOAK_ADMIN or
// GitLab's GITLAB_ROOT_PASSWORD: a config value that creates exactly one
// admin account automatically on first startup against a fresh database,
// then becomes a permanent no-op. Solves the "brand new database has no
// account with permission to grant permission" deadlock without any
// manual SQL, on this environment or any future one.
//
// Safety property that matters: this only ever CREATES an account for the
// configured Object ID if none exists yet. It never touches an existing
// account's role — so a leftover config value can't silently re-promote
// someone who was deliberately demoted later. Leave this key set in
// config permanently; it's inert after the first successful run.
// ==================================================================
var bootstrapAdminObjectId = builder.Configuration["Authorization:BootstrapGlobalAdminObjectId"];
if (!string.IsNullOrWhiteSpace(bootstrapAdminObjectId))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var db = dbf.CreateDbContext();

        var alreadyExists = db.Set<UserAccount>().Any(u => u.EntraObjectId == bootstrapAdminObjectId);
        if (!alreadyExists)
        {
            var globalAdminGroup = db.Set<RoleGroup>().FirstOrDefault(g => g.Name == "Global Administrator");
            if (globalAdminGroup != null)
            {
                db.Set<UserAccount>().Add(new UserAccount
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Bootstrap Administrator",   // replaced with the real name automatically on first sign-in
                    EntraObjectId = bootstrapAdminObjectId,
                    RoleGroupId = globalAdminGroup.Id,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow,
                    CreatedByObjectId = "system",
                    CreatedByDisplayName = "Startup bootstrap (Authorization:BootstrapGlobalAdminObjectId)"
                });
                db.SaveChanges();
                logger.LogInformation("Bootstrap: created Global Administrator account for configured Object ID {ObjectId}.", bootstrapAdminObjectId);
            }
            else
            {
                logger.LogWarning("Authorization:BootstrapGlobalAdminObjectId is set, but no 'Global Administrator' role group exists yet — run the Admin Console foundation SQL script first.");
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Bootstrap admin check failed. App will continue.");
    }
}

app.Run();
