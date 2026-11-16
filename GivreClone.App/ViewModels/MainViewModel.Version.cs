using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] public partial bool VersionNoticeVisible { get; set; }
    [ObservableProperty] public partial string VersionNoticeText { get; set; } = "";

    // Manual recheck never suppresses a real error. The protocol details panel
    // is a separate, opt-in surface, not a second permanent banner.
    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RefreshLocalVersionAsync() => RefreshProtocolAuditAsync();
}
