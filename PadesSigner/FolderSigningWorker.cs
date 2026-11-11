using System.Threading.Channels;

namespace PadesSigner;

/// <summary>
/// Watches the input folder and signs every PDF that appears in it, one at a time.
/// Signed files go to the output folder; originals are archived (or deleted); failures go to the error folder.
/// </summary>
public sealed class FolderSigningWorker : IDisposable
{
    private readonly Settings _settings;
    private readonly PdfSigningService _signer;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new() { SingleReader = true });
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _rescanTimer;
    private Task? _loop;

    public event Action<string, string>? FileSigned;           // input name, output path
    public event Action<string, Exception>? FileFailed;        // input name, error

    public FolderSigningWorker(Settings settings, PdfSigningService signer)
    {
        _settings = settings;
        _signer = signer;
    }

    public void Start()
    {
        var input = _settings.InputFolder;
        foreach (var other in new[] { _settings.OutputFolder, _settings.ProcessedFolder, _settings.ErrorFolder })
        {
            if (!string.IsNullOrEmpty(other) && PathsEqual(input, other))
                throw new InvalidOperationException($"InputFolder must differ from the output, processed and error folders ({other}).");
        }

        Directory.CreateDirectory(input);
        Directory.CreateDirectory(_settings.OutputFolder);
        Directory.CreateDirectory(_settings.ErrorFolder);
        if (!string.IsNullOrEmpty(_settings.ProcessedFolder)) Directory.CreateDirectory(_settings.ProcessedFolder);

        _loop = Task.Run(() => RunAsync(_cts.Token));

        _watcher = new FileSystemWatcher(input, "*.pdf")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += (_, e) => Enqueue(e.FullPath);
        _watcher.Changed += (_, e) => Enqueue(e.FullPath);
        _watcher.Renamed += (_, e) => Enqueue(e.FullPath);
        _watcher.Error += (_, e) =>
        {
            Log.Warn($"File watcher error, rescanning: {e.GetException().Message}");
            Rescan();
        };
        _watcher.EnableRaisingEvents = true;

        // Picks up files that were already waiting, and anything the watcher might have missed.
        var interval = TimeSpan.FromSeconds(_settings.RescanIntervalSeconds);
        _rescanTimer = new System.Threading.Timer(_ => Rescan(), null, TimeSpan.Zero, interval);

        Log.Info($"Watching {input} -> {_settings.OutputFolder} ({_signer.Description})");
    }

    private void Rescan()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_settings.InputFolder, "*.pdf"))
                Enqueue(file);
        }
        catch (Exception ex)
        {
            Log.Warn($"Rescan of {_settings.InputFolder} failed: {ex.Message}");
        }
    }

    private void Enqueue(string path)
    {
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return;
        lock (_pending)
        {
            if (!_pending.Add(path)) return;
        }
        _queue.Writer.TryWrite(path);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var path in _queue.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await ProcessAsync(path, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error($"Unexpected error while handling {path}", ex);
                }
                finally
                {
                    lock (_pending) _pending.Remove(path);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ProcessAsync(string inputPath, CancellationToken ct)
    {
        if (!File.Exists(inputPath)) return;

        if (!await WaitUntilReadyAsync(inputPath, ct))
        {
            Log.Warn($"{inputPath} is still in use, will retry on the next rescan.");
            return;
        }

        var name = Path.GetFileName(inputPath);
        var outputPath = UniquePath(_settings.OutputFolder,
            Path.GetFileNameWithoutExtension(name) + _settings.OutputSuffix + ".pdf");
        var tempPath = outputPath + ".partial";

        try
        {
            File.Delete(tempPath);
            _signer.Sign(inputPath, tempPath);
            // Only a complete file ever appears under its final name.
            File.Move(tempPath, outputPath);
        }
        catch (Exception ex)
        {
            TryDelete(tempPath);
            Log.Error($"Signing {name} failed", ex);
            MoveToErrorFolder(inputPath, ex);
            FileFailed?.Invoke(name, ex);
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(_settings.ProcessedFolder))
                File.Delete(inputPath);
            else
                File.Move(inputPath, UniquePath(_settings.ProcessedFolder, name));
        }
        catch (Exception ex)
        {
            // The signed file exists, but the original stays in the input folder and would be signed again.
            Log.Error($"Signed {name}, but could not remove it from the input folder", ex);
        }

        Log.Info($"Signed {name} -> {outputPath}");
        FileSigned?.Invoke(name, outputPath);
    }

    /// <summary>Waits until the producer has finished writing (the file can be opened exclusively).</summary>
    private static async Task<bool> WaitUntilReadyAsync(string path, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        long lastLength = -1;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                // Some writers close and reopen the file; also require the size to be stable.
                if (fs.Length > 0 && fs.Length == lastLength) return true;
                lastLength = fs.Length;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            await Task.Delay(500, ct);
        }
        return false;
    }

    private void MoveToErrorFolder(string inputPath, Exception error)
    {
        try
        {
            var target = UniquePath(_settings.ErrorFolder, Path.GetFileName(inputPath));
            File.Move(inputPath, target);
            File.WriteAllText(target + ".error.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{error}");
        }
        catch (Exception ex)
        {
            Log.Error($"Could not move {inputPath} to the error folder", ex);
        }
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; File.Exists(path) || File.Exists(path + ".partial"); i++)
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
        return path;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                      Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                      StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _rescanTimer?.Dispose();
        _watcher?.Dispose();
        _queue.Writer.TryComplete();
        try { _loop?.Wait(TimeSpan.FromSeconds(30)); } catch { }
        _cts.Dispose();
    }
}
