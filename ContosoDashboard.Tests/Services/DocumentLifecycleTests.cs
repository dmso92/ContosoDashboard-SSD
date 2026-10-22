using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentLifecycleTests : IAsyncLifetime
{
    private readonly DocumentServiceFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Team_lead_can_edit_member_document_but_cannot_delete_it()
    {
        var uploaded = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Original", "original"));
        Assert.True(uploaded.Succeeded);

        var edited = await _fixture.Service.UpdateMetadataAsync(3, uploaded.DocumentId!.Value,
            "Updated", "Reviewed", DocumentCategory.TeamResources, new[] { "team" });
        var deleted = await _fixture.Service.DeleteAsync(3, uploaded.DocumentId.Value, true);

        Assert.True(edited);
        Assert.False(deleted);
        Assert.Equal("Updated", await _fixture.Db.Documents.Where(d => d.DocumentId == uploaded.DocumentId).Select(d => d.Title).SingleAsync());
    }

    [Fact]
    public async Task Deletion_requires_explicit_confirmation_and_removes_file()
    {
        var uploaded = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Delete me", "delete"));
        var key = await _fixture.Db.Documents.Where(d => d.DocumentId == uploaded.DocumentId).Select(d => d.StorageKey).SingleAsync();

        Assert.False(await _fixture.Service.DeleteAsync(4, uploaded.DocumentId!.Value, false));
        Assert.True(await _fixture.Service.DeleteAsync(4, uploaded.DocumentId.Value, true));
        Assert.Empty(await _fixture.Db.Documents.ToListAsync());
        Assert.DoesNotContain(key, _fixture.Storage.Published.Keys);
        Assert.Contains(await _fixture.Db.DocumentActivities.Select(a => a.Action).ToListAsync(), a => a == DocumentActivityAction.Delete);
    }

    [Fact]
    public async Task Project_manager_can_manage_associated_file_but_not_other_project_file()
    {
        var associated = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Managed project", "managed", 1));
        var personal = await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload("Personal", "personal"));
        Assert.True(associated.Succeeded);
        Assert.True(personal.Succeeded);

        Assert.True(await _fixture.Service.UpdateMetadataAsync(2, associated.DocumentId!.Value,
            "Manager updated", null, DocumentCategory.ProjectDocuments, Array.Empty<string>()));
        Assert.False(await _fixture.Service.UpdateMetadataAsync(2, personal.DocumentId!.Value,
            "Not allowed", null, DocumentCategory.Other, Array.Empty<string>()));
        Assert.True(await _fixture.Service.DeleteAsync(2, associated.DocumentId.Value, true));
        Assert.False(await _fixture.Service.DeleteAsync(2, personal.DocumentId.Value, true));
    }
}
