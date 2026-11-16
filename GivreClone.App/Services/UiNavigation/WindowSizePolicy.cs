namespace GivreClone.App.Services.UiNavigation;

public static class WindowSizePolicy
{
    public static (double Width, double Height) Restore(double savedWidth, double savedHeight, double minWidth, double minHeight,
        double? screenWidth, double? screenHeight)
    {
        if (savedWidth <= 0 || savedHeight <= 0 || double.IsNaN(savedWidth) || double.IsNaN(savedHeight)) return (0, 0);
        double w = System.Math.Max(minWidth, savedWidth), h = System.Math.Max(minHeight, savedHeight);
        if (screenWidth is > 0 && w > screenWidth) w = System.Math.Max(minWidth, screenWidth.Value);
        if (screenHeight is > 0 && h > screenHeight) h = System.Math.Max(minHeight, screenHeight.Value);
        return (w, h);
    }
}
