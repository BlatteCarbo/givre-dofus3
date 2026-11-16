using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GivreClone.App.ViewModels;
using GivreClone.App.Views;
using Xunit;

namespace GivreClone.UiTests;

public sealed class DashboardUxTests
{
    [AvaloniaTheory]
    [InlineData(1460, 920)]
    [InlineData(960, 600)]
    public void Subscription_card_keeps_the_expiry_and_account_scope_visible(int width, int height)
    {
        var vm = (DashboardViewModel)RuntimeHelpers.GetUninitializedObject(typeof(DashboardViewModel));
        vm.SubscriptionHeadline = "expiré";
        vm.SubscriptionDetail = "Comptes mémorisés · échéance la plus proche · compte@example.test · Templarbot · 30/10/2026 12:00";
        vm.SubscriptionUrgent = true;
        var view = new DashboardView { DataContext = vm };
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            foreach (var text in view.GetVisualDescendants().OfType<TextBlock>().Where(t =>
                t.Text == vm.SubscriptionHeadline || t.Text == vm.SubscriptionDetail))
            {
                Assert.True(text.IsEffectivelyVisible);
                Assert.True(text.Bounds.Height > 0);
                var point = text.TranslatePoint(default, window)!.Value;
                Assert.InRange(point.X, 0, window.ClientSize.Width - text.Bounds.Width);
                Assert.InRange(point.Y, 0, window.ClientSize.Height - text.Bounds.Height);
            }
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(directory);
            frame.Save(Path.Combine(directory, $"dashboard-subscription-{width}.png"));
        }
        finally { window.Close(); }
    }
}
