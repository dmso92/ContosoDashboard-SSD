using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public sealed class DocumentStorageOptions
{
    public string RootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ContosoDashboard", "Documents");
}

/// <summary>Stores files outside the web root under opaque server-generated names.</summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;
    private readonly string _quarantine;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    public LocalFileStorageService(IOptions<DocumentStorageOptions> options, IWebHostEnvironment environment)
    {
        var configuredRoot = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(configuredRoot))
            configuredRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContosoDashboard", "Documents");
        _root = Path.GetFullPath(configuredRoot);
        if (!string.IsNullOrWhiteSpace(environment.WebRootPath))
        {
            var webRoot = Path.GetFullPath(environment.WebRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootPrefix = webRoot + Path.DirectorySeparatorChar;
            if (string.Equals(_root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), webRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                _root.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Document storage must be outside the web root.");
        }
        _quarantine = Path.Combine(_root, "quarantine");
    }

    public async Task<string> StageAsync(Stream content, CancellationToken cancellationToken = default)
    {
        await EnsureDirectoriesAsync(cancellationToken);
        var key = Guid.NewGuid().ToString("N");
        var path = QuarantinePath(key);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await content.CopyToAsync(output, cancellationToken);
            return key;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public async Task<string> PublishAsync(string quarantineKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureDirectoriesAsync(cancellationToken);
        var publishedKey = Guid.NewGuid().ToString("N");
        File.Move(QuarantinePath(quarantineKey), PublishedPath(publishedKey));
        return publishedKey;
    }

    public Task<Stream> OpenQuarantineReadAsync(string quarantineKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(QuarantinePath(quarantineKey), FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream>(stream);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(PublishedPath(storageKey), FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream>(stream);
    }

    public Task DeleteQuarantineAsync(string quarantineKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(QuarantinePath(quarantineKey));
        return Task.CompletedTask;
    }

    public Task DeletePublishedAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(PublishedPath(storageKey));
        return Task.CompletedTask;
    }

    private async Task EnsureDirectoriesAsync(CancellationToken cancellationToken)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_quarantine);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private string QuarantinePath(string key) => Path.Combine(_quarantine, ValidateKey(key));
    private string PublishedPath(string key) => Path.Combine(_root, ValidateKey(key));

    private static string ValidateKey(string key) =>
        Guid.TryParseExact(key, "N", out _) ? key : throw new ArgumentException("Invalid storage key.", nameof(key));

    private static void TryDelete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
