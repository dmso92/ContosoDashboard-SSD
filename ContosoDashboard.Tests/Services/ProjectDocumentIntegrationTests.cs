using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests.Services;

public sealed class ProjectDocumentIntegrationTests : IAsyncLifetime
{
    private readonly DocumentServiceFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Task_upload_uses_task_project_and_notifies_current_members()
    {
        var request = DocumentSearchTests.Upload("Task attachment", "task");
        request = new DocumentUploadRequest
        {
            FileName = request.FileName, Content = request.Content, Title = request.Title, Category = request.Category,
            Tags = request.Tags, TaskId = 1
        };
        var result = await _fixture.Service.UploadAsync(4, request);

        Assert.True(result.Succeeded);
        var saved = await _fixture.Db.Documents.SingleAsync(d => d.DocumentId == result.DocumentId);
        Assert.Equal(1, saved.TaskId);
        Assert.Equal(1, saved.ProjectId);
        Assert.True(await _fixture.Db.Notifications.AnyAsync(n => n.UserId == 3 && n.Type == NotificationType.ProjectDocumentAdded));
        Assert.Single(await _fixture.Service.SearchAsync(3, new DocumentSearch { ProjectId = 1 }));
    }
}
