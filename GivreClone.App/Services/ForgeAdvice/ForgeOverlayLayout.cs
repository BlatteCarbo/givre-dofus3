using System;

namespace GivreClone.App.Services.ForgeAdvice;

public sealed record ForgeOverlayCalibration(double FirstRowX, double FirstRowY, double RowHeight, int ColumnCount);

public sealed record ForgeOverlayCell(double X, double Y, double Size);

public static class ForgeOverlayLayout
{
    public static ForgeOverlayCell? CellFor(ForgeOverlayCalibration calibration, int lineIndex, string tier)
    {
        if (lineIndex < 0 || calibration.RowHeight <= 0 || calibration.ColumnCount <= 0) return null;
        int column = tier switch { "Ba" => 0, "Pa" => 1, "Ra" => 2, _ => -1 };
        if (column < 0 || column >= calibration.ColumnCount) return null;
        double columnSpacing = calibration.RowHeight;
        return new ForgeOverlayCell(
            calibration.FirstRowX + column * columnSpacing,
            calibration.FirstRowY + lineIndex * calibration.RowHeight,
            calibration.RowHeight);
    }

    public static ForgeOverlayCalibration Scale(ForgeOverlayCalibration calibration, double factor)
    {
        if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        return calibration with
        {
            FirstRowX = calibration.FirstRowX * factor,
            FirstRowY = calibration.FirstRowY * factor,
            RowHeight = calibration.RowHeight * factor,
        };
    }
}
