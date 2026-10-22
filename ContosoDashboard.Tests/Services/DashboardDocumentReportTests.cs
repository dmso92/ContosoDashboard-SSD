using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests.Services;

public sealed class DashboardDocumentReportTests : IAsyncLifetime
{
    private readonly DocumentServiceFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Dashboard_counts_visible_docs_and_limits_recent_uploads_to_five()
    {
        for (var i = 0; i < 6; i++)
            Assert.True((await _fixture.Service.UploadAsync(4, DocumentSearchTests.Upload($"File {i}", $"tag{i}"))).Succeeded);

        var dashboard = new DashboardService(_fixture.Db, _fixture.Service);
        var summary = await dashboard.GetDashboardSummaryAsync(4);

        Assert.Equal(6, summary.VisibleDocuments);
        Assert.Equal(5, summary.RecentDocuments.Count);
    }

    [Fact]
    public async Task Only_administrators_can_read_aggregate_document_reports()
    {
        Assert.Null(await _fixture.Service.GetReportAsync(4));
        Assert.NotNull(await _fixture.Service.GetReportAsync(1));
    }
}
