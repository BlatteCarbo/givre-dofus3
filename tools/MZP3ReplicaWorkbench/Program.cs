using Avalonia;

namespace MZP3ReplicaWorkbench;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var app = AppBuilder.Configure<ReplicaApplication>()
            .UsePlatformDetect().LogToTrace();
#if DEBUG
        // The debug bridge attaches only to THIS independent replica app.
        // Never use it to attach to the installed/running GivreClone.
        app = app.WithDeveloperTools();
#endif
        app.StartWithClassicDesktopLifetime(args);
    }
}
