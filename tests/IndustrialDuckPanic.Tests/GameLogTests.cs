using System.Text;
using IndustrialDuckPanic.Core.Logs;

namespace IndustrialDuckPanic.Tests;

public class GameLogParserTests
{
    [Fact]
    public void Parses_scramble_with_eve_markup()
    {
        const string line = "[ 2026.11.17 18:22:31 ] (combat) <color=0xffffffff><b>Warp scramble attempt</b> <color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b>Gurista Dismantler</b> <color=0x77ffffff><font size=10>to <b><color=0xffffffff></font>you!";
        var ev = Assert.IsType<TackleEvent>(GameLogParser.Parse(line));
        Assert.Equal("Warp scramble attempt", ev.Kind);
        Assert.Equal("Gurista Dismantler", ev.Attacker);
        Assert.Equal(new DateTime(2026, 11, 17, 18, 22, 31, DateTimeKind.Utc), ev.TimestampUtc);
    }

    [Fact]
    public void Parses_disruption_with_showinfo_link()
    {
        const string line = "[ 2026.11.17 18:22:31 ] (combat) <color=0xffffffff><b>Warp disruption attempt</b> <color=0x77ffffff><font size=10>from</font> <color=0xffffffff><b><a href=showinfo:11176//1234>Bad &amp; Guy</a></b> <color=0x77ffffff><font size=10>to <b><color=0xffffffff></font>you!\r";
        var ev = Assert.IsType<TackleEvent>(GameLogParser.Parse(line));
        Assert.Equal("Warp disruption attempt", ev.Kind);
        Assert.Equal("Bad & Guy", ev.Attacker);
    }

    [Fact]
    public void Ignores_our_own_scram_on_someone_else()
    {
        const string line = "[ 2026.11.17 18:22:31 ] (combat) <b>Warp scramble attempt</b> from <b>you</b> to <b>Some Rat</b>!";
        Assert.Null(GameLogParser.Parse(line));
    }

    [Theory]
    [InlineData("[ 2026.11.17 18:22:31 ] (combat) 312 from Gurista - Hits")]
    [InlineData("[ 2026.11.17 18:22:31 ] (notify) Warp scramble attempt from X to you!")]
    [InlineData("random text")]
    [InlineData("")]
    public void Ignores_other_lines(string line) => Assert.Null(GameLogParser.Parse(line));

    [Fact]
    public void Parses_jump()
    {
        var ev = Assert.IsType<JumpEvent>(GameLogParser.Parse("[ 2026.11.17 18:30:00 ] (None) Jumping from 1DQ1-A to 8WA-Z6"));
        Assert.Equal("1DQ1-A", ev.FromSystem);
        Assert.Equal("8WA-Z6", ev.ToSystem);
    }

    [Fact]
    public void Reads_listener_from_header()
    {
        var header = new[]
        {
            "------------------------------------------------------------",
            "  Gamelog",
            "  Listener: Mining Duck",
            "  Session Started: 2026.11.17 18:00:00",
        };
        Assert.Equal("Mining Duck", GameLogParser.ReadListener(header));
    }
}

public class GameLogWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "idp-tests-" + Guid.NewGuid().ToString("N"));

    public GameLogWatcherTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string Header = "------------------------------------------------------------\r\n  Gamelog\r\n  Listener: Mining Duck\r\n  Session Started: 2026.11.17 18:00:00\r\n------------------------------------------------------------\r\n";
    private const string Scram = "[ 2026.11.17 18:22:31 ] (combat) <b>Warp scramble attempt</b> from <b>Rat</b> to <b>you!</b>\r\n";

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    public void Tails_new_lines_only(string enc)
    {
        Encoding encoding = enc == "utf8" ? new UTF8Encoding(false) : Encoding.Unicode;
        var path = Path.Combine(_dir, "20261117_180000_95465499.txt");
        File.WriteAllBytes(path, [.. encoding.GetPreamble(), .. encoding.GetBytes(Header + Scram)]);

        var w = new GameLogWatcher(_dir, 95465499, "Mining Duck");
        Assert.Empty(w.Poll());              // existing lines are history
        Assert.Equal(path, w.CurrentFile);

        AppendText(path, encoding, Scram);
        var ev = Assert.Single(w.Poll());
        Assert.IsType<TackleEvent>(ev);
        Assert.Empty(w.Poll());
    }

    [Fact]
    public void Keeps_half_written_line_until_complete()
    {
        var path = Path.Combine(_dir, "20261117_180000_1.txt");
        File.WriteAllText(path, Header);
        var w = new GameLogWatcher(_dir, 1, "Mining Duck");
        w.Poll();

        AppendText(path, new UTF8Encoding(false), Scram[..30]);
        Assert.Empty(w.Poll());
        AppendText(path, new UTF8Encoding(false), Scram[30..]);
        Assert.Single(w.Poll());
    }

    [Fact]
    public void Finds_file_by_listener_and_ignores_other_characters()
    {
        File.WriteAllText(Path.Combine(_dir, "20261117_170000.txt"), Header.Replace("Mining Duck", "Someone Else"));
        var mine = Path.Combine(_dir, "20261117_160000.txt");
        File.WriteAllText(mine, Header);
        File.SetLastWriteTimeUtc(mine, DateTime.UtcNow.AddMinutes(-5));

        var w = new GameLogWatcher(_dir, 42, "Mining Duck");
        w.Poll();
        Assert.Equal(mine, w.CurrentFile);
    }

    [Fact]
    public void Reads_new_session_file_from_start()
    {
        var first = Path.Combine(_dir, "20261117_160000_7.txt");
        File.WriteAllText(first, Header);
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(-10));
        var w = new GameLogWatcher(_dir, 7, "Mining Duck") { ScanInterval = TimeSpan.Zero };
        w.Poll();

        var second = Path.Combine(_dir, "20261117_190000_7.txt");
        File.WriteAllText(second, Header + Scram);
        var events = w.Poll();
        Assert.Equal(second, w.CurrentFile);
        Assert.Single(events);
    }

    [Fact]
    public void Missing_folder_is_not_an_error()
    {
        var w = new GameLogWatcher(Path.Combine(_dir, "nope"), 1, "x");
        Assert.False(w.FolderExists);
        Assert.Empty(w.Poll());
    }

    private static void AppendText(string path, Encoding encoding, string text)
    {
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        var bytes = encoding.GetBytes(text);
        fs.Write(bytes);
    }
}
