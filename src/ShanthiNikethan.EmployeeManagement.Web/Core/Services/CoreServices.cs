using System.ComponentModel.DataAnnotations;
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
}

public class ModuleStateRecord
{
    [MaxLength(50)]  public string ModuleName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public LicenseTier LicenseTier { get; set; }
    public DateTime LastStartedAtUtc { get; set; } = DateTime.UtcNow;
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
    /// </summary>
    Task LogAsync(string module, string entityType, string? entityId, string action,
                  string? field = null, string? oldValue = null, string? newValue = null,
                  string? context = null, string? actorDisplayNameOverride = null,
                  string? actorObjectIdOverride = null, CancellationToken ct = default);

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
                               string? actorObjectIdOverride = null, CancellationToken ct = default)
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
            Context = Truncate(context, 500)
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
