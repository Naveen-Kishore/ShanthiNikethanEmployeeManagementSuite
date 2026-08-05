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
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public bool IsAuthenticated => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public string DisplayName =>
        _accessor.HttpContext?.User.FindFirst("name")?.Value
        ?? _accessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value
        ?? _accessor.HttpContext?.User.FindFirst("preferred_username")?.Value
        ?? "unknown";

    public string ObjectId =>
        _accessor.HttpContext?.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
        ?? _accessor.HttpContext?.User.FindFirst("oid")?.Value
        ?? string.Empty;
}

// ==================================================================
// IAuditService — one call site for every mutation to record itself
// ==================================================================

public interface IAuditService
{
    Task LogAsync(string module, string entityType, string? entityId, string action,
                  string? field = null, string? oldValue = null, string? newValue = null,
                  string? context = null, CancellationToken ct = default);

    Task<List<AuditLogEntry>> GetRecentAsync(int count = 10, CancellationToken ct = default);
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
                               string? context = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        db.AuditLog.Add(new AuditLogEntry
        {
            OccurredAtUtc = DateTime.UtcNow,
            ActorDisplayName = _user.DisplayName,
            ActorObjectId = _user.ObjectId,
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
}
