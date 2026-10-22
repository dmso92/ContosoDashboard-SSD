using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Services;

public sealed class DocumentServiceUploadTests : IAsyncLifetime
{
    private readonly DocumentServiceFixture _fixture = new();
    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Clean_supported_file_is_published_to_uploader()
    {
        var result = await _fixture.Service.UploadAsync(4, PdfUpload());

        Assert.True(result.Succeeded);
        Assert.Single(_fixture.Storage.Published);
        var document = await _fixture.Db.Documents.Include(d => d.Tags).SingleAsync();
        Assert.Equal(4, document.UploaderUserId);
        Assert.Equal("application/pdf", document.ContentType);
        Assert.Equal(new[] { "proposal", "review" }, document.Tags.Select(t => t.Value).OrderBy(t => t).ToArray());
        Assert.Equal(DocumentActivityAction.Upload, await _fixture.Db.DocumentActivities.Select(a => a.Action).SingleAsync());
    }

    [Fact]
    public async Task Unsupported_extension_or_mismatched_content_is_rejected()
    {
        var result = await _fixture.Service.UploadAsync(4, new DocumentUploadRequest
        {
            FileName = "report.exe", Content = new MemoryStream(PdfBytes()), Title = "Report", Category = DocumentCategory.Reports
        });

        Assert.False(result.Succeeded);
        Assert.Empty(_fixture.Storage.Published);
        Assert.Empty(await _fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Infected_and_unavailable_scans_fail_closed_and_leave_no_document()
    {
        foreach (var verdict in new[] { FileScanVerdict.Infected, FileScanVerdict.Unavailable })
        {
            _fixture.Scanner.Verdict = verdict;
            var result = await _fixture.Service.UploadAsync(4, PdfUpload());
            Assert.False(result.Succeeded);
        }
        Assert.Empty(_fixture.Storage.Published);
        Assert.Empty(await _fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Project_association_requires_current_membership()
    {
        var result = await _fixture.Service.UploadAsync(4, new DocumentUploadRequest
        {
            FileName = "brief.pdf", Content = new MemoryStream(PdfBytes()), Title = "Brief",
            Category = DocumentCategory.ProjectDocuments, ProjectId = 999
        });

        Assert.False(result.Succeeded);
        Assert.Empty(await _fixture.Db.Documents.ToListAsync());
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_storage()
    {
        var oversized = new byte[(int)DocumentService.MaximumFileBytes + 1];
        oversized[0] = (byte)'%';
        oversized[1] = (byte)'P';
        oversized[2] = (byte)'D';
        oversized[3] = (byte)'F';
        oversized[4] = (byte)'-';
        var result = await _fixture.Service.UploadAsync(4, new DocumentUploadRequest
        {
            FileName = "large.pdf", Content = new MemoryStream(oversized), Title = "Large",
            Category = DocumentCategory.Reports
        });

        Assert.False(result.Succeeded);
        Assert.Empty(_fixture.Storage.Published);
    }

    [Fact]
    public async Task Inclusive_size_limit_accepts_a_supported_file_at_exactly_25_mib()
    {
        var exactLimit = new byte[(int)DocumentService.MaximumFileBytes];
        "%PDF-1.7"u8.CopyTo(exactLimit);
        var result = await _fixture.Service.UploadAsync(4, new DocumentUploadRequest
        {
            FileName = "limit.pdf", Content = new MemoryStream(exactLimit), Title = "Limit",
            Category = DocumentCategory.Reports
        });

        Assert.True(result.Succeeded);
        Assert.Equal(DocumentService.MaximumFileBytes, await _fixture.Db.Documents.Select(d => d.FileSizeBytes).SingleAsync());
    }

    [Fact]
    public async Task Database_failure_compensates_published_file()
    {
        await _fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER RejectDocument BEFORE INSERT ON Documents BEGIN SELECT RAISE(FAIL, 'forced failure'); END;");

        var result = await _fixture.Service.UploadAsync(4, PdfUpload());

        Assert.False(result.Succeeded);
        Assert.Empty(_fixture.Storage.Published);
    }

    [Fact]
    public async Task Unauthorized_user_cannot_list_or_open_another_users_document()
    {
        _fixture.Db.Users.Add(new User
        {
            UserId = 99,
            Email = "outside@example.test",
            DisplayName = "Outside Employee",
            Department = "Sales",
            Role = UserRole.Employee
        });
        await _fixture.Db.SaveChangesAsync();
        _fixture.Scanner.Verdict = FileScanVerdict.Clean;
        var uploaded = await _fixture.Service.UploadAsync(4, PdfUpload());
        Assert.True(uploaded.Succeeded);

        Assert.Empty(await _fixture.Service.SearchAsync(99, new DocumentSearch()));
        Assert.Null(await _fixture.Service.OpenFileAsync(99, uploaded.DocumentId!.Value, preview: true));
        Assert.Contains(await _fixture.Db.DocumentActivities.Select(a => a.Action).ToListAsync(),
            action => action == DocumentActivityAction.DeniedAccess);
    }

    [Fact]
    public async Task Owner_can_preview_and_download_supported_content_with_audit_events()
    {
        var uploaded = await _fixture.Service.UploadAsync(4, PdfUpload());
        Assert.True(uploaded.Succeeded);

        var preview = await _fixture.Service.OpenFileAsync(4, uploaded.DocumentId!.Value, preview: true);
        Assert.NotNull(preview);
        await preview!.Content.DisposeAsync();
        var download = await _fixture.Service.OpenFileAsync(4, uploaded.DocumentId.Value, preview: false);
        Assert.NotNull(download);
        await download!.Content.DisposeAsync();

        var events = await _fixture.Db.DocumentActivities.Select(a => a.Action).ToListAsync();
        Assert.Contains(DocumentActivityAction.Preview, events);
        Assert.Contains(DocumentActivityAction.Download, events);
    }

    private static DocumentUploadRequest PdfUpload() => new()
    {
        FileName = "proposal.pdf",
        Content = new MemoryStream(PdfBytes()),
        Title = "Proposal",
        Category = DocumentCategory.Reports,
        Tags = new[] { " proposal ", "review", "PROPOSAL" }
    };

    private static byte[] PdfBytes() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nsample document content");
}
