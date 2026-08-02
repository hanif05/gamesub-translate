namespace GameSubTranslate.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // Headless self-checks: run checks then exit before any window is shown.
        if (e.Args.Length > 0 && e.Args[0].StartsWith("--selfcheck"))
        {
            Shutdown(SelfChecks.Run(e.Args[0]));
            return;
        }

        var main = new MainWindow();
        main.Show();
    }
}
