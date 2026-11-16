using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GivreClone.App.Services;
using GivreClone.App.Services.ForgeAdvice;

namespace GivreClone.App.Views;

// Fenetre transparente, sans decoration, posee par-dessus le client Dofus MITM. Click-through (WS_EX_TRANSPARENT) :
// jamais de focus, jamais de clic vole au jeu. Suit la geometrie de la fenetre du jeu et dessine le cadre conseille
// par ForgeAdviceController (pont InGameAdviceBridge de SmithMagicViewModel), sans jamais poser de rune a la place
// du joueur -- lecture seule, comme le dit explicitement ForgeAdviceRecord.
public sealed partial class ForgeOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20, WsExLayered = 0x80000, WsExToolWindow = 0x80;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private int _gamePid;
    private ForgeOverlayCalibration? _calibration;
    private bool _calibrating;
    private Point? _baCell, _paCell, _raCell, _secondRowBaCell;
    private readonly string[] _calibrationSteps =
    {
        "Clique au centre de la 1re case de rune (Ba) de la 1re ligne.",
        "Clique au centre de la case Pa de la 1re ligne.",
        "Clique au centre de la case Ra de la 1re ligne.",
        "Clique au centre de la 1re case de rune (Ba) de la 2e ligne.",
    };
    private int _calibrationStep;

    public ForgeOverlayWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero)
        {
            int style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExLayered | WsExToolWindow);
        }
        else return;
        SetClickThrough(true);
    }

    public void AttachToGame(int pid, ForgeOverlayCalibration? calibration)
    {
        _gamePid = pid;
        _calibration = calibration;
    }

    // Repositionne l'overlay sur la fenetre du jeu. Appele a intervalle regulier par l'appelant (le jeu peut bouger/resize).
    public bool FollowGameWindow()
    {
        var hwnd = GameTagger.FindMainWindowForOverlay((uint)_gamePid);
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return false;
        Position = new PixelPoint(rect.Left, rect.Top);
        Width = Math.Max(1, rect.Right - rect.Left);
        Height = Math.Max(1, rect.Bottom - rect.Top);
        return true;
    }

    public void ShowAdvice(int lineIndex, string tier, string label)
    {
        if (_calibration is null) { HideAdvice(); return; }
        var cell = ForgeOverlayLayout.CellFor(_calibration, lineIndex, tier);
        if (cell is null) { HideAdvice(); return; }
        AdviceFrame.IsVisible = true;
        AdviceFrame.Width = cell.Size;
        AdviceFrame.Height = cell.Size;
        Canvas.SetLeft(AdviceFrame, cell.X);
        Canvas.SetTop(AdviceFrame, cell.Y);
        AdviceText.Text = label;
        AdviceLabel.IsVisible = true;
        Canvas.SetLeft(AdviceLabel, cell.X);
        Canvas.SetTop(AdviceLabel, cell.Y - 28);
    }

    public void HideAdvice()
    {
        AdviceFrame.IsVisible = false;
        AdviceLabel.IsVisible = false;
    }

    public void StartCalibration()
    {
        _calibrating = true;
        _calibrationStep = 0;
        _baCell = _paCell = _raCell = _secondRowBaCell = null;
        SetClickThrough(false);
        ShowCalibrationPrompt();
    }

    private void ShowCalibrationPrompt()
    {
        CalibrationPromptText.Text = _calibrationSteps[_calibrationStep];
        CalibrationPrompt.IsVisible = true;
        Canvas.SetLeft(CalibrationPrompt, 20);
        Canvas.SetTop(CalibrationPrompt, 20);
    }

    private void OnCalibrationClick(object? sender, PointerPressedEventArgs e)
    {
        if (!_calibrating) return;
        var point = e.GetPosition(OverlayCanvas);
        switch (_calibrationStep)
        {
            case 0: _baCell = point; break;
            case 1: _paCell = point; break;
            case 2: _raCell = point; break;
            case 3: _secondRowBaCell = point; break;
        }
        _calibrationStep++;
        if (_calibrationStep >= _calibrationSteps.Length) { FinishCalibration(); return; }
        ShowCalibrationPrompt();
    }

    private void FinishCalibration()
    {
        _calibrating = false;
        CalibrationPrompt.IsVisible = false;
        SetClickThrough(true);
        if (_baCell is not { } ba || _paCell is not { } pa || _raCell is not { } ra || _secondRowBaCell is not { } second) return;
        double columnSpacing = pa.X - ba.X;
        double rowHeight = second.Y - ba.Y;
        if (columnSpacing <= 0 || rowHeight <= 0) return;
        var calibration = new ForgeOverlayCalibration(ba.X, ba.Y, rowHeight, 3);
        ForgeOverlayCalibrationStore.Save(calibration);
        _calibration = calibration;
    }

    private void SetClickThrough(bool enabled)
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, enabled ? style | WsExTransparent : style & ~WsExTransparent);
    }
}
