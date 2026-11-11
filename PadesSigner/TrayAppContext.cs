using System.Diagnostics;

namespace PadesSigner;

/// <summary>Windowless application: a tray icon plus the folder worker. Reloads itself when appsettings.json changes.</summary>
public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly SynchronizationContext _ui;
    private readonly FileSystemWatcher _settingsWatcher;
    private readonly System.Threading.Timer _reloadDebounce;

    private Settings _settings = new();
    private FolderSigningWorker? _worker;
    private PdfSigningService? _signer;
    private int _signedCount;
    private string? _lastConfigError;

    public TrayAppContext()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };
        _autostartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutostart());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open input folder", null, (_, _) => OpenFolder(_settings.InputFolder));
        menu.Items.Add("Open output folder", null, (_, _) => OpenFolder(_settings.OutputFolder));
        menu.Items.Add("Open error folder", null, (_, _) => OpenFolder(_settings.ErrorFolder));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Choose certificate...", null, (_, _) => CertificatePicker.ChooseAndSave(_settings.Certificate.Location));
        menu.Items.Add("Edit settings", null, (_, _) => Shell("notepad.exe", Settings.FilePath));
        menu.Items.Add("Reload settings", null, (_, _) => Reload());
        menu.Items.Add("Open log", null, (_, _) => { if (File.Exists(Log.CurrentFile)) Shell(Log.CurrentFile); else OpenFolder(Log.Directory); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => _autostartItem.Checked = Autostart.IsEnabled;

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "PAdES Signer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenFolder(_settings.OutputFolder);

        // Reload automatically when the settings file is edited (e.g. once the certificate path is filled in).
        _reloadDebounce = new System.Threading.Timer(_ => _ui.Post(_ => Reload(), null));
        _settingsWatcher = new FileSystemWatcher(Path.GetDirectoryName(Settings.FilePath)!, Path.GetFileName(Settings.FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler onChange = (_, _) => _reloadDebounce.Change(1000, Timeout.Infinite);
        _settingsWatcher.Changed += onChange;
        _settingsWatcher.Created += onChange;
        _settingsWatcher.Renamed += (s, e) => onChange(s, e);
        _settingsWatcher.EnableRaisingEvents = true;

        Reload();
    }

    private void Reload()
    {
        StopWorker();

        try
        {
            _settings = Settings.Load();
            var certificate = SigningCertificate.Load(_settings.Certificate);
            try
            {
                _signer = new PdfSigningService(certificate, _settings.Signature);
            }
            catch
            {
                // The signer owns the certificate only once it is constructed.
                certificate.Dispose();
                throw;
            }
            _worker = new FolderSigningWorker(_settings, _signer);
            _worker.FileSigned += (_, _) => _ui.Post(_ => { _signedCount++; UpdateStatus(); }, null);
            _worker.FileFailed += (name, ex) => _ui.Post(_ =>
                _tray.ShowBalloonTip(5000, "PAdES Signer", $"Signing {name} failed: {ex.Message}", ToolTipIcon.Error), null);
            _worker.Start();

            _lastConfigError = null;
            SetStatus(true, $"Running: {_signer.Description}");
        }
        catch (Exception ex)
        {
            StopWorker();
            Log.Error("Not signing: configuration is incomplete or invalid", ex);
            SetStatus(false, $"Not signing: {ex.Message}");

            // Only pop a balloon when the problem changes, not on every save of the settings file.
            if (ex.Message != _lastConfigError)
                _tray.ShowBalloonTip(5000, "PAdES Signer is not signing", ex.Message + "\nRight-click the tray icon > Edit settings.", ToolTipIcon.Warning);
            _lastConfigError = ex.Message;
        }
    }

    private void StopWorker()
    {
        _worker?.Dispose();
        _worker = null;
        _signer?.Dispose();
        _signer = null;
    }

    private bool _running;
    private string _statusText = "";

    private void SetStatus(bool running, string text)
    {
        _running = running;
        _statusText = text;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var text = _running && _signedCount > 0 ? $"{_statusText} ({_signedCount} signed)" : _statusText;
        _statusItem.Text = text;
        _tray.Icon = _running ? SystemIcons.Shield : SystemIcons.Warning;
        // NotifyIcon.Text is limited to 127 characters.
        var tip = "PAdES Signer - " + text;
        _tray.Text = tip.Length > 127 ? tip[..124] + "..." : tip;
    }

    private void ToggleAutostart()
    {
        try
        {
            if (Autostart.IsEnabled) Autostart.Disable(); else Autostart.Enable();
        }
        catch (Exception ex)
        {
            Log.Error("Changing autostart failed", ex);
            _tray.ShowBalloonTip(5000, "PAdES Signer", ex.Message, ToolTipIcon.Error);
        }
    }

    private static void OpenFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        Shell(folder);
    }

    private static void Shell(string fileName, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName) { Arguments = args ?? "", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {fileName}", ex);
        }
    }

    protected override void ExitThreadCore()
    {
        Log.Info("Exiting.");
        _settingsWatcher.Dispose();
        _reloadDebounce.Dispose();
        StopWorker();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
