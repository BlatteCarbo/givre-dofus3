extern alias F06App;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using S = F06App::GivreClone.App.Services;
using V = F06App::GivreClone.App.ViewModels;
using Xunit;

namespace GivreClone.Tests;

public sealed class NotificationFilterTests
{
    [Fact]
    public void Mark_read_with_a_filter_acknowledges_only_visible_rows()
    {
        var vm = (V.NotificationCenterViewModel)RuntimeHelpers.GetUninitializedObject(typeof(V.NotificationCenterViewModel));
        var shown = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Error, Title = "a" });
        var hidden = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Info, Title = "b" });
        typeof(V.NotificationCenterViewModel).GetField("<Items>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new ObservableCollection<V.NotifRow> { shown, hidden });
        vm.IncidentsOnly = true;
        Assert.Equal("Marquer les visibles lus", vm.MarkReadLabel);
        vm.MarkAllReadCommand.Execute(null);
        Assert.True(shown.Read);
        Assert.False(hidden.Read);
        vm.IncidentsOnly = false;
        Assert.Equal("Tout marquer lu", vm.MarkReadLabel);
    }

    [Fact]
    public void Severity_first_puts_errors_then_warnings_ahead_and_keeps_recency_inside_groups()
    {
        var vm = (V.NotificationCenterViewModel)RuntimeHelpers.GetUninitializedObject(typeof(V.NotificationCenterViewModel));
        var info = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Info, Title = "i" });
        var warnNew = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Warning, Title = "w1" });
        var errNew = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Error, Title = "e1" });
        var warnOld = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Warning, Title = "w2" });
        var errOld = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Error, Title = "e2" });
        var items = new ObservableCollection<V.NotifRow> { info, warnNew, errNew, warnOld, errOld }; // récent -> ancien
        typeof(V.NotificationCenterViewModel).GetField("<Items>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, items);

        Assert.Equal(items, vm.VisibleItems);
        vm.SeverityFirst = true;
        Assert.Equal(new[] { errNew, errOld, warnNew, warnOld, info }, vm.VisibleItems);
        vm.IncidentsOnly = true;
        Assert.Equal(new[] { errNew, errOld, warnNew, warnOld }, vm.VisibleItems);
        Assert.Equal(new[] { "Erreur", "Alerte", "" }, new[] { errNew.SeverityText, warnNew.SeverityText, info.SeverityText });
        Assert.Equal(5, vm.Items.Count);
    }

    [Fact]
    public void Copy_text_keeps_the_whole_body_source_and_zoned_time()
    {
        string body = new string('x', 500);
        var row = new V.NotifRow(new S.NotifItem { Title = "Titre", Source = "alpha", Body = body, Ts = 0 });
        var parts = row.CopyText.Split('\n');
        Assert.Equal("Titre", parts[0]);
        Assert.Equal("alpha", parts[1]);
        Assert.Contains("UTC", parts[2]);
        Assert.Equal(body, parts[3]);
        Assert.DoesNotContain("\n\n", new V.NotifRow(new S.NotifItem { Title = "Seul" }).CopyText);
    }

    [Fact]
    public void Filters_intersect_without_acknowledging_or_removing_history()
    {
        var vm = (V.NotificationCenterViewModel)RuntimeHelpers.GetUninitializedObject(typeof(V.NotificationCenterViewModel));
        var warning = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Warning, Title = "Stock", Body = "Insuffisant", Source = "alpha" });
        var acknowledged = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Error, Read = true, Source = "beta" });
        var info = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Info });
        var items = new ObservableCollection<V.NotifRow> { warning, acknowledged, info };
        typeof(V.NotificationCenterViewModel).GetField("<Items>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, items);

        vm.IncidentsOnly = true;
        Assert.Equal(new[] { warning, acknowledged }, vm.VisibleItems);
        vm.UnreadOnly = true;

        Assert.Same(warning, Assert.Single(vm.VisibleItems));
        vm.NotificationQuery = " INSUFFISANT ";
        vm.SourceQuery = "ALPHA";
        Assert.Same(warning, Assert.Single(vm.VisibleItems));
        vm.SourceQuery = "beta";
        Assert.Empty(vm.VisibleItems);
        vm.SourceQuery = "";
        vm.NotificationQuery = "";
        Assert.Equal(3, vm.Items.Count);
        Assert.False(warning.Read);
        Assert.False(info.Read);
        Assert.True(acknowledged.Read);
        vm.IncidentsOnly = false;
        vm.UnreadOnly = false;
        Assert.Equal(3, vm.VisibleItems.Count);
    }

    [Fact]
    public void Badge_counts_visible_unread_under_filters_and_drops_when_visible_rows_are_acknowledged()
    {
        var vm = (V.NotificationCenterViewModel)RuntimeHelpers.GetUninitializedObject(typeof(V.NotificationCenterViewModel));
        var err1 = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Error, Title = "e1" });
        var err2 = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Error, Title = "e2" });
        var info = new V.NotifRow(new S.NotifItem { Kind = S.NotifKind.Info, Title = "i" });
        typeof(V.NotificationCenterViewModel).GetField("<Items>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, new ObservableCollection<V.NotifRow> { err1, info, err2 });
        vm.UnreadCount = 3;
        Assert.Equal(3, vm.BadgeCount);

        vm.IncidentsOnly = true;
        Assert.Equal(2, vm.BadgeCount);
        Assert.Equal("2", vm.UnreadBadge);

        vm.NotificationQuery = "e1";
        Assert.Equal(1, vm.BadgeCount);
        vm.NotificationQuery = "";

        vm.AcknowledgeVisibleCommand.Execute(null);
        Assert.True(err1.Read);
        Assert.True(err2.Read);
        Assert.Equal(0, vm.BadgeCount);
        Assert.False(vm.HasUnread);
        Assert.False(info.Read);
    }
}
