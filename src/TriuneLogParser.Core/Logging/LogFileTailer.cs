using System.Text;

namespace TriuneLogParser.Core.Logging;

/// <summary>
/// Reads an EverQuest log file and, optionally, follows it as the game client appends
/// to it. The client keeps the file open for writing, so we always open shared and
/// never lock it. Handles truncation / rotation by resetting to the start of the file.
/// </summary>
public sealed class LogFileTailer : IDisposable
{
    private readonly string _path;
    private readonly TimeSpan _pollInterval;
    private long _position;
    private long _lastLength;
    private int _lineNumber;
    private readonly StringBuilder _partial = new();

    public LogFileTailer(string path, TimeSpan? pollInterval = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
    }

    public string Path => _path;

    /// <summary>Character name inferred from the file name (eqlog_&lt;Character&gt;_&lt;server&gt;.txt).</summary>
    public string? CharacterName => TryExtractCharacterName(_path);

    /// <summary>Server tag inferred from the file name.</summary>
    public string? ServerName => TryExtractServerName(_path);

    /// <summary>Reads every complete line currently in the file, from wherever we left off.</summary>
    public IEnumerable<string> ReadNewLines()
    {
        if (!File.Exists(_path))
            yield break;

        using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        long length = stream.Length;
        if (length < _lastLength || _position > length)
        {
            // File was truncated or rotated — start over.
            _position = 0;
            _lineNumber = 0;
            _partial.Clear();
        }

        _lastLength = length;
        if (_position >= length)
            yield break;

        stream.Seek(_position, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            _lineNumber++;
            yield return line;
        }

        _position = stream.Position;
    }

    /// <summary>
    /// Follows the file until <paramref name="cancellationToken"/> fires, invoking
    /// <paramref name="onLine"/> for each new line (with a running 1-based line number).
    /// </summary>
    public async Task FollowAsync(Action<string, int> onLine, CancellationToken cancellationToken)
    {
        // Emit whatever is already there first.
        foreach (string line in ReadNewLines())
            onLine(line, _lineNumber);

        using var watcher = CreateWatcher(out var signal);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            signal.Reset();
            foreach (string line in ReadNewLines())
                onLine(line, _lineNumber);
        }
    }

    private FileSystemWatcher? CreateWatcher(out ManualResetEventSlimAsync signal)
    {
        var s = new ManualResetEventSlimAsync();
        signal = s;
        try
        {
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path)) ?? ".";
            string file = System.IO.Path.GetFileName(_path);
            var w = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            w.Changed += (_, _) => s.Set();
            w.Created += (_, _) => s.Set();
            w.Renamed += (_, _) => s.Set();
            return w;
        }
        catch (Exception)
        {
            // Directory missing / not watchable — polling alone still works.
            return null;
        }
    }

    public void Dispose()
    {
    }

    // File name is eqlog_<Character>_<server>.txt. The character is a single token;
    // everything after the first underscore is the server tag.
    public static string? TryExtractCharacterName(string path)
    {
        string rest = StripPrefix(path);
        if (rest.Length == 0)
            return null;
        int idx = rest.IndexOf('_');
        return idx > 0 ? rest[..idx] : rest;
    }

    public static string? TryExtractServerName(string path)
    {
        string rest = StripPrefix(path);
        int idx = rest.IndexOf('_');
        return idx > 0 && idx < rest.Length - 1 ? rest[(idx + 1)..] : null;
    }

    private static string StripPrefix(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("eqlog_", StringComparison.OrdinalIgnoreCase)
            ? name["eqlog_".Length..]
            : string.Empty;
    }

    /// <summary>Minimal awaitable reset event so following doesn't spin.</summary>
    private sealed class ManualResetEventSlimAsync
    {
        private volatile TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Set() => _tcs.TrySetResult(true);

        public void Reset()
        {
            if (_tcs.Task.IsCompleted)
                _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Task delay = Task.Delay(timeout, cancellationToken);
            await Task.WhenAny(_tcs.Task, delay).ConfigureAwait(false);
        }
    }
}
