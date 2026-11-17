using System.Text;

namespace IndustrialDuckPanic.Core.Logs;

/// <summary>
/// Follows the newest EVE gamelog of one character (Documents\EVE\logs\Gamelogs) and returns new events.
/// Only reads files; never touches the game.
/// </summary>
public sealed class GameLogWatcher
{
    private readonly string _folder;
    private readonly int _characterId;
    private readonly string _characterName;
    private readonly Dictionary<string, string?> _listenerCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastScan = DateTime.MinValue;

    private string? _file;
    private long _position;
    private Decoder? _decoder;
    private readonly StringBuilder _partial = new();

    public GameLogWatcher(string folder, int characterId, string characterName)
    {
        _folder = folder;
        _characterId = characterId;
        _characterName = characterName;
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EVE", "logs", "Gamelogs");

    public string Folder => _folder;
    public bool FolderExists => Directory.Exists(_folder);
    public string? CurrentFile => _file;

    /// <summary>How often the folder is scanned for a newer log (new game session).</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Call about once per second.</summary>
    public IReadOnlyList<GameLogEvent> Poll(DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        if (_file is null || now - _lastScan >= ScanInterval)
        {
            _lastScan = now;
            var newest = FindNewestLog();
            if (newest is not null && !string.Equals(newest, _file, StringComparison.OrdinalIgnoreCase))
                Switch(newest, fromStart: _file is not null);
        }
        return _file is null ? [] : ReadNew();
    }

    /// <summary>
    /// First file found: start at its end (old lines are history).
    /// A file that appears later is a new session: read it from the start.
    /// </summary>
    private void Switch(string file, bool fromStart)
    {
        _file = file;
        _partial.Clear();
        var bytes = ReadHead(file, 4);
        var encoding = DetectEncoding(bytes, out var bomLength);
        _decoder = encoding.GetDecoder();
        _position = fromStart ? bomLength : new FileInfo(file).Length;
    }

    private IReadOnlyList<GameLogEvent> ReadNew()
    {
        var events = new List<GameLogEvent>();
        try
        {
            using var fs = new FileStream(_file!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _position) { _position = 0; _partial.Clear(); }
            if (fs.Length == _position) return events;

            fs.Seek(_position, SeekOrigin.Begin);
            var buffer = new byte[Math.Min(fs.Length - _position, 1 << 20)];
            var read = fs.Read(buffer, 0, buffer.Length);
            _position += read;

            var chars = new char[_decoder!.GetCharCount(buffer, 0, read)];
            _decoder.GetChars(buffer, 0, read, chars, 0);
            _partial.Append(chars);

            var text = _partial.ToString();
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0) return events;
            _partial.Clear().Append(text, lastNewline + 1, text.Length - lastNewline - 1);

            foreach (var line in text[..lastNewline].Split('\n'))
                if (GameLogParser.Parse(line) is { } ev) events.Add(ev);
        }
        catch (IOException) { /* file busy or gone: retry next poll */ }
        catch (UnauthorizedAccessException) { }
        return events;
    }

    private string? FindNewestLog()
    {
        if (!FolderExists) return null;
        var cutoff = DateTime.UtcNow.AddDays(-2);
        FileInfo[] files;
        try
        {
            files = new DirectoryInfo(_folder).GetFiles("*.txt")
                .Where(f => f.LastWriteTimeUtc >= cutoff)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();
        }
        catch (IOException) { return null; }

        foreach (var f in files)
        {
            // Recent clients name the file yyyyMMdd_HHmmss_<characterId>.txt
            if (Path.GetFileNameWithoutExtension(f.Name).EndsWith("_" + _characterId, StringComparison.Ordinal))
                return f.FullName;
            if (string.Equals(ListenerOf(f.FullName), _characterName, StringComparison.OrdinalIgnoreCase))
                return f.FullName;
        }
        return null;
    }

    private string? ListenerOf(string path)
    {
        if (_listenerCache.TryGetValue(path, out var cached)) return cached;
        string? name = null;
        try
        {
            var head = ReadHead(path, 2048);
            var encoding = DetectEncoding(head, out var bom);
            var text = encoding.GetString(head, bom, head.Length - bom);
            name = GameLogParser.ReadListener(text.Split('\n'));
        }
        catch (IOException) { }
        _listenerCache[path] = name;
        return name;
    }

    private static byte[] ReadHead(string path, int count)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buf = new byte[Math.Min(count, fs.Length)];
        var n = fs.Read(buf, 0, buf.Length);
        return n == buf.Length ? buf : buf[..n];
    }

    /// <summary>EVE logs are UTF-16 LE (chat) or UTF-8; detect from BOM or zero bytes.</summary>
    public static Encoding DetectEncoding(byte[] head, out int bomLength)
    {
        bomLength = 0;
        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE) { bomLength = 2; return Encoding.Unicode; }
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) { bomLength = 3; return new UTF8Encoding(false); }
        if (head.Length >= 2 && head[1] == 0x00 && head[0] != 0x00) return Encoding.Unicode;
        return new UTF8Encoding(false);
    }
}
