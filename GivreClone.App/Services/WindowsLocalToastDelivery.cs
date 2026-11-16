using System;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

public sealed class WindowsLocalToastDelivery : ILocalToastDelivery
{
    private readonly SemaphoreSlim _slot = new(1, 1);

    public static string ToastXml(LocalBotNotice notice) =>
        "<toast><visual><binding template=\"ToastGeneric\"><text>" + SecurityElement.Escape(notice.Title) +
        "</text><text>" + SecurityElement.Escape(notice.Body) + "</text></binding></visual><audio silent=\"true\"/></toast>";

    public Task<LocalToastResult> SendAsync(LocalBotNotice notice, CancellationToken cancellationToken)
        => RunAsync(notice, cancellationToken);

    public Task<LocalToastResult> CheckAvailabilityAsync(CancellationToken cancellationToken)
        => RunAsync(null, cancellationToken);

    internal static LocalToastResult DecodeExitCode(int exitCode)
        => exitCode is >= 21 and <= 24 ? new(LocalToastStatus.Suppressed, exitCode - 20)
            : new(exitCode switch { 0 => LocalToastStatus.Submitted, 10 => LocalToastStatus.Available, _ => LocalToastStatus.Failed });

    public static string PowerShellScript(LocalBotNotice? notice)
    {
        string script = "$ErrorActionPreference='Stop'; try { " +
            "[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] > $null; " +
            "$key='HKCU:\\Software\\Classes\\AppUserModelId\\GivreClone.Local'; " +
            "New-Item -Path $key -Force > $null; New-ItemProperty -Path $key -Name DisplayName -Value 'GivreClone' -PropertyType String -Force > $null; " +
            "$manager=[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('GivreClone.Local'); " +
            "if ([int]$manager.Setting -ne 0) { exit (20 + [int]$manager.Setting) }; ";
        if (notice is null) return script + "exit 10 } catch { exit 2 }";
        // Only base64 data is interpolated; event text is never executable PowerShell.
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(ToastXml(notice)));
        return script +
            "[Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom.XmlDocument,ContentType=WindowsRuntime] > $null; " +
            "$xml=New-Object Windows.Data.Xml.Dom.XmlDocument; " +
            "$xml.LoadXml([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + payload + "'))); " +
            "$toast=[Windows.UI.Notifications.ToastNotification]::new($xml); $manager.Show($toast); exit 0 " +
            "} catch { exit 2 }";
    }

    private async Task<LocalToastResult> RunAsync(LocalBotNotice? notice, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(LocalToastStatus.Unsupported);
        if (!await _slot.WaitAsync(0, cancellationToken)) return new(LocalToastStatus.Busy);
        try
        {
            string script = PowerShellScript(notice);
            var start = new ProcessStartInfo
            {
                FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start);
            if (process is null) return new(LocalToastStatus.Failed);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                return new(LocalToastStatus.TimedOut);
            }
            return DecodeExitCode(process.ExitCode);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        { return new(LocalToastStatus.Failed); }
        finally { _slot.Release(); }
    }
}
