extern alias F06App;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using Xunit;
using AppServices = F06App::GivreClone.App.Services;
using AppEco = F06App::GivreClone.App.Services.Eco;
using Vm = F06App::GivreClone.App.ViewModels;

namespace GivreClone.Tests;

[CollectionDefinition("Dashboard static stores", DisableParallelization = true)]
public sealed class DashboardStoreCollection;

[Collection("Dashboard static stores")]
public sealed class DashboardUxTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void Set(object owner, string field, object value)
        => owner.GetType().GetField(field, Hidden)!.SetValue(owner, value);

    private static Vm.DashboardViewModel Dashboard(params (string Login, bool Running)[] accounts)
    {
        var bots = Empty<Vm.BotsViewModel>();
        var rows = new ObservableCollection<Vm.BotRowViewModel>();
        foreach (var (login, running) in accounts)
        {
            var bot = Empty<Vm.BotRowViewModel>();
            var row = new Vm.AccountRow(new Account { Login = login }, new ObservableCollection<ProxyEntry>(), "");
            row.Running = running;
            Set(bot, "<Row>k__BackingField", row);
            rows.Add(bot);
        }
        Set(bots, "<Bots>k__BackingField", rows);
        var dashboard = Empty<Vm.DashboardViewModel>();
        Set(dashboard, "_bots", bots);
        return dashboard;
    }

    private static void RefreshSubscription(Vm.DashboardViewModel dashboard)
        => typeof(Vm.DashboardViewModel).GetMethod("RefreshSubscription", Hidden)!.Invoke(dashboard, null);

    [Fact]
    public void Active_account_expiry_takes_precedence_over_an_expired_saved_account()
    {
        AppServices.SubscriptionStore.Locked(book =>
        {
            var saved = book.Entries;
            try
            {
                var expired = new AppEco.SubEntry { Login = "saved@example.test", EndUtc = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
                var active = new AppEco.SubEntry { Login = "active@example.test", EndUtc = new DateTime(2100, 11, 25, 0, 0, 0, DateTimeKind.Utc) };
                book.Entries = new() { expired, active };
                var dashboard = Dashboard(("ACTIVE@example.test", true), (expired.Login, false));
                RefreshSubscription(dashboard);
                Assert.Contains(active.Login, dashboard.SubscriptionDetail);
                Assert.DoesNotContain(expired.Login, dashboard.SubscriptionDetail);
                Assert.False(dashboard.SubscriptionUrgent);

                RefreshSubscription(dashboard = Dashboard((expired.Login, false)));
                Assert.Contains(expired.Login, dashboard.SubscriptionDetail);
                Assert.True(dashboard.SubscriptionUrgent);

                RefreshSubscription(dashboard = Dashboard(("unknown@example.test", true)));
                Assert.DoesNotContain(expired.Login, dashboard.SubscriptionDetail);
                Assert.DoesNotContain(active.Login, dashboard.SubscriptionDetail);
                Assert.False(dashboard.SubscriptionUrgent);
            }
            finally { book.Entries = saved; }
            return 0;
        });
    }

    [Theory]
    [InlineData(false, AppServices.NotifKind.Info)]
    [InlineData(true, AppServices.NotifKind.Success)]
    public void Toast_tone_does_not_create_an_incident(bool success, AppServices.NotifKind expected)
    {
        var sink = AppServices.Notifier.Sink;
        var notices = new List<AppServices.NotifKind>();
        var shown = new List<(string Message, bool Success, bool Sound)>();
        void OnToast(string message, bool ok, bool sound) => shown.Add((message, ok, sound));
        try
        {
            AppServices.Notifier.Sink = (kind, _, _, _) => notices.Add(kind);
            AppServices.Toasts.Requested += OnToast;
            AppServices.Toasts.Show("validation", success, sound: false);
            Assert.Equal(("validation", success, false), Assert.Single(shown));
            Assert.Equal(expected, Assert.Single(notices));
            AppServices.Notifier.Push(AppServices.NotifKind.Error, "incident");
            Assert.Equal(AppServices.NotifKind.Error, notices[1]);
        }
        finally
        {
            AppServices.Toasts.Requested -= OnToast;
            AppServices.Notifier.Sink = sink;
        }
    }

    [Fact]
    public void Opening_notification_list_preserves_unread_incidents_and_read_history()
    {
        var notifications = Empty<Vm.NotificationCenterViewModel>();
        var items = new ObservableCollection<Vm.NotifRow>
        {
            new(new AppServices.NotifItem { Kind = AppServices.NotifKind.Error, Title = "error" }),
            new(new AppServices.NotifItem { Kind = AppServices.NotifKind.Warning, Title = "warning" }),
            new(new AppServices.NotifItem { Kind = AppServices.NotifKind.Info, Title = "info" }),
            new(new AppServices.NotifItem { Kind = AppServices.NotifKind.Error, Title = "history", Read = true })
        };
        Set(notifications, "<Items>k__BackingField", items);
        notifications.UnreadCount = 3;

        notifications.ToggleOpenCommand.Execute(null);
        Assert.True(notifications.IsOpen);
        Assert.Equal(3, notifications.UnreadCount);
        Assert.True(notifications.HasUnread);
        Assert.Equal("3", notifications.UnreadBadge);
        Assert.Equal(new[] { false, false, false, true }, items.Select(i => i.Read));
        Assert.Equal(new[] { false, false, false, true }, items.Select(i => i.Model.Read));

        notifications.CloseOpenCommand.Execute(null);
        Assert.False(notifications.IsOpen);
        notifications.IsOpen = true;
        Assert.Equal(4, notifications.Items.Count);
        Assert.Equal(new[] { false, false, false, true }, items.Select(i => i.Read));
        Assert.Equal(new[] { false, false, false, true }, items.Select(i => i.Model.Read));
        Assert.Equal(3, notifications.UnreadCount);
    }

    [Fact]
    public void Attention_keeps_unread_incidents_but_not_validation_toasts()
    {
        var dashboard = Dashboard();
        var bots = (Vm.BotsViewModel)typeof(Vm.DashboardViewModel).GetField("_bots", Hidden)!.GetValue(dashboard)!;
        Set(bots, "<Supervisors>k__BackingField", new ObservableCollection<Vm.SupervisorViewModel>());
        var notifications = Empty<Vm.NotificationCenterViewModel>();
        var items = new ObservableCollection<Vm.NotifRow>();
        Set(notifications, "<Items>k__BackingField", items);
        Set(dashboard, "_notifications", notifications);
        Set(dashboard, "<VisibleBots>k__BackingField", new ObservableCollection<Vm.BotRowViewModel>());
        Set(dashboard, "<Alerts>k__BackingField", new ObservableCollection<Vm.DashboardAlertRow>());
        var sink = AppServices.Notifier.Sink;
        try
        {
            AppServices.Notifier.Sink = (kind, title, body, source) =>
                items.Add(new Vm.NotifRow(new AppServices.NotifItem
                {
                    Kind = kind, Title = title, Body = body, Source = source
                }));
            AppServices.Toasts.Show("validation", success: false);
            AppServices.Notifier.Push(AppServices.NotifKind.Error, "incident");
            AppServices.Notifier.Push(AppServices.NotifKind.Warning, "warning");
            AppServices.Notifier.Push(AppServices.NotifKind.Error, "read");
            items[^1].Read = true;
            notifications.ToggleOpenCommand.Execute(null);
            dashboard.RefreshSnapshotCommand.Execute(null);
            Assert.Equal(2, dashboard.ProblemCount);
            Assert.Equal(new[] { "incident", "warning" }, dashboard.Alerts.Select(a => a.Title));
        }
        finally { AppServices.Notifier.Sink = sink; }
    }

    [Fact]
    public void Sidebar_destinations_are_named_and_tools_follow_the_menu_order()
    {
        const string path = "GivreClone.App/Views/MainWindow.axaml";
        var root = MappingScan.WalkUp(d => File.Exists(Path.Combine(d.FullName, path)))!;
        var document = XDocument.Load(Path.Combine(root.FullName, path));
        XNamespace ui = "https://github.com/avaloniaui";
        var sidebar = document.Descendants(ui + "Border").Single(e => (string?)e.Attribute("Name") == "Sidebar");
        var buttons = sidebar.Descendants(ui + "Button").ToArray();
        Assert.All(buttons, button =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("ToolTip.Tip")));
            Assert.False(string.IsNullOrWhiteSpace((string?)button.Attribute("AutomationProperties.Name")));
            Assert.Contains(button.Descendants(ui + "TextBlock"), text => (string?)text.Attribute("Classes") == "navlabel");
        });
        var names = buttons.Select(b => (string?)b.Attribute("Name")).ToList();
        string[] ordered = ["NavProfessionPlannerBtn", "NavItemCraftBtn", "NavWorldAtlasBtn",
            "NavVisualModsBtn", "NavEcoToolsBtn", "NavProxysBtn", "NavAdaptersBtn", "MZP0NavDiagnosticsBtn"];
        Assert.All(ordered, name => Assert.Contains(name, names));
        Assert.Equal(ordered, names.Where(n => ordered.Contains(n)));
    }
}
