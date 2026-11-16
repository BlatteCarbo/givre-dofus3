extern alias F06App;
using System.Reflection;
using AppConfig = F06App::GivreClone.App.Services.AppConfig;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class NativeProviderOwnershipTests
{
    [Fact]
    public async Task Failed_activation_releases_its_per_process_reservation()
    {
        var enhanced = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.DofusEnhanced")!;
        var active = enhanced.GetMethod("ActivationInProgress", BindingFlags.NonPublic | BindingFlags.Static)!;
        var activate = enhanced.GetMethod("ActivateAsync", BindingFlags.Public | BindingFlags.Static)!;
        const int pid = -171103;

        var attempt = (Task<string>)activate.Invoke(null, new object[] { pid })!;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await attempt);

        Assert.Equal(false, active.Invoke(null, new object[] { pid }));
    }

    [Fact]
    public void Remembered_enhanced_preference_does_not_claim_an_unloaded_client()
    {
        bool previous = AppConfig.Current.DofusEnhancedEnabled;
        try
        {
            AppConfig.Current.DofusEnhancedEnabled = true;
            var runtime = typeof(LaunchService).Assembly.GetType("GivreClone.App.Services.NativeTurboRuntime")!;
            var owns = runtime.GetMethod("EnhancedOwnsHooks", BindingFlags.NonPublic | BindingFlags.Static)!;

            var actual = owns.Invoke(null, new object[] { Environment.ProcessId });

            Assert.Equal(false, actual);
        }
        finally { AppConfig.Current.DofusEnhancedEnabled = previous; }
    }
}
