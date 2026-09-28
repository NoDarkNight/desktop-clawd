using Avalonia;

namespace DesktopClawd;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // DesktopClawd --export-templates <dir>: write the placeholder art as editable PNG strips.
        if (args is ["--export-templates", var dir])
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            foreach (var (anim, path, frames) in SpriteLibrary.ExportTemplates(dir))
                Console.WriteLine($"{anim,-10} {frames} frame(s)  {path}");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
