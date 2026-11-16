using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GivreClone.UiTests.TestApp))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace GivreClone.UiTests;

// Only resources and control themes: never Program.Main, production App startup,
// MainViewModel, SmithMagicViewModel, account stores, or transport services.
public sealed class TestApp : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
