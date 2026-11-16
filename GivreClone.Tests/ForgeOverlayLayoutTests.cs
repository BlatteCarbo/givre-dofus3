using GivreClone.App.Services.ForgeAdvice;
using Xunit;

namespace GivreClone.Tests;

public sealed class ForgeOverlayLayoutTests
{
    [Fact]
    public void Ba_cell_is_the_first_row_at_the_calibrated_origin()
    {
        var calibration = new ForgeOverlayCalibration(FirstRowX: 100, FirstRowY: 200, RowHeight: 30, ColumnCount: 3);
        var cell = ForgeOverlayLayout.CellFor(calibration, lineIndex: 0, tier: "Ba");
        Assert.NotNull(cell);
        Assert.Equal(100, cell!.X);
        Assert.Equal(200, cell.Y);
        Assert.Equal(30, cell.Size);
    }

    [Fact]
    public void Pa_and_Ra_columns_shift_right_by_one_row_height_each()
    {
        var calibration = new ForgeOverlayCalibration(FirstRowX: 100, FirstRowY: 200, RowHeight: 30, ColumnCount: 3);
        var pa = ForgeOverlayLayout.CellFor(calibration, lineIndex: 0, tier: "Pa");
        var ra = ForgeOverlayLayout.CellFor(calibration, lineIndex: 0, tier: "Ra");
        Assert.Equal(130, pa!.X);
        Assert.Equal(160, ra!.X);
    }

    [Fact]
    public void Each_line_index_moves_down_by_one_row_height()
    {
        var calibration = new ForgeOverlayCalibration(FirstRowX: 100, FirstRowY: 200, RowHeight: 30, ColumnCount: 3);
        var secondLine = ForgeOverlayLayout.CellFor(calibration, lineIndex: 1, tier: "Ba");
        Assert.Equal(230, secondLine!.Y);
    }

    [Fact]
    public void Unknown_tier_or_negative_line_returns_null()
    {
        var calibration = new ForgeOverlayCalibration(FirstRowX: 100, FirstRowY: 200, RowHeight: 30, ColumnCount: 3);
        Assert.Null(ForgeOverlayLayout.CellFor(calibration, lineIndex: 0, tier: "Unknown"));
        Assert.Null(ForgeOverlayLayout.CellFor(calibration, lineIndex: -1, tier: "Ba"));
    }

    [Fact]
    public void Scale_multiplies_origin_and_row_height_by_the_dpi_factor()
    {
        var calibration = new ForgeOverlayCalibration(FirstRowX: 100, FirstRowY: 200, RowHeight: 30, ColumnCount: 3);
        var scaled = ForgeOverlayLayout.Scale(calibration, 1.5);
        Assert.Equal(150, scaled.FirstRowX);
        Assert.Equal(300, scaled.FirstRowY);
        Assert.Equal(45, scaled.RowHeight);
    }
}
