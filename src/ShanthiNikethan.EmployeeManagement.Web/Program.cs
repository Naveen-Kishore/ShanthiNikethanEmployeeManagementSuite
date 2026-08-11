using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using QuestPDF.Infrastructure;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;
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

    // DEV-only bridge: the production ClientSecret already occupies the
    // name "AzureAd--ClientSecret" in this vault (Key Vault secret names
    // must be unique within a vault), so a dev-tenant secret being tested
    // here needs a different name - "AzureAd-Dev--ClientSecret", which
    // Key Vault's double-dash convention maps to the config key
    // "AzureAd-Dev:ClientSecret", not the standard "AzureAd:ClientSecret"
    // every other part of the app (including the new Graph client
    // registration) actually reads from. This copies it across, but only
    // outside Production - PROD's own secret is never touched or
    // shadowed by this.
    if (!builder.Environment.IsProduction())
    {
        var devSecret = builder.Configuration["AzureAd-Dev:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(devSecret))
            builder.Configuration["AzureAd:ClientSecret"] = devSecret;
    }
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

// Configurable so this can be adjusted without a code change - defaults
// to 30 minutes, a common baseline for business apps handling sensitive
// data (salary information, staff records) without being so aggressive
// it disrupts a normal workday. SlidingExpiration=true on both schemes
// below means this is a genuine idle timeout, not a flat session length -
// active use keeps renewing it, inactivity lets it expire.
var idleTimeoutMinutes = builder.Configuration.GetValue("Authentication:IdleTimeoutMinutes", 30);
var idleTimeout = TimeSpan.FromMinutes(idleTimeoutMinutes);

builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .Services.AddAuthentication()
    .AddCookie("LocalAuth", options =>
    {
        options.LoginPath = "/signin";
        options.AccessDeniedPath = "/access-denied";
        options.Cookie.Name = LocalAuthCookieName;
        options.ExpireTimeSpan = idleTimeout;
        options.SlidingExpiration = true;
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
    // Same idle-timeout reasoning as the LocalAuth scheme above - this is
    // the app's OWN cookie, entirely separate from whatever Entra's own
    // SSO session is doing upstream, and fully within the app's control
    // regardless of Entra ID licensing tier.
    options.ExpireTimeSpan = idleTimeout;
    options.SlidingExpiration = true;
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
    // No longer checks a static Object ID list here. That list required
    // manually editing this exact config file for every single new staff
    // member added through Add Staff - directly fighting the onboarding
    // flow this whole project built around automatic, database-driven
    // provisioning. The real authorization check now lives where it
    // belongs: whether a matching UserAccount record exists at all,
    // enforced dynamically in MainLayout (see ResolveAccountAndLogSignInAsync) -
    // which redirects to /access-denied for anyone genuinely unmatched,
    // with zero config file to maintain as staff are added or removed.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
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
builder.Services.AddScoped<IDashboardNotificationService, DashboardNotificationService>();
builder.Services.AddScoped<ISignInContextService, SignInContextService>();
builder.Services.AddScoped<IGraphProvisioningService, GraphProvisioningService>();

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
// Rate limiting - a second, independent layer of brute-force protection
// alongside the per-account lockout in UserAccountService.VerifyLocalLoginAsync.
// This one caps attempts per IP address, regardless of which username is
// being tried, which the account-level lockout alone can't do (someone
// spreading guesses across many different usernames would never trip any
// single account's threshold). Deliberately generous rather than strict -
// this is a small school's break-glass fallback login, not a public
// service, so the goal is slowing down automated guessing, not
// inconveniencing a real admin who mistypes a password twice.
// ==================================================================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("LocalLoginPolicy", opt =>
    {
        // Configurable specifically so the E2E test environment can raise
        // this via an environment variable override (Authentication__LocalLoginRateLimitPermits),
        // the same pattern already used for the E2E database connection
        // string - without touching this default at all, which is what
        // every real environment (dev, and eventually production) keeps
        // using. A growing library of E2E tests signing in for real,
        // repeatedly, in a short window is exactly the kind of traffic
        // this limiter is designed to slow down - correctly, for a real
        // user, but it has no actual attacker to catch in an isolated
        // test run against a throwaway database.
        opt.PermitLimit = builder.Configuration.GetValue("Authentication:LocalLoginRateLimitPermits", 10);
        opt.Window = TimeSpan.FromMinutes(5);
        opt.QueueLimit = 0; // reject immediately once the limit is hit, rather than queuing and delaying
    });
});

// ==================================================================
// General-purpose HttpClient (available for any future outbound calls)
// ==================================================================
builder.Services.AddHttpClient();

// ==================================================================
// Microsoft Graph credentials — TenantId/ClientId/ClientSecret already
// configured for sign-in are reused here (same app registration, now
// also holding the User.ReadWrite.All and GroupMember.ReadWrite.All
// Application permissions granted via admin consent). Deliberately NOT
// constructing the actual GraphServiceClient here as a DI singleton -
// that constructor throws on a missing/invalid secret, and doing that
// eagerly during DI resolution took down the entire Blazor circuit the
// moment anything merely navigated to a page that injects
// IGraphProvisioningService, rather than showing a clean error on that
// one page. GraphProvisioningService builds the client itself, lazily,
// wrapped in error handling - see that file for why.
// ==================================================================

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

// ==================================================================
// Security response headers - applied to every response, before
// anything else in the pipeline runs. CSP specifically checked against
// this app's real structure rather than a generic template: script-src
// 'self' works without 'unsafe-inline' because every <script> tag here
// is external (App.razor, and the sign-in page after moving its inline
// onclick/onload handlers into signin.js) - style-src still needs
// 'unsafe-inline' given how extensively inline style="..." attributes
// are used throughout the Razor components; removing that would mean
// rewriting a very large number of components into CSS classes, a
// separate, much larger undertaking. connect-src allows wss:/ws:
// specifically for Blazor Server's SignalR circuit - without it, the
// entire app would fail to render at all, not just look different.
// ==================================================================
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self' wss: ws:; " +
        "frame-ancestors 'none'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self';";
    await next();
});

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
app.UseRateLimiter();

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
// Guid.TryParse, not just a non-empty check - a leftover placeholder value
// (e.g. "PASTE_YOUR_PRODUCTION_TENANT_OBJECT_ID_HERE") is non-empty text
// too, and was previously being silently accepted as if it were a real
// Object ID - creating a "Bootstrap Administrator" account nobody could
// ever actually sign into, since no real Entra token has that literal
// string as its oid claim. Guid.TryParse means only a value that's
// actually shaped like an Object ID gets treated as one.
if (!string.IsNullOrWhiteSpace(bootstrapAdminObjectId) && Guid.TryParse(bootstrapAdminObjectId, out _))
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
