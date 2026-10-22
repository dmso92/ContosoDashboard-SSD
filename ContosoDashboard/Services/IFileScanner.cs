namespace ContosoDashboard.Services;

public enum FileScanVerdict
{
    Clean,
    Infected,
    Unavailable,
    Error
}

public interface IFileScanner
{
    Task<FileScanVerdict> ScanAsync(Stream content, CancellationToken cancellationToken = default);
}

public sealed class FileScannerOptions
{
    public string ExecutablePath { get; set; } = "clamscan";
    public int TimeoutSeconds { get; set; } = 30;
}
