using Avalonia;

namespace MZP1OperatorShell;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
        => AppBuilder.Configure<ShellApp>().UsePlatformDetect().LogToTrace()
            .StartWithClassicDesktopLifetime(args);
}
