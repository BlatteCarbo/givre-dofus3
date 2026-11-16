using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.Services;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using GivreClone.Engine.Recovery;
using Xunit;

namespace GivreClone.UiTests;

public sealed class SmithCrushingPickerTests
{
    [AvaloniaTheory]
    [InlineData(1440)]
    [InlineData(760)]
    public void Explicit_picker_binds_individual_quantities_and_renders_the_preview(int width)
    {
        var directory = Path.Combine(Path.GetTempPath(), "givre-crush-ui-" + Guid.NewGuid());
        var vm = new SmithMagicViewModel
        {
            CrushProvenance = new(Path.Combine(directory, "provenance.json")),
            CrushOwnerOf = _ => new("headless-crushing", 354, 123),
            CrushHistoryHeadOf = (_, _) => 0,
            CrushQuantityOf = (_, _) => 10,
            CrushGidOf = (_, _) => 30,
            BindingOf = (_, _) => ItemBind.None,
        };
        typeof(SmithMagicViewModel).GetField("_pid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, 101);
        var row = new BagPickRow { ObjectUid = 7, Gid = 30, Name = "Anneau de test", Quantity = 10,
            InBag = true, HasSlot = true, Level = 20, TypeName = "Anneau" };
        vm.Picks.Add(row);
        vm.PickCrushCommand.Execute(null);
        Assert.Empty(vm.FilteredPicks);
        var view = new SmithMagicView { DataContext = vm };
        var window = new Window { Width = width, Height = 900, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var compact = view.FindControl<TabItem>("CompactInventoryTab")!;
            if (width < 1040) compact.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            var host = view.FindControl<ContentControl>(width < 1040 ? "CompactInventoryHost" : "WideInventoryHost")!;
            var inventory = Assert.IsType<Border>(host.Content);
            var toggle = inventory.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => AutomationProperties.GetName(c) == "Inclure les autres équipements du sac");
            Assert.True(toggle.IsEffectivelyVisible);
            toggle.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IncludeOtherCrushItems);
            var quantity = inventory.GetVisualDescendants().OfType<NumericUpDown>().Single();
            Assert.Equal(10, quantity.Maximum);
            quantity.Value = 3;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, row.CrushQuantity);
            row.IsChecked = true;
            var add = inventory.GetVisualDescendants().OfType<Button>()
                .Single(c => c.Command == vm.AddCheckedToCrushCommand);
            add.Command!.Execute(null);
            Assert.Equal(3, Assert.Single(vm.CrushQueue).Qty);
            AssertInside(quantity, window);
            Save(window, $"smith-crush-picker-{width}.png");

            var tabs = inventory.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = 2;
            Dispatcher.UIThread.RunJobs();
            var start = inventory.GetVisualDescendants().OfType<Button>()
                .Single(c => c.Command == vm.StartCrushCommand);
            Assert.True(start.IsEnabled);
            AssertInside(start, window);
            Save(window, $"smith-crush-preview-{width}.png");
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void AssertInside(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible);
        var position = control.TranslatePoint(default, window);
        Assert.NotNull(position);
        Assert.InRange(position.Value.X, 0, window.ClientSize.Width);
        Assert.InRange(position.Value.Y, 0, window.ClientSize.Height);
        Assert.InRange(position.Value.X + control.Bounds.Width, 1, window.ClientSize.Width);
        Assert.InRange(position.Value.Y + control.Bounds.Height, 1, window.ClientSize.Height);
    }

    private static void Save(Window window, string filename)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, filename));
    }
}
