using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.TestSupport;

public sealed class FakeFileScanner : IFileScanner
{
    public FileScanVerdict Verdict { get; set; } = FileScanVerdict.Clean;

    public async Task<FileScanVerdict> ScanAsync(Stream content, CancellationToken cancellationToken = default)
    {
        await content.CopyToAsync(Stream.Null, cancellationToken);
        return Verdict;
    }
}
