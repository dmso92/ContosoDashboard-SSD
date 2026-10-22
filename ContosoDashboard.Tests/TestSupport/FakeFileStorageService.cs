using ContosoDashboard.Services;

namespace ContosoDashboard.Tests.TestSupport;

public sealed class FakeFileStorageService : IFileStorageService
{
    private readonly Dictionary<string, byte[]> _quarantine = new();
    private readonly Dictionary<string, byte[]> _published = new();
    private int _nextKey;

    public IReadOnlyDictionary<string, byte[]> Published => _published;

    public async Task<string> StageAsync(Stream content, CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        await content.CopyToAsync(output, cancellationToken);
        var key = $"q{Interlocked.Increment(ref _nextKey):N}";
        _quarantine[key] = output.ToArray();
        return key;
    }

    public Task<Stream> OpenQuarantineReadAsync(string quarantineKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(_quarantine[quarantineKey], writable: false));

    public Task<string> PublishAsync(string quarantineKey, CancellationToken cancellationToken = default)
    {
        var key = $"p{Interlocked.Increment(ref _nextKey):N}";
        _published[key] = _quarantine[quarantineKey];
        _quarantine.Remove(quarantineKey);
        return Task.FromResult(key);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(_published[storageKey], writable: false));

    public Task DeleteQuarantineAsync(string quarantineKey, CancellationToken cancellationToken = default)
    {
        _quarantine.Remove(quarantineKey);
        return Task.CompletedTask;
    }

    public Task DeletePublishedAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        _published.Remove(storageKey);
        return Task.CompletedTask;
    }
}
