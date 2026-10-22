using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentShareTests : IAsyncLifetime
{
    private readonly DocumentServiceFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Direct_share_grants_access_notification_and_revocation()
    {
        _fixture.Db.Users.Add(new User { UserId = 99, Email = "recipient@example.test", DisplayName = "Recipient", Department = "Sales", Role = UserRole.Employee });
        await _fixture.Db.SaveChangesAsync();
        var uploaded = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Shareable", "share"));
        Assert.True(uploaded.Succeeded);

        Assert.True(await _fixture.Service.ShareAsync(4, uploaded.DocumentId!.Value, 99, null));
        Assert.Single(await _fixture.Service.SearchAsync(99, new DocumentSearch()));
        Assert.True(await _fixture.Db.Notifications.AnyAsync(n => n.UserId == 99 && n.Type == NotificationType.DocumentShared));
        var shareId = await _fixture.Db.DocumentShares.Select(s => s.DocumentShareId).SingleAsync();
        Assert.True(await _fixture.Service.UnshareAsync(4, uploaded.DocumentId.Value, shareId));
        Assert.Empty(await _fixture.Service.SearchAsync(99, new DocumentSearch()));
    }

    [Fact]
    public async Task Share_requires_owner_and_exactly_one_recipient()
    {
        _fixture.Db.Users.Add(new User { UserId = 99, Email = "recipient@example.test", DisplayName = "Recipient", Department = "Sales", Role = UserRole.Employee });
        await _fixture.Db.SaveChangesAsync();
        var uploaded = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Shareable", "share"));
        Assert.True(uploaded.Succeeded);

        Assert.False(await _fixture.Service.ShareAsync(3, uploaded.DocumentId!.Value, 99, null));
        Assert.False(await _fixture.Service.ShareAsync(4, uploaded.DocumentId.Value, 99, "Sales"));
        Assert.Empty(await _fixture.Db.DocumentShares.ToListAsync());
    }

    [Fact]
    public async Task Replacement_scans_new_content_before_removing_old_file()
    {
        var uploaded = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Current", "current"));
        Assert.True(uploaded.Succeeded);
        var oldKey = await _fixture.Db.Documents.Where(d => d.DocumentId == uploaded.DocumentId)
            .Select(d => d.StorageKey).SingleAsync();
        var replacement = new DocumentUploadRequest
        {
            FileName = "replacement.pdf",
            Content = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nreplacement")),
            Title = "Current",
            Category = DocumentCategory.Reports
        };

        var replaced = await _fixture.Service.ReplaceFileAsync(4, uploaded.DocumentId!.Value, replacement);

        Assert.True(replaced);
        var currentKey = await _fixture.Db.Documents.Where(d => d.DocumentId == uploaded.DocumentId)
            .Select(d => d.StorageKey).SingleAsync();
        Assert.NotEqual(oldKey, currentKey);
        Assert.DoesNotContain(oldKey, _fixture.Storage.Published.Keys);
        Assert.Contains(currentKey, _fixture.Storage.Published.Keys);
    }

    [Fact]
    public async Task Failed_replacement_preserves_current_file()
    {
        var uploaded = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Current", "current"));
        var oldKey = await _fixture.Db.Documents.Where(d => d.DocumentId == uploaded.DocumentId)
            .Select(d => d.StorageKey).SingleAsync();
        _fixture.Scanner.Verdict = FileScanVerdict.Unavailable;
        var replacement = new DocumentUploadRequest
        {
            FileName = "replacement.pdf",
            Content = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nreplacement")),
            Title = "Current",
            Category = DocumentCategory.Reports
        };

        Assert.False(await _fixture.Service.ReplaceFileAsync(4, uploaded.DocumentId!.Value, replacement));
        Assert.Equal(oldKey, await _fixture.Db.Documents.Where(d => d.DocumentId == uploaded.DocumentId)
            .Select(d => d.StorageKey).SingleAsync());
        Assert.Contains(oldKey, _fixture.Storage.Published.Keys);
    }
}
