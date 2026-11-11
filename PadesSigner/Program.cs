namespace PadesSigner;

static class Program
{
    /// <summary>
    /// PadesSigner.exe                 run in the tray
    /// PadesSigner.exe --install       register to start with Windows (current user) and exit
    /// PadesSigner.exe --uninstall     remove the autostart entry and exit
    /// PadesSigner.exe --choose-cert   pick the signing certificate in the Windows dialog and exit
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var command = args.FirstOrDefault()?.TrimStart('-', '/').ToLowerInvariant();
        switch (command)
        {
            case "install":
                Autostart.Enable();
                return 0;
            case "uninstall":
                Autostart.Disable();
                return 0;
            case "choose-cert":
                CertificatePicker.ChooseAndSave(Settings.Load().Certificate.Location);
                return 0;
        }

        using var mutex = new Mutex(true, @"Local\PadesSigner.SingleInstance", out var isFirst);
        if (!isFirst) return 0;

        Application.ThreadException += (_, e) => Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log.Error("Unobserved task exception", e.Exception); e.SetObserved(); };

        Log.Info($"Starting PadesSigner {typeof(Program).Assembly.GetName().Version} from {AppContext.BaseDirectory}");
        Application.Run(new TrayAppContext());
        return 0;
    }
}
