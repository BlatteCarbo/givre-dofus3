using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class TransportLogFilesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "givre-log-selection-" + Guid.NewGuid().ToString("N"));
    public TransportLogFilesTests() => Directory.CreateDirectory(_directory);
    private string Log(string name)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, "test line");
        return path;
    }

    [Fact]
    public void Current_segment_wins_even_if_old_segment_has_newer_metadata()
    {
        string old = Log("transport-old.log"), current = Log("transport-current.log");
        File.SetLastWriteTimeUtc(current, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow);
        Assert.Equal(current, TransportLogFiles.Select(_directory, "", current));
        string rotated = Log("transport-current.2.log");
        Assert.Equal(rotated, TransportLogFiles.Select(_directory, "", rotated));
    }

    [Fact]
    public void Explicit_history_is_still_readable()
    {
        string old = Log("transport-old.log"), current = Log("transport-current.log");
        Assert.Equal(old, TransportLogFiles.Select(_directory, "transport-old.log", current));
        Assert.Null(TransportLogFiles.Select(_directory, "transport-missing.log", current));
    }

    [Theory]
    [InlineData("../transport-other.log")]
    [InlineData("settings.json")]
    public void Arbitrary_files_are_rejected(string name)
        => Assert.Throws<ArgumentException>(() => TransportLogFiles.Select(_directory, name, null));

    [Fact]
    public void Missing_current_file_falls_back_to_available_history()
    {
        string old = Log("transport-old.log");
        Assert.Equal(old, TransportLogFiles.Select(_directory, "", Path.Combine(_directory, "transport-missing.log")));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
