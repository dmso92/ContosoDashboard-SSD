using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

/// <summary>
/// Streams quarantined content to local clamscan. Only exit code 0 is a clean verdict;
/// all missing, timed-out, malformed, or unexpected outcomes fail closed.
/// </summary>
public sealed class ClamAvFileScanner : IFileScanner
{
    private readonly FileScannerOptions _options;

    public ClamAvFileScanner(IOptions<FileScannerOptions> options) => _options = options.Value;

    public async Task<FileScanVerdict> ScanAsync(Stream content, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 300)));
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--no-summary");
            start.ArgumentList.Add("--stdout");
            start.ArgumentList.Add("-");

            process = new Process { StartInfo = start };
            if (!process.Start())
                return FileScanVerdict.Unavailable;

            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await content.CopyToAsync(process.StandardInput.BaseStream, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            _ = await stderr; // Consume stderr to avoid process pipe blockage; never expose scanner output.

            if (process.ExitCode == 0 && output.Contains("stdin: OK", StringComparison.OrdinalIgnoreCase))
                return FileScanVerdict.Clean;
            if (process.ExitCode == 1)
                return FileScanVerdict.Infected;
            return FileScanVerdict.Error;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
                throw;
            return FileScanVerdict.Error;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            return FileScanVerdict.Unavailable;
        }
        catch (IOException)
        {
            TryKill(process);
            return FileScanVerdict.Error;
        }
        catch
        {
            TryKill(process);
            return FileScanVerdict.Error;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // A stopped or inaccessible scanner is a failed scan and is never accepted.
        }
    }
}
