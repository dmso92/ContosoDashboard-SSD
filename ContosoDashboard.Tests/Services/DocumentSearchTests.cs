using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentSearchTests : IAsyncLifetime
{
    private readonly DocumentServiceFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Search_filters_and_sorts_only_documents_visible_to_requester()
    {
        await AddOutsideUser();
        var ownerFile = await _fixture.Service.UploadAsync(4, Upload("Quarterly Report", "report"));
        var projectFile = await _fixture.Service.UploadAsync(4, Upload("Project Brief", "brief", projectId: 1));
        Assert.True(ownerFile.Succeeded);
        Assert.True(projectFile.Succeeded);

        var reportMatches = await _fixture.Service.SearchAsync(3, new DocumentSearch { Query = "report" });
        Assert.Contains(reportMatches, d => d.Title == "Quarterly Report");
        Assert.DoesNotContain(reportMatches, d => d.Title == "Project Brief");

        var leadVisible = await _fixture.Service.SearchAsync(3, new DocumentSearch { ProjectId = 1, SortBy = "title" });
        Assert.Single(leadVisible);
        Assert.Equal("Project Brief", leadVisible[0].Title);

        var outsider = await _fixture.Service.SearchAsync(99, new DocumentSearch());
        Assert.Empty(outsider);
    }

    [Fact]
    public async Task Search_can_find_documents_shared_with_requester()
    {
        await AddOutsideUser();
        var uploaded = await _fixture.Service.UploadAsync(4, Upload("Shared notes", "shared"));
        Assert.True(uploaded.Succeeded);
        Assert.True(await _fixture.Service.ShareAsync(4, uploaded.DocumentId!.Value, 99, null));

        var shared = await _fixture.Service.SearchAsync(99, new DocumentSearch { SharedWithMeOnly = true });

        Assert.Single(shared);
        Assert.Equal("Shared notes", shared[0].Title);
    }

    private async Task AddOutsideUser()
    {
        _fixture.Db.Users.Add(new User { UserId = 99, Email = "external@example.test", DisplayName = "Outside", Department = "Sales", Role = UserRole.Employee });
        await _fixture.Db.SaveChangesAsync();
    }

    internal static DocumentUploadRequest Upload(string title, string tag, int? projectId = null) => new()
    {
        FileName = $"{tag}.pdf", Content = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nsample")),
        Title = title, Category = DocumentCategory.Reports, Tags = new[] { tag }, ProjectId = projectId
    };
}
