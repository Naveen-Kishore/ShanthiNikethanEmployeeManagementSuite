using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Modules;

namespace ShanthiNikethan.EmployeeManagement.Core.Services;

// ==================================================================
// Persisted entities
// ==================================================================

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string ActorDisplayName { get; set; } = string.Empty;
    [MaxLength(100)] public string ActorObjectId { get; set; } = string.Empty;
    [MaxLength(50)]  public string Module { get; set; } = string.Empty;
    [MaxLength(100)] public string EntityType { get; set; } = string.Empty;
    [MaxLength(50)]  public string? EntityId { get; set; }
    [MaxLength(50)]  public string Action { get; set; } = string.Empty;
    [MaxLength(100)] public string? FieldName { get; set; }
    [MaxLength(500)] public string? OldValue { get; set; }
    [MaxLength(500)] public string? NewValue { get; set; }
    [MaxLength(500)] public string? Context { get; set; }

    // ---- Sign-in / security context. Populated for Auth/Session entries;
    // RoleGroupAtTime is populated for every entry, everything else here
    // stays null for non-auth actions since none of it applies. ----
    [MaxLength(100)] public string? RequestId { get; set; }
    [MaxLength(100)] public string? RoleGroupAtTime { get; set; }
    public bool IsSuccess { get; set; } = true;
    [MaxLength(300)] public string? SignInError { get; set; }
    [MaxLength(50)]  public string? Provider { get; set; }
    [MaxLength(64)]  public string? IpAddress { get; set; }
    [MaxLength(150)] public string? GeoLocation { get; set; }
    [MaxLength(150)] public string? DeviceInfo { get; set; }
    [MaxLength(150)] public string? BrowserInfo { get; set; }
}

public class ModuleStateRecord
{
    [MaxLength(50)]  public string ModuleName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public LicenseTier LicenseTier { get; set; }
    public DateTime LastStartedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DashboardNotification
{
    public Guid Id { get; set; }
    [MaxLength(300)] public string Message { get; set; } = string.Empty;
    [MaxLength(300)] public string? LinkUrl { get; set; }
    [MaxLength(100)] public string TargetRoleGroupName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedByObjectId { get; set; } = string.Empty;
    [MaxLength(200)] public string CreatedByDisplayName { get; set; } = string.Empty;
    public DateTime? ExpiresAtUtc { get; set; }
}

public class DashboardNotificationDismissal
{
    public Guid NotificationId { get; set; }
    public Guid UserAccountId { get; set; }
    public DateTime DismissedAtUtc { get; set; } = DateTime.UtcNow;
}

// ==================================================================
// ICurrentUser — resolves the signed-in Entra user
// ==================================================================

public interface ICurrentUser
{
    string DisplayName { get; }
    string ObjectId { get; }
    bool IsAuthenticated { get; }

    /// <summary>The signed-in Entra ID user's UPN (e.g. name@school.onmicrosoft.com) — used to match an account an admin pre-created by UPN before this person's first login, since the admin can't know their opaque Object ID upfront.</summary>
    string? Upn { get; }

    /// <summary>True if this session authenticated via the local-credential fallback scheme rather than Entra ID.</summary>
    bool IsLocalAuth { get; }

    /// <summary>The signed-in UserAccount's own Id, once loaded — null until SetAccountContext has been called (e.g. by MainLayout on first render).</summary>
    Guid? UserAccountId { get; }

    /// <summary>The linked Staff profile's Id, for self-service filtering — null if this account isn't linked to a staff record (e.g. the two admin fallback accounts).</summary>
    Guid? LinkedStaffId { get; }

    string RoleGroupName { get; }

    /// <summary>Defaults to empty until loaded — an unloaded/unrecognized session has zero permissions, never all of them.</summary>
    bool HasPermission(string permissionKey);

    void SetAccountContext(Guid userAccountId, Guid? linkedStaffId, string roleGroupName, IEnumerable<string> permissions);
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private HashSet<string> _permissions = new();

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public bool IsLocalAuth => _accessor.HttpContext?.User.Identity?.AuthenticationType == "LocalAuth";

    public string DisplayName =>
        _accessor.HttpContext?.User.FindFirst("name")?.Value
        ?? _accessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value
        ?? _accessor.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? "unknown";

    public string ObjectId =>
        _accessor.HttpContext?.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? _accessor.HttpContext?.User.FindFirst("oid")?.Value
        ?? string.Empty;

    public string? Upn =>
        _accessor.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? _accessor.HttpContext?.User.FindFirst(ClaimTypes.Upn)?.Value;

    public Guid? UserAccountId { get; private set; }
    public Guid? LinkedStaffId { get; private set; }
    public string RoleGroupName { get; private set; } = string.Empty;

    public bool HasPermission(string permissionKey) => _permissions.Contains(permissionKey);

    public void SetAccountContext(Guid userAccountId, Guid? linkedStaffId, string roleGroupName, IEnumerable<string> permissions)
    {
        UserAccountId = userAccountId;
        LinkedStaffId = linkedStaffId;
        RoleGroupName = roleGroupName;
        _permissions = permissions.ToHashSet();
    }
}

// ==================================================================
// ISignInContextService — captures IP/device/browser/geo for sign-in
// audit entries. Everything here is best-effort: a slow or unreachable
// geo-IP lookup, or an unparseable User-Agent, must never delay or break
// an actual sign-in - this enriches the audit trail, it doesn't gate it.
// ==================================================================

public class SignInContext
{
    public string? RequestId { get; set; }
    public string? IpAddress { get; set; }
    public string? GeoLocation { get; set; }
    public string? DeviceInfo { get; set; }
    public string? BrowserInfo { get; set; }
}

public interface ISignInContextService
{
    Task<SignInContext> CaptureAsync(HttpContext? httpContext, CancellationToken ct = default);
}

public class SignInContextService : ISignInContextService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SignInContextService> _logger;

    public SignInContextService(IHttpClientFactory httpClientFactory, ILogger<SignInContextService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SignInContext> CaptureAsync(HttpContext? httpContext, CancellationToken ct = default)
    {
        var result = new SignInContext();
        if (httpContext == null) return result;

        result.RequestId = httpContext.TraceIdentifier;

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        result.IpAddress = ip;

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        (result.BrowserInfo, result.DeviceInfo) = ParseUserAgent(userAgent);

        result.GeoLocation = await LookupGeoLocationAsync(ip, ct);
        return result;
    }

    // Loopback/private addresses (localhost during dev, or a VM's own
    // internal IP if something's misconfigured upstream) will never
    // resolve to a real location - skip the network call entirely rather
    // than waiting on a lookup that can't succeed.
    private static bool IsLocalOrPrivate(string ip) =>
        ip is "::1" or "127.0.0.1" || ip.StartsWith("10.") || ip.StartsWith("192.168.") ||
        (ip.StartsWith("172.") && int.TryParse(ip.Split('.').ElementAtOrDefault(1), out var second) && second is >= 16 and <= 31);

    private async Task<string?> LookupGeoLocationAsync(string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        if (IsLocalOrPrivate(ip)) return "Local/internal network";

        try
        {
            // ip-api.com's free endpoint needs no signup/API key - reasonable
            // for a single school's realistic sign-in volume. Plain HTTP only
            // on the free tier; the only data sent is the IP address itself,
            // which is not sensitive information on its own. A short, hard
            // timeout is what makes this genuinely non-blocking: if the
            // service is slow or unreachable, sign-in proceeds immediately
            // with GeoLocation left as "Unknown" rather than waiting.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            var client = _httpClientFactory.CreateClient();
            var response = await client.GetFromJsonAsync<GeoIpResponse>(
                $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,city,countryCode",
                timeoutCts.Token);

            if (response?.status == "success")
                return string.IsNullOrEmpty(response.city) ? response.countryCode : $"{response.city}, {response.countryCode}";
            return "Unknown";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Geo-IP lookup failed for {Ip} - non-critical, continuing without it.", ip);
            return "Unknown";
        }
    }

    private static (string? Browser, string? Device) ParseUserAgent(string ua)
    {
        // Deliberately simple string-matching rather than a dedicated
        // UAParser NuGet package - covers realistic browser/device
        // diversity for a school's staff without adding a new dependency
        // to keep updated. Order matters: Edge and Chrome both contain
        // "Chrome/" in their UA string, so Edge must be checked first.
        if (string.IsNullOrWhiteSpace(ua)) return (null, null);

        string? browser =
            ua.Contains("Edg/") ? "Edge" :
            ua.Contains("OPR/") ? "Opera" :
            ua.Contains("Chrome/") ? "Chrome" :
            ua.Contains("Firefox/") ? "Firefox" :
            ua.Contains("Safari/") ? "Safari" :
            null;

        string? device =
            ua.Contains("iPhone") ? "iPhone" :
            ua.Contains("iPad") ? "iPad" :
            ua.Contains("Android") ? (ua.Contains("Mobile") ? "Android phone" : "Android tablet") :
            ua.Contains("Windows") ? "Windows PC" :
            ua.Contains("Macintosh") ? "Mac" :
            ua.Contains("Linux") ? "Linux PC" :
            null;

        return (browser ?? "Unknown browser", device ?? "Unknown device");
    }

    private class GeoIpResponse
    {
        public string? status { get; set; }
        public string? city { get; set; }
        public string? countryCode { get; set; }
    }
}

// ==================================================================
// IAuditService — one call site for every mutation to record itself
// ==================================================================

/// <summary>
/// Every filter is optional and additive (AND between categories, OR within
/// a category's own list) - an empty list for a given category means "don't
/// filter on this", not "match nothing". This mirrors how Purview's audit
/// log filtering behaves: pick as many or as few filter groups as you want,
/// each with one or more checked values.
/// </summary>
public class AuditLogSearchFilter
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public List<string> Modules { get; set; } = new();
    public List<string> EntityTypes { get; set; } = new();
    public List<string> Actions { get; set; } = new();

    /// <summary>Matches against ActorDisplayName (contains, case-insensitive) - free text rather than a checkbox list, since the set of people who've ever acted in the system can grow unbounded.</summary>
    public string? ActorSearch { get; set; }

    /// <summary>Free-text search across EntityId, OldValue, NewValue, and Context - the fields where "what actually happened" specifics live.</summary>
    public string? Keyword { get; set; }

    public string SortBy { get; set; } = "OccurredAtUtc";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public interface IAuditService
{
    /// <summary>
    /// actorDisplayNameOverride/actorObjectIdOverride: normally omitted -
    /// the actor is read from the injected ICurrentUser. The one case that
    /// needs these is logging a sign-in event itself: at that exact moment,
    /// HttpContext.User still reflects the PRE-signin state (SignInAsync
    /// doesn't retroactively update it mid-request), so ICurrentUser would
    /// report the wrong actor - these overrides let the caller supply the
    /// identity directly instead.
    ///
    /// roleGroupAtTimeOverride: same idea - normally read from ICurrentUser
    /// automatically, but a local sign-in's success log fires before
    /// SetAccountContext has ever run for that session, so there's nothing
    /// ambient to read yet.
    ///
    /// requestId/ipAddress/geoLocation/deviceInfo/browserInfo: populated by
    /// ISignInContextService.CaptureAsync at the sign-in call sites; left
    /// null for ordinary mutation logging where none of this applies.
    /// </summary>
    Task LogAsync(string module, string entityType, string? entityId, string action,
                  string? field = null, string? oldValue = null, string? newValue = null,
                  string? context = null, string? actorDisplayNameOverride = null,
                  string? actorObjectIdOverride = null, string? roleGroupAtTimeOverride = null,
                  string? requestId = null, string? ipAddress = null, string? geoLocation = null,
                  string? deviceInfo = null, string? browserInfo = null, bool isSuccess = true,
                  string? signInError = null, string? provider = null, CancellationToken ct = default);

    Task<List<AuditLogEntry>> GetRecentAsync(int count = 10, CancellationToken ct = default);

    /// <summary>Filtered, sorted, paged search - the query behind the Audit Log module's UI.</summary>
    Task<(List<AuditLogEntry> Items, int TotalCount)> SearchAsync(AuditLogSearchFilter filter, CancellationToken ct = default);

    /// <summary>Distinct Module values actually present in the log, for populating that filter group's checkbox list - reflects what's really there rather than a hardcoded guess that could drift from reality.</summary>
    Task<List<string>> GetDistinctModulesAsync(CancellationToken ct = default);
    Task<List<string>> GetDistinctEntityTypesAsync(CancellationToken ct = default);
    Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default);
}

public class AuditService : IAuditService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;

    public AuditService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user)
    {
        _dbf = dbf;
        _user = user;
    }

    public async Task LogAsync(string module, string entityType, string? entityId, string action,
                               string? field = null, string? oldValue = null, string? newValue = null,
                               string? context = null, string? actorDisplayNameOverride = null,
                               string? actorObjectIdOverride = null, string? roleGroupAtTimeOverride = null,
                               string? requestId = null, string? ipAddress = null, string? geoLocation = null,
                               string? deviceInfo = null, string? browserInfo = null, bool isSuccess = true,
                               string? signInError = null, string? provider = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        db.AuditLog.Add(new AuditLogEntry
        {
            OccurredAtUtc = DateTime.UtcNow,
            ActorDisplayName = actorDisplayNameOverride ?? _user.DisplayName,
            ActorObjectId = actorObjectIdOverride ?? _user.ObjectId,
            Module = module,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            FieldName = field,
            OldValue = Truncate(oldValue, 500),
            NewValue = Truncate(newValue, 500),
            Context = Truncate(context, 500),
            RoleGroupAtTime = roleGroupAtTimeOverride ?? _user.RoleGroupName,
            RequestId = requestId,
            IpAddress = ipAddress,
            GeoLocation = geoLocation,
            DeviceInfo = deviceInfo,
            BrowserInfo = browserInfo,
            IsSuccess = isSuccess,
            SignInError = signInError,
            Provider = provider
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? Truncate(string? s, int max) =>
        s == null ? null : s.Length <= max ? s : s[..max];

    public async Task<List<AuditLogEntry>> GetRecentAsync(int count = 10, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.AuditLog
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<(List<AuditLogEntry> Items, int TotalCount)> SearchAsync(AuditLogSearchFilter filter, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var query = db.AuditLog.AsNoTracking().AsQueryable();

        if (filter.FromUtc.HasValue)
            query = query.Where(a => a.OccurredAtUtc >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)
            query = query.Where(a => a.OccurredAtUtc <= filter.ToUtc.Value);

        if (filter.Modules.Count > 0)
            query = query.Where(a => filter.Modules.Contains(a.Module));
        if (filter.EntityTypes.Count > 0)
            query = query.Where(a => filter.EntityTypes.Contains(a.EntityType));
        if (filter.Actions.Count > 0)
            query = query.Where(a => filter.Actions.Contains(a.Action));

        if (!string.IsNullOrWhiteSpace(filter.ActorSearch))
            query = query.Where(a => a.ActorDisplayName.Contains(filter.ActorSearch));

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword;
            query = query.Where(a =>
                a.ActorDisplayName.Contains(kw) ||
                (a.EntityId != null && a.EntityId.Contains(kw)) ||
                (a.OldValue != null && a.OldValue.Contains(kw)) ||
                (a.NewValue != null && a.NewValue.Contains(kw)) ||
                (a.Context != null && a.Context.Contains(kw)));
        }

        var totalCount = await query.CountAsync(ct);

        query = filter.SortBy switch
        {
            "Module" => filter.SortDescending ? query.OrderByDescending(a => a.Module) : query.OrderBy(a => a.Module),
            "EntityType" => filter.SortDescending ? query.OrderByDescending(a => a.EntityType) : query.OrderBy(a => a.EntityType),
            "Action" => filter.SortDescending ? query.OrderByDescending(a => a.Action) : query.OrderBy(a => a.Action),
            "ActorDisplayName" => filter.SortDescending ? query.OrderByDescending(a => a.ActorDisplayName) : query.OrderBy(a => a.ActorDisplayName),
            "RoleGroupAtTime" => filter.SortDescending ? query.OrderByDescending(a => a.RoleGroupAtTime) : query.OrderBy(a => a.RoleGroupAtTime),
            "IsSuccess" => filter.SortDescending ? query.OrderByDescending(a => a.IsSuccess) : query.OrderBy(a => a.IsSuccess),
            "IpAddress" => filter.SortDescending ? query.OrderByDescending(a => a.IpAddress) : query.OrderBy(a => a.IpAddress),
            "GeoLocation" => filter.SortDescending ? query.OrderByDescending(a => a.GeoLocation) : query.OrderBy(a => a.GeoLocation),
            _ => filter.SortDescending ? query.OrderByDescending(a => a.OccurredAtUtc) : query.OrderBy(a => a.OccurredAtUtc),
        };

        var items = await query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<List<string>> GetDistinctModulesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.AuditLog.Select(a => a.Module).Distinct().OrderBy(m => m).ToListAsync(ct);
    }

    public async Task<List<string>> GetDistinctEntityTypesAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.AuditLog.Select(a => a.EntityType).Distinct().OrderBy(e => e).ToListAsync(ct);
    }

    public async Task<List<string>> GetDistinctActionsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.AuditLog.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
    }
}

// ==================================================================
// IDashboardNotificationService — targeted, dismissible dashboard banners.
// Built now as Stage 1 foundation for the future onboarding/offboarding
// flow ("notify Correspondent when a new staff profile needs a salary
// set"), but usable by anything - nothing here is specific to staff
// lifecycle events.
// ==================================================================

public interface IDashboardNotificationService
{
    Task<Guid> CreateAsync(string message, string targetRoleGroupName, string? linkUrl = null,
                            DateTime? expiresAtUtc = null, CancellationToken ct = default);

    /// <summary>Active (not expired, not dismissed by this specific user) notifications targeting the current user's role group.</summary>
    Task<List<DashboardNotification>> GetActiveForCurrentUserAsync(CancellationToken ct = default);

    Task DismissAsync(Guid notificationId, CancellationToken ct = default);
}

public class DashboardNotificationService : IDashboardNotificationService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;

    public DashboardNotificationService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user)
    {
        _dbf = dbf;
        _user = user;
    }

    public async Task<Guid> CreateAsync(string message, string targetRoleGroupName, string? linkUrl = null,
                                        DateTime? expiresAtUtc = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var notification = new DashboardNotification
        {
            Id = Guid.NewGuid(),
            Message = message,
            LinkUrl = linkUrl,
            TargetRoleGroupName = targetRoleGroupName,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = _user.ObjectId,
            CreatedByDisplayName = _user.DisplayName,
            ExpiresAtUtc = expiresAtUtc
        };
        db.DashboardNotifications.Add(notification);
        await db.SaveChangesAsync(ct);
        return notification.Id;
    }

    public async Task<List<DashboardNotification>> GetActiveForCurrentUserAsync(CancellationToken ct = default)
    {
        if (!_user.UserAccountId.HasValue || string.IsNullOrEmpty(_user.RoleGroupName))
            return new List<DashboardNotification>();

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var userAccountId = _user.UserAccountId.Value;
        var now = DateTime.UtcNow;

        var dismissedIds = await db.DashboardNotificationDismissals
            .Where(d => d.UserAccountId == userAccountId)
            .Select(d => d.NotificationId)
            .ToListAsync(ct);

        return await db.DashboardNotifications
            .Where(n => n.TargetRoleGroupName == _user.RoleGroupName)
            .Where(n => n.ExpiresAtUtc == null || n.ExpiresAtUtc > now)
            .Where(n => !dismissedIds.Contains(n.Id))
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task DismissAsync(Guid notificationId, CancellationToken ct = default)
    {
        if (!_user.UserAccountId.HasValue) return;

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var exists = await db.DashboardNotificationDismissals
            .AnyAsync(d => d.NotificationId == notificationId && d.UserAccountId == _user.UserAccountId.Value, ct);
        if (exists) return;

        db.DashboardNotificationDismissals.Add(new DashboardNotificationDismissal
        {
            NotificationId = notificationId,
            UserAccountId = _user.UserAccountId.Value,
            DismissedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
