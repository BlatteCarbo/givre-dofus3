using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class CombatVisualStatusTests
{
    [AvaloniaFact]
    public void Enhanced_reservation_disables_standalone_controls_and_release_restores_them()
    {
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        var bot = (BotRowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotRowViewModel));
        typeof(BotRowViewModel).GetField("<Pid>k__BackingField", hidden)!.SetValue(bot, Environment.ProcessId);
        typeof(BotsViewModel).GetField("_profileBot", hidden)!.SetValue(vm, bot);
        typeof(BotsViewModel).GetField("_profile", hidden)!.SetValue(vm, new CombatProfile());
        typeof(BotsViewModel).GetField("_visualModulesAvailable", hidden)!.SetValue(vm, true);
        var refresh = typeof(BotsViewModel).GetMethod("RefreshLiveCombatVisualStatus", hidden)!;
        var reservations = (ConcurrentDictionary<int, byte>)typeof(DofusEnhanced)
            .GetField("Activations", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        bool previous = AppConfig.Current.ModsEnabled;
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var view = new BotsView();
        var diagnostic = view.GetLogicalDescendants().OfType<TextBlock>().Single(control =>
            AutomationProperties.GetName(control) == "Diagnostic des mods visuels de combat");
        var panel = Assert.IsType<StackPanel>(diagnostic.Parent);
        var card = Assert.IsType<Border>(panel.Parent);
        Assert.IsType<StackPanel>(card.Parent).Children.Remove(card);
        card.DataContext = vm;
        var window = new Window { Content = card, Width = 500, Height = 700 };
        try
        {
            AppConfig.Current.ModsEnabled = true;
            refresh.Invoke(vm, null);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.CanCombatVisualOptions);
            string available = vm.CombatVisualStatus;
            Assert.Equal(available, diagnostic.Text);
            Assert.True(reservations.TryAdd(Environment.ProcessId, 0));
            notifications.Clear();

            refresh.Invoke(vm, null);
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.CanCombatVisualOptions);
            Assert.False(vm.CanCombatInstantMove);
            Assert.False(vm.CanCombatCollapse);
            Assert.NotEqual(available, vm.CombatVisualStatus);
            Assert.Equal(vm.CombatVisualStatus, diagnostic.Text);
            Assert.True(diagnostic.IsEffectivelyVisible);
            Assert.All(panel.Children.OfType<CheckBox>().Take(2), control => Assert.False(control.IsEnabled));
            Assert.Contains(nameof(BotsViewModel.CombatVisualStatus), notifications);
            Assert.Contains(nameof(BotsViewModel.CanCombatVisualOptions), notifications);

            reservations.TryRemove(Environment.ProcessId, out _);
            refresh.Invoke(vm, null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.CanCombatVisualOptions);
            Assert.Equal(available, vm.CombatVisualStatus);
            Assert.Equal(available, diagnostic.Text);
            Assert.All(panel.Children.OfType<CheckBox>().Take(2), control => Assert.True(control.IsEnabled));
        }
        finally
        {
            window.Close();
            reservations.TryRemove(Environment.ProcessId, out _);
            AppConfig.Current.ModsEnabled = previous;
        }
    }

    [Fact]
    public void Live_refresh_notifies_changed_gates_without_rechecking_modules_or_repeating_notifications()
    {
        // No production constructor: no account stores, timers or transport startup.
        var vm = (BotsViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BotsViewModel));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(BotsViewModel).GetField("_profile", hidden)!.SetValue(vm, new CombatProfile());
        var modules = typeof(BotsViewModel).GetField("_visualModulesAvailable", hidden)!;
        modules.SetValue(vm, true);
        var refresh = typeof(BotsViewModel).GetMethod("RefreshLiveCombatVisualStatus", hidden)!;
        bool previous = AppConfig.Current.ModsEnabled;
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        try
        {
            AppConfig.Current.ModsEnabled = false;
            refresh.Invoke(vm, null);
            string disabled = vm.CombatVisualStatus;
            notifications.Clear();

            AppConfig.Current.ModsEnabled = true;
            refresh.Invoke(vm, null);

            Assert.NotEqual(disabled, vm.CombatVisualStatus);
            Assert.Contains(nameof(BotsViewModel.CombatVisualStatus), notifications);
            Assert.Contains(nameof(BotsViewModel.CanCombatVisualOptions), notifications);
            Assert.Contains(nameof(BotsViewModel.CanCombatInstantMove), notifications);
            Assert.Contains(nameof(BotsViewModel.CanCombatCollapse), notifications);
            Assert.Equal(true, modules.GetValue(vm));

            notifications.Clear();
            refresh.Invoke(vm, null);
            Assert.Empty(notifications);
        }
        finally { AppConfig.Current.ModsEnabled = previous; }
    }
}
