using System.Diagnostics;
using System.Windows;

namespace SCS;

public partial class App : Application
{
    private static Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // One instance only: two copies would both hook the keyboard and double-fire every stratagem.
        _single = new Mutex(true, "SCS.StratagemControlSystem.v2", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("S.C.S is already running.", "S.C.S", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Keep the UI process responsive while a game saturates the CPU. The input threads
        // additionally run at Highest + MMCSS "Games" priority.
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }

        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            var mainUp = MainWindow is { IsLoaded: true, IsVisible: true };
            MessageBox.Show(Describe(args.Exception), "S.C.S error", MessageBoxButton.OK, MessageBoxImage.Error);
            // If the main window never came up, don't leave a headless process holding the keyboard hook.
            if (!mainUp) Shutdown(1);
        };
        base.OnStartup(e);

        try
        {
            MainWindow = new UI.MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("S.C.S failed to start:\n\n" + Describe(ex), "S.C.S error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Innermost exception first, so the useful message isn't hidden behind a wrapper.</summary>
    private static string Describe(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner == ex ? ex.Message : $"{inner.Message}\n\n({ex.GetType().Name}: {ex.Message})";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _single?.ReleaseMutex(); } catch { }
        _single?.Dispose();
        base.OnExit(e);
    }
}
