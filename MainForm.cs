using System.Drawing.Drawing2D;

namespace CK2MAP;

internal sealed class MainForm : Form
{
    // CK2-ish palette
    private static readonly Color BgDark   = Color.FromArgb(24, 28, 48);
    private static readonly Color BgPanel  = Color.FromArgb(34, 40, 68);
    private static readonly Color Accent   = Color.FromArgb(84, 86, 160);
    private static readonly Color AccentHi = Color.FromArgb(108, 110, 196);
    private static readonly Color Gold     = Color.FromArgb(224, 190, 96);
    private static readonly Color Ok       = Color.FromArgb(90, 190, 120);
    private static readonly Color Warn     = Color.FromArgb(224, 168, 72);
    private static readonly Color Err      = Color.FromArgb(214, 96, 96);

    private readonly Label _status = new();
    private readonly CheckBox _autoPatch = new();
    private readonly Button _applyBtn = new();
    private readonly TextBox _log = new();
    private readonly MemoryPatcher _patcher;
    private readonly NotifyIcon _tray = new();

    private CancellationTokenSource? _monitorCts;
    private volatile bool _patchedThisSession;
    private bool _trayHintShown;

    public MainForm()
    {
        _patcher = new MemoryPatcher(Log);
        BuildUi();
        SetupTray();
        StartMonitor();
    }

    // ---------------------------------------------------------------- UI
    private void BuildUi()
    {
        Text = "CK2 Modded Achievement Patcher";
        BackColor = BgDark;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(520, 620);
        MinimumSize = new Size(460, 520);
        StartPosition = FormStartPosition.CenterScreen;
        LoadEmbeddedIcon();

        var title = new Label
        {
            Text = "CRUSADER KINGS II",
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            ForeColor = Gold,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(0, 12, 0, 0),
        };

        var subtitle = new Label
        {
            Text = "Modded Achievement Patcher",
            Font = new Font("Segoe UI", 11f),
            ForeColor = Color.Gainsboro,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 26,
        };

        // Status banner
        _status.Text = "Waiting for CK2game.exe…";
        _status.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        _status.ForeColor = Warn;
        _status.BackColor = BgPanel;
        _status.TextAlign = ContentAlignment.MiddleCenter;
        _status.Dock = DockStyle.Top;
        _status.Height = 54;
        _status.Padding = new Padding(10);

        var instructions = new Label
        {
            Text = "1.  Launch Crusader Kings II.\n" +
                   "2.  This tool patches the game automatically once it loads.\n" +
                   "3.  Play with mods in Ironman and earn achievements.",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.Gainsboro,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(18, 12, 18, 6),
        };

        _autoPatch.Text = "  Auto-patch when CK2 is detected";
        _autoPatch.Checked = true;
        _autoPatch.ForeColor = Color.White;
        _autoPatch.Font = new Font("Segoe UI", 10f);
        _autoPatch.Dock = DockStyle.Top;
        _autoPatch.Height = 32;
        _autoPatch.Padding = new Padding(18, 0, 0, 0);
        _autoPatch.FlatStyle = FlatStyle.Flat;

        StyleButton(_applyBtn, "Apply Patch Now");
        _applyBtn.Dock = DockStyle.Top;
        _applyBtn.Height = 46;
        _applyBtn.Margin = new Padding(18);
        _applyBtn.Click += (_, _) => Task.Run(() => ApplyOnce(manual: true));

        var applyHost = new Panel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(18, 6, 18, 8) };
        applyHost.Controls.Add(_applyBtn);

        // Log
        var logLabel = new Label
        {
            Text = "Log",
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = Gold,
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(18, 2, 0, 0),
        };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.FromArgb(18, 20, 34);
        _log.ForeColor = Color.FromArgb(180, 200, 220);
        _log.BorderStyle = BorderStyle.FixedSingle;
        _log.Font = new Font("Consolas", 9f);
        _log.Dock = DockStyle.Fill;

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 0, 18, 14) };
        logHost.Controls.Add(_log);

        var footer = new Label
        {
            Text = "Patches memory only — closing the game reverts everything. Original tool by JihadiJackass.",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray,
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        // Add in reverse dock order (Fill last)
        Controls.Add(logHost);
        Controls.Add(logLabel);
        Controls.Add(applyHost);
        Controls.Add(_autoPatch);
        Controls.Add(instructions);
        Controls.Add(_status);
        Controls.Add(subtitle);
        Controls.Add(title);
        Controls.Add(footer);
    }

    private void StyleButton(Button b, string text)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        b.MouseEnter += (_, _) => b.BackColor = AccentHi;
        b.MouseLeave += (_, _) => b.BackColor = Accent;
    }

    // ---------------------------------------------------------- Monitor
    private int _handledPid;
    private int _autoAttempts;
    private const int MaxAutoAttempts = 12; // ~18s of retries while the game loads

    private void StartMonitor()
    {
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                bool running = MemoryPatcher.IsGameRunning(out int pid);

                if (!running)
                {
                    if (_handledPid != 0) LogDedup("CK2 closed. Waiting for it to start…");
                    _handledPid = 0;
                    _patchedThisSession = false;
                    _autoAttempts = 0;
                    SetStatus("Waiting for CK2game.exe…", Warn);
                }
                else
                {
                    if (pid != _handledPid)   // a fresh game instance
                    {
                        _handledPid = pid;
                        _patchedThisSession = false;
                        _autoAttempts = 0;
                        LogDedup($"CK2 detected (pid {pid}).");
                    }

                    if (_patchedThisSession)
                        SetStatus("Patched — play with mods in Ironman!", Ok);
                    else if (!_autoPatch.Checked)
                        SetStatus("CK2 detected. Click Apply Patch Now.", Warn);
                    else if (_autoAttempts < MaxAutoAttempts)
                    {
                        SetStatus("CK2 detected — patching…", Warn);
                        ApplyOnce(manual: false);
                    }
                    else
                        SetStatus("Could not patch automatically — click Apply Patch Now to retry.", Err);
                }

                try { await Task.Delay(1500, token); } catch { break; }
            }
        }, token);
    }

    /// <summary>Attempt one patch pass. Safe to call repeatedly.</summary>
    private void ApplyOnce(bool manual)
    {
        if (_patchedThisSession && !manual) return;

        if (!MemoryPatcher.IsGameRunning(out _))
        {
            if (manual) { SetStatus("CK2game.exe is not running.", Err); LogDedup("CK2game.exe is not running."); }
            return;
        }

        if (!manual) _autoAttempts++;

        SetApplyEnabled(false);
        try
        {
            PatchResult r = _patcher.Apply();
            if (r.Success)
            {
                _patchedThisSession = true;
                SetStatus(r.Message.StartsWith("Already") ? "Already patched." : "Patched — play with mods in Ironman!", Ok);
                LogDedup(r.Message);
            }
            else
            {
                // While the game is still loading this is expected; log it once
                // (LogDedup suppresses the repeats) instead of spamming.
                LogDedup(r.Message);
                if (manual) SetStatus(r.Message, Err);
            }
        }
        finally
        {
            SetApplyEnabled(true);
        }
    }

    // ---------------------------------------------------- thread-safe UI
    private string _lastStatusText = "";
    private string _lastLogLine = "";

    private void SetStatus(string text, Color color)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text, color)); return; }
        _status.ForeColor = color;
        if (_lastStatusText == text) return; // avoid redundant repaints
        _lastStatusText = text;
        _status.Text = text;
    }

    /// <summary>Log a line, suppressing immediate duplicates.</summary>
    private void LogDedup(string message)
    {
        if (message == _lastLogLine) return;
        _lastLogLine = message;
        Log(message);
    }

    private void SetApplyEnabled(bool enabled)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetApplyEnabled(enabled)); return; }
        _applyBtn.Enabled = enabled;
        _applyBtn.BackColor = enabled ? Accent : Color.FromArgb(60, 64, 92);
    }

    private void Log(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    // ------------------------------------------------- minimize to tray
    private void LoadEmbeddedIcon()
    {
        // The .ico is embedded in the assembly (see csproj), so this works
        // in a single-file publish with no loose file next to the exe.
        try
        {
            using var s = typeof(MainForm).Assembly
                .GetManifestResourceStream("ck2_icon.ico");
            if (s is not null)
                Icon = new Icon(s);
        }
        catch { /* fall back to the default app icon */ }
    }

    private void SetupTray()
    {
        _tray.Icon = Icon ?? SystemIcons.Application;
        _tray.Text = "CK2 Modded Achievement Patcher";
        _tray.Visible = false;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => { _allowExit = true; _tray.Visible = false; Close(); });
        _tray.ContextMenuStrip = menu;

        _tray.DoubleClick += (_, _) => RestoreFromTray();

        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                MinimizeToTray();
        };
    }

    private void MinimizeToTray()
    {
        Hide();                 // removes the taskbar button
        _tray.Visible = true;

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.BalloonTipTitle = "Still running";
            _tray.BalloonTipText = "CK2-MAP is down here and will keep patching the game. Right-click to exit.";
            _tray.ShowBalloonTip(2000);
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        _tray.Visible = false;
        Activate();
    }

    private bool _allowExit;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Clicking the [X] sends the app to the tray instead of exiting, so it
        // keeps watching for and patching the game in the background. A real
        // exit comes from the tray's "Exit" item (which sets _allowExit).
        if (e.CloseReason == CloseReason.UserClosing && !_allowExit)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        _monitorCts?.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }

    // Subtle vertical gradient background for a less "flat gray" look.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
            return; // client area not laid out yet (startup/minimize)

        using var brush = new LinearGradientBrush(
            rect,
            Color.FromArgb(28, 32, 56),
            Color.FromArgb(18, 20, 36),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, rect);
    }
}
