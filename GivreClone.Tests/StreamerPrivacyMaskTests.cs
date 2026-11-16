extern alias F06App;

using System.Collections.ObjectModel;
using Xunit;
using Account = GivreClone.Engine.Accounts.Account;
using AppConfig = F06App::GivreClone.App.Services.AppConfig;
using PrivacyMask = F06App::GivreClone.App.Services.PrivacyMask;
using Proxy = GivreClone.Engine.Proxies.ProxyEntry;
using Row = F06App::GivreClone.App.ViewModels.AccountRow;

namespace GivreClone.Tests;

public sealed class StreamerPrivacyMaskTests : IDisposable
{
    private readonly bool _saved = AppConfig.Current.StreamerPrivacy;
    public void Dispose() => AppConfig.Current.StreamerPrivacy = _saved;

    [Theory]
    [InlineData("jean.dupont@mail.fr", "je***@mail.fr")]
    [InlineData("a@mail.fr", "a***@mail.fr")]
    [InlineData("jeandupont", "je***")]
    [InlineData("jo", "jo***")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskLogin_masks_when_on(string? login, string expected)
        => Assert.Equal(expected, PrivacyMask.MaskLogin(login, true));

    [Theory]
    [InlineData("jean.dupont@mail.fr")]
    [InlineData("jeandupont")]
    [InlineData("")]
    public void MaskLogin_is_identity_when_off(string login)
        => Assert.Equal(login, PrivacyMask.MaskLogin(login, false));

    [Theory]
    [InlineData("82.64.12.200", "***.***.***.***")]
    [InlineData("82.64.12.200:1080", "***.***.***.***:1080")]
    [InlineData("proxy.example.net:8080", "***.***.***.***:8080")]
    [InlineData("proxy.example.net", "***.***.***.***")]
    [InlineData("2001:db8::1", "***.***.***.***")]
    [InlineData("[2001:db8::1]:443", "***.***.***.***:443")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskIp_masks_when_on(string? ip, string expected)
        => Assert.Equal(expected, PrivacyMask.MaskIp(ip, true));

    [Theory]
    [InlineData("82.64.12.200")]
    [InlineData("proxy.example.net:8080")]
    [InlineData("-")]
    public void MaskIp_is_identity_when_off(string ip)
        => Assert.Equal(ip, PrivacyMask.MaskIp(ip, false));

    [Fact]
    public void AccountRow_display_login_follows_the_setting_live_while_login_stays_raw()
    {
        AppConfig.Current.StreamerPrivacy = false;
        var row = new Row(new Account { Login = "jean.dupont@mail.fr" }, new ObservableCollection<Proxy>(), "");
        row.ExitIp = "82.64.12.200:1080";
        Assert.Equal("jean.dupont@mail.fr", row.DisplayLogin);
        Assert.Equal("82.64.12.200:1080", row.DisplayExitIp);

        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        AppConfig.Current.StreamerPrivacy = true;
        AppConfig.NotifyUiPreferencesChanged();

        Assert.Contains(nameof(Row.DisplayLogin), changed);
        Assert.Contains(nameof(Row.DisplayExitIp), changed);
        Assert.Equal("je***@mail.fr", row.DisplayLogin);
        Assert.Equal("***.***.***.***:1080", row.DisplayExitIp);
        Assert.Equal("jean.dupont@mail.fr", row.Login);
        Assert.Equal("jean.dupont@mail.fr", row.Model.Login);
        Assert.Equal("82.64.12.200:1080", row.ExitIp);

        changed.Clear();
        AppConfig.Current.StreamerPrivacy = false;
        AppConfig.NotifyUiPreferencesChanged();
        Assert.Contains(nameof(Row.DisplayLogin), changed);
        Assert.Equal("jean.dupont@mail.fr", row.DisplayLogin);
    }
}
