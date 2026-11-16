using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

namespace GivreClone.App.Services;

/// <summary>Copie du texte dans le presse-papier (API data-transfer Avalonia 12).</summary>
public static class ClipboardService
{
    public static async Task CopyAsync(string text)
    {
        var top = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var cb = top?.Clipboard;
        if (cb is null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await cb.SetDataAsync(data);
    }
}
