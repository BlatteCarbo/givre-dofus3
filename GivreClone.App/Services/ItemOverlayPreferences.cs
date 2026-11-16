using System;
using System.IO;
using System.Text;

namespace GivreClone.App.Services;

public static class ItemOverlayPreferences
{
    public static string FilePath => Path.Combine(MZP19LocalClone.DataRoot, "ItemIdOverlay", "settings.tsv");

    public static string ReadFormat()
    {
        try
        {
            if (new FileInfo(FilePath) is { Exists: true, Length: <= 128 })
                foreach (string line in File.ReadAllLines(FilePath))
                    if (line == "format\tbrackets") return "brackets";
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "hash";
    }

    public static void Save(bool enabled, string format)
    {
        if (format is not ("hash" or "brackets")) throw new ArgumentOutOfRangeException(nameof(format));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, $"enabled\t{(enabled ? "true" : "false")}\nformat\t{format}\n", new UTF8Encoding(false));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
