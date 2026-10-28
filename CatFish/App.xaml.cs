using System.Windows;
using CatFish.Core;
using CatFish.UI;

namespace CatFish;

public partial class App : Application
{
    private SingleInstanceService? _instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new SingleInstanceService();
        if (!_instance.IsPrimary)
        {
            if (!await _instance.NotifyExistingInstanceAsync())
                MessageBox.Show("CatFish 已在运行，请稍后重试或从托盘打开。", "CatFish", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        SettingsStore.MigrateLegacySettings();
        AppController.InitializeSettings();
        var window = new MainWindow();
        MainWindow = window;
        _instance.ActivationRequested += () =>
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(new Action(window.RestoreFromTray));
        };
        _instance.StartListening();
        window.Show();
        SessionEnding += (_, _) => window.ExitRequested = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instance?.IsPrimary == true) AppController.Shutdown();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

