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

    private CancellationTokenSource? _monitorCts;
    private volatile bool _patchedThisSession;

    public MainForm()
    {
        _patcher = new MemoryPatcher(Log);
        BuildUi();
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
        try { Icon = new Icon("ck2_icon.ico"); } catch { /* optional */ }

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
    private void StartMonitor()
    {
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                bool running = MemoryPatcher.IsGameRunning(out _);

                if (!running)
                {
                    _patchedThisSession = false;
                    SetStatus("Waiting for CK2game.exe…", Warn);
                }
                else if (_patchedThisSession)
                {
                    SetStatus("Patched — play with mods in Ironman!", Ok);
                }
                else
                {
                    SetStatus("CK2 detected…", Warn);
                    if (_autoPatch.Checked)
                        ApplyOnce(manual: false);
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
            if (manual) SetStatus("CK2game.exe is not running.", Err);
            return;
        }

        SetStatus("Patching…", Warn);
        SetApplyEnabled(false);
        try
        {
            PatchResult r = _patcher.Apply();
            if (r.Success)
            {
                _patchedThisSession = true;
                SetStatus(r.Message.StartsWith("Already") ? "Already patched." : "Patched — play with mods in Ironman!", Ok);
                Log(r.Message);
            }
            else
            {
                // "signature not found" is normal while the game is still loading;
                // during auto mode we just retry on the next poll.
                if (manual) SetStatus(r.Message, Err);
                Log(r.Message);
            }
        }
        finally
        {
            SetApplyEnabled(true);
        }
    }

    // ---------------------------------------------------- thread-safe UI
    private void SetStatus(string text, Color color)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text, color)); return; }
        _status.Text = text;
        _status.ForeColor = color;
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _monitorCts?.Cancel();
        base.OnFormClosing(e);
    }

    // Subtle vertical gradient background for a less "flat gray" look.
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(28, 32, 56),
            Color.FromArgb(18, 20, 36),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
