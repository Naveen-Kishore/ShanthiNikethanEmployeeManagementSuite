using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Modules;

namespace ShanthiNikethan.EmployeeManagement.Tests.TestHelpers;

/// <summary>
/// A real IDbContextFactory&lt;AppDbContext&gt; backed by an in-memory
/// SQLite database, not a mock. Using SQLite (rather than EF Core's own
/// bare "InMemory" provider) matters specifically because it enforces
/// real constraints - unique indexes, foreign keys - the same way SQL
/// Server does, which is exactly the kind of thing a fake in-memory
/// provider would silently let through.
///
/// SQLite's in-memory mode only persists data while at least one
/// connection to it stays open, so this class holds one open for its
/// entire lifetime and every AppDbContext it hands out shares that same
/// underlying connection/options.
///
/// AppDbContext's constructor needs a real, working ModuleRegistry (it
/// reads EnabledModules while building the schema) - not something that
/// can be skipped or mocked away, so this builds one directly rather
/// than reading modules.json from disk (there's no IWebHostEnvironment
/// available in a test context to read it with). Every real module gets
/// enabled here, so the full schema the app actually uses in production
/// gets created for tests too, not a partial subset that happens to be
/// missing whatever table a given test needs.
/// </summary>
public class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly ModuleRegistry _moduleRegistry;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        var discovered = ModuleRegistry.DiscoverModules().ToList();
        var config = new ModulesRoot();
        foreach (var module in discovered)
            config.Modules[module.Name] = new ModuleConfig { Enabled = true, LicenseTier = LicenseTier.Base };
        _moduleRegistry = new ModuleRegistry(config, discovered);

        using var context = new AppDbContext(_options, _moduleRegistry);
        context.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options, _moduleRegistry);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
