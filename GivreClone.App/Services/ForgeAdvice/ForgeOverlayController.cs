using System;
using System.Linq;
using Avalonia.Threading;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;

namespace GivreClone.App.Services.ForgeAdvice;

public sealed class ForgeOverlayController : IDisposable
{
    private readonly SmithMagicViewModel _vm;
    private ForgeOverlayWindow? _window;
    private DispatcherTimer? _followTimer;

    public ForgeOverlayController(SmithMagicViewModel vm)
    {
        _vm = vm;
        _vm.InGameAdviceBridge = OnAdvice;
    }

    public void Start()
    {
        if (_window is not null) return;
        _window = new ForgeOverlayWindow();
        _window.Show();
        _window.AttachToGame(_vm.Pid, ForgeOverlayCalibrationStore.Load());
        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _followTimer.Tick += (_, _) => _window?.FollowGameWindow();
        _followTimer.Start();
    }

    public void Stop()
    {
        _followTimer?.Stop();
        _followTimer = null;
        _window?.Close();
        _window = null;
    }

    public void Calibrate() => _window?.StartCalibration();

    private void OnAdvice(ForgeAdviceRecord record)
    {
        if (_window is null) return;
        if (record.Status != ForgeAdviceStatus.Ready || record.RuneGid is not { } gid)
        {
            _window.HideAdvice();
            return;
        }
        var rune = Stores.Launch.RuneItemsByGid().GetValueOrDefault(gid);
        if (rune.Name is null or "") { _window.HideAdvice(); return; }
        int lineIndex = _vm.Lines.ToList().FindIndex(l => l.EffectId == rune.EffectId);
        if (lineIndex < 0) { _window.HideAdvice(); return; }
        _window.ShowAdvice(lineIndex, rune.Tier, rune.Name);
    }

    public void Dispose() => Stop();
}
