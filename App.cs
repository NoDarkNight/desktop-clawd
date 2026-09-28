using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace DesktopClawd;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var sprites = new SpriteLibrary();
            var pet = new PetWindow(sprites) { Icon = sprites.CreateIcon() };
            desktop.MainWindow = pet;

            var listener = new ClaudeListener(activity => Dispatcher.UIThread.Post(() => pet.OnClaude(activity)));
            var listening = listener.Start();
            desktop.Exit += (_, _) => listener.Dispose();

            var tray = new TrayIcon
            {
                Icon = pet.Icon,
                ToolTipText = listening ? "Desktop Clawd" : "Desktop Clawd (Claude events unavailable: port in use)",
                Menu = BuildTrayMenu(desktop, pet, sprites),
            };
            TrayIcon.SetIcons(this, new TrayIcons { tray });

            pet.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static NativeMenu BuildTrayMenu(IClassicDesktopStyleApplicationLifetime desktop, PetWindow pet, SpriteLibrary sprites)
    {
        var reload = new NativeMenuItem("Reload sprites");
        reload.Click += (_, _) => pet.ReloadSprites();

        var openFolder = new NativeMenuItem("Open sprites folder");
        openFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(sprites.Directory);
            Process.Start(new ProcessStartInfo(sprites.Directory) { UseShellExecute = true });
        };

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var menu = new NativeMenu();
        menu.Items.Add(reload);
        menu.Items.Add(openFolder);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        return menu;
    }
}
