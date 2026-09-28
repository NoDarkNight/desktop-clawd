using Avalonia;

namespace DesktopClawd;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        switch (args)
        {
            // DesktopClawd --export-templates <dir>: write the placeholder art as editable PNGs.
            case ["--export-templates", var dir]:
                BuildAvaloniaApp().SetupWithoutStarting();
                foreach (var (name, path, frames) in SpriteLibrary.ExportTemplates(dir))
                    Console.WriteLine($"{name,-12} {frames} frame(s)  {path}");
                return;

            // DesktopClawd --list-surfaces: show the window ledges the pet can stand on.
            case ["--list-surfaces"] when OperatingSystem.IsWindows():
                BuildAvaloniaApp().SetupWithoutStarting(); // makes the process per-monitor DPI aware
                foreach (var line in new WindowsDesktop().Describe())
                    Console.WriteLine(line);
                return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
