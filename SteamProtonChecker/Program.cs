using Avalonia;

namespace SteamProtonChecker;

internal static class Program
{
    // Avalonia needs a plain, non-async Main so its own message loop can take over.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Also reused by Avalonia's design-time previewer in the IDE.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
