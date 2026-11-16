using System;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services.ForgeAdvice;

public static class ForgeOverlayCalibrationStore
{
    private static string FilePath => Path.Combine(MZP19LocalClone.DataRoot, "forge", "overlay-calibration.json");

    public static ForgeOverlayCalibration? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<ForgeOverlayCalibration>(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(ForgeOverlayCalibration calibration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(calibration));
    }
}
