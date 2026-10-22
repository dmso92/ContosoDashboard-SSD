using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.TestSupport;

public sealed class DocumentServiceFixture : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    public ApplicationDbContext Db { get; private set; } = null!;
    public FakeFileStorageService Storage { get; } = new();
    public FakeFileScanner Scanner { get; } = new();
    public DocumentService Service { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        Db = new ApplicationDbContext(options);
        await Db.Database.EnsureCreatedAsync();
        Service = new DocumentService(Db, Storage, Scanner);
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
