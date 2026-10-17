using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CoffeeCup
{
    /// <summary>The tray icon, its menu, and the keep-awake session.</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private static readonly int[] DurationChoices = { 0, 15, 30, 60, 120, 240, 480 };

        private const int FastInterval = 40;   // ms between frames while coffee pours or drains
        private const int SlowInterval = 300;  // ms between steam frames
        private const float PourStep = 1f / 22; // fraction of a full mug per fast frame (~1 s to fill)
        private static readonly TimeSpan NudgeAfterIdle = TimeSpan.FromSeconds(30);

        private readonly Settings _settings = Settings.Load();
        private readonly NotifyIcon _notify;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _toggleItem;
        private readonly ToolStripMenuItem _durationMenu;
        private readonly ToolStripMenuItem _keepDisplayItem;
        private readonly ToolStripMenuItem _startWithWindowsItem;
        private readonly ToolStripMenuItem _startActiveItem;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly System.Windows.Forms.Timer _keepAliveTimer;
        private readonly MugRenderer _renderer;
        private readonly Control _invoker;
        private readonly EventWaitHandle _exitEvent;
        private readonly EventWaitHandle _showEvent;
        private readonly RegisteredWaitHandle _exitWait;
        private readonly RegisteredWaitHandle _showWait;

        private bool _active;
        private DateTime? _sessionEndUtc;   // null while active means "indefinitely"
        private TimeSpan _sessionLength;
        private float _shownLevel;          // 0..1, animates towards the target level
        private Icon _currentIcon;
        private int _iconSize;
        private bool _lightTaskbar;

        public TrayApp()
        {
            _invoker = new Control();
            _ = _invoker.Handle; // create the handle so BeginInvoke works from other threads

            _iconSize = SystemInformation.SmallIconSize.Width;
            _lightTaskbar = Settings.TaskbarIsLight;
            _renderer = new MugRenderer(_iconSize, _lightTaskbar);

            _toggleItem = new ToolStripMenuItem("Stay awake", null, (s, e) => Toggle());
            _toggleItem.Font = new Font(_toggleItem.Font, FontStyle.Bold);

            _durationMenu = new ToolStripMenuItem("Stay awake for");
            foreach (int minutes in DurationChoices)
            {
                int m = minutes;
                var item = new ToolStripMenuItem(DescribeDuration(m), null, (s, e) => ChooseDuration(m))
                {
                    Tag = m,
                    ToolTipText = "Starts now. Double-clicking the mug uses this length too.",
                };
                _durationMenu.DropDownItems.Add(item);
            }

            _keepDisplayItem = new ToolStripMenuItem("Keep screen on", null, (s, e) => SetKeepDisplayOn(!_settings.KeepDisplayOn));
            _startWithWindowsItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleStartWithWindows());
            _startActiveItem = new ToolStripMenuItem("Stay awake when CoffeeCup starts", null, (s, e) =>
            {
                _settings.StartActive = !_settings.StartActive;
                _settings.Save();
            });

            _menu = new ContextMenuStrip();
            _menu.Items.AddRange(new ToolStripItem[]
            {
                _toggleItem,
                _durationMenu,
                new ToolStripSeparator(),
                _keepDisplayItem,
                _startWithWindowsItem,
                _startActiveItem,
                new ToolStripSeparator(),
                new ToolStripMenuItem("Exit", null, (s, e) => Exit()),
            });
            _menu.Opening += (s, e) => RefreshMenu();

            _notify = new NotifyIcon { ContextMenuStrip = _menu };
            _notify.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Toggle();
            };

            _timer = new System.Windows.Forms.Timer { Interval = SlowInterval };
            _timer.Tick += (s, e) => Tick();

            _keepAliveTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            _keepAliveTimer.Tick += (s, e) => KeepAlive();

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // Other launches of CoffeeCup.exe talk to this one through these events.
            _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
            _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (o, t) => OnUiThread(Exit), null, -1, false);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (o, t) => OnUiThread(ShowWhereIAm), null, -1, false);

            UpdateIcon();
            _notify.Visible = true;

            if (_settings.StartActive)
                Activate(_settings.DefaultMinutes);

            if (!_settings.WelcomeShown)
            {
                _settings.WelcomeShown = true;
                _settings.Save();
                ShowWhereIAm();
            }
        }

        // ---- Session ---------------------------------------------------------------

        private void Toggle()
        {
            if (_active) Deactivate();
            else Activate(_settings.DefaultMinutes);
        }

        private void ChooseDuration(int minutes)
        {
            _settings.DefaultMinutes = minutes;
            _settings.Save();
            Activate(minutes);
        }

        private void Activate(int minutes)
        {
            _active = true;
            if (minutes > 0)
            {
                _sessionLength = TimeSpan.FromMinutes(minutes);
                _sessionEndUtc = DateTime.UtcNow + _sessionLength;
            }
            else
            {
                _sessionEndUtc = null;
            }
            Power.KeepAwake(_settings.KeepDisplayOn);
            _keepAliveTimer.Start();
            StartAnimating();
        }

        private void Deactivate()
        {
            _active = false;
            _sessionEndUtc = null;
            _keepAliveTimer.Stop();
            Power.AllowSleep();
            StartAnimating();
        }

        private void SetKeepDisplayOn(bool value)
        {
            _settings.KeepDisplayOn = value;
            _settings.Save();
            if (_active) Power.KeepAwake(value);
            UpdateTooltip();
        }

        private void ToggleStartWithWindows()
        {
            try
            {
                Settings.StartWithWindows = !Settings.StartWithWindows;
            }
            catch (Exception ex)
            {
                _notify.ShowBalloonTip(4000, "CoffeeCup", "Couldn't change the startup setting: " + ex.Message, ToolTipIcon.Warning);
            }
        }

        /// <summary>Runs every 15 s while awake: renews the power request and, once the
        /// user has been idle for a while, simulates a key press so Windows sees activity.</summary>
        private void KeepAlive()
        {
            if (!_active) return;
            Power.KeepAwake(_settings.KeepDisplayOn);
            // A key press also wakes the display, so only use it when the screen should stay on.
            if (_settings.KeepDisplayOn && Power.IdleTime >= NudgeAfterIdle)
                Power.SimulateActivity();
        }

        private TimeSpan? Remaining =>
            _active && _sessionEndUtc.HasValue ? _sessionEndUtc.Value - DateTime.UtcNow : (TimeSpan?)null;

        /// <summary>How full the mug should be right now: full when indefinite, draining when timed.</summary>
        private float TargetLevel()
        {
            if (!_active) return 0f;
            var remaining = Remaining;
            if (remaining == null) return 1f;
            double fraction = remaining.Value.TotalSeconds / _sessionLength.TotalSeconds;
            // Snap to the renderer's steps so the mug only animates when it visibly changes,
            // and never looks empty while still awake.
            int steps = (int)Math.Ceiling(fraction * MugRenderer.Levels);
            return Math.Max(1, Math.Min(MugRenderer.Levels, steps)) / (float)MugRenderer.Levels;
        }

        // ---- Animation -------------------------------------------------------------

        private void StartAnimating()
        {
            _timer.Interval = FastInterval;
            _timer.Start();
            Tick();
        }

        private void Tick()
        {
            if (_active && Remaining is TimeSpan left && left <= TimeSpan.Zero)
                Deactivate(); // timed session finished

            float target = TargetLevel();
            bool moving = Math.Abs(target - _shownLevel) > 0.0001f;
            if (moving)
            {
                _shownLevel = target > _shownLevel
                    ? Math.Min(target, _shownLevel + PourStep)
                    : Math.Max(target, _shownLevel - PourStep);
            }

            UpdateIcon();
            UpdateTooltip();

            if (!_active && _shownLevel <= 0f)
            {
                _timer.Stop(); // nothing left to animate until the next activation
                return;
            }
            int interval = moving ? FastInterval : SlowInterval;
            if (_timer.Interval != interval) _timer.Interval = interval;
        }

        private void UpdateIcon()
        {
            int level = (int)Math.Round(_shownLevel * MugRenderer.Levels);
            if (_shownLevel > 0f && level == 0) level = 1;
            int steamFrame = (int)((uint)Environment.TickCount / SlowInterval % MugRenderer.SteamFrames);
            var icon = _renderer.Get(level, steamFrame);
            if (!ReferenceEquals(icon, _currentIcon))
            {
                _currentIcon = icon;
                _notify.Icon = icon;
            }
        }

        private void UpdateTooltip()
        {
            string text;
            if (!_active)
            {
                text = "CoffeeCup: sleep allowed. Double-click to stay awake.";
            }
            else
            {
                string what = _settings.KeepDisplayOn ? "awake, screen on" : "awake, screen may dim";
                text = Remaining is TimeSpan left
                    ? $"CoffeeCup: {what}, {DescribeRemaining(left)} left"
                    : $"CoffeeCup: {what}";
            }
            if (text.Length > 63) text = text.Substring(0, 63); // NotifyIcon limit
            if (_notify.Text != text) _notify.Text = text;
        }

        // ---- Menu ------------------------------------------------------------------

        private void RefreshMenu()
        {
            _toggleItem.Text = _active ? "Allow sleep" : "Stay awake" + DefaultSuffix();
            foreach (ToolStripMenuItem item in _durationMenu.DropDownItems)
                item.Checked = (int)item.Tag == _settings.DefaultMinutes;
            _keepDisplayItem.Checked = _settings.KeepDisplayOn;
            _startWithWindowsItem.Checked = Settings.StartWithWindows;
            _startActiveItem.Checked = _settings.StartActive;
        }

        private string DefaultSuffix() =>
            _settings.DefaultMinutes > 0 ? " for " + DescribeDuration(_settings.DefaultMinutes).ToLowerInvariant() : "";

        private static string DescribeDuration(int minutes)
        {
            if (minutes <= 0) return "Indefinitely";
            if (minutes < 60) return minutes + " minutes";
            int hours = minutes / 60;
            return hours == 1 ? "1 hour" : hours + " hours";
        }

        private static string DescribeRemaining(TimeSpan t)
        {
            int totalMinutes = (int)Math.Ceiling(t.TotalMinutes);
            if (totalMinutes < 1) totalMinutes = 1;
            int h = totalMinutes / 60, m = totalMinutes % 60;
            if (h == 0) return m + " min";
            return m == 0 ? h + " h" : $"{h} h {m} min";
        }

        // ---- Housekeeping ----------------------------------------------------------

        private void ShowWhereIAm()
        {
            _notify.ShowBalloonTip(5000, "CoffeeCup is in your notification area",
                "Double-click the mug to keep your PC awake. Right-click it for options.", ToolTipIcon.None);
        }

        private void OnUiThread(Action action)
        {
            try
            {
                if (_invoker.IsHandleCreated && !_invoker.IsDisposed)
                    _invoker.BeginInvoke(action);
            }
            catch (InvalidOperationException) { } // shutting down
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            bool light = Settings.TaskbarIsLight;
            if (light != _lightTaskbar) ResetRenderer(_iconSize, light);
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            int size = SystemInformation.SmallIconSize.Width;
            if (size != _iconSize) ResetRenderer(size, _lightTaskbar);
        }

        private void ResetRenderer(int size, bool light)
        {
            _iconSize = size;
            _lightTaskbar = light;
            _notify.Icon = null;
            _currentIcon = null;
            _renderer.Reset(size, light);
            UpdateIcon();
        }

        private void Exit()
        {
            Power.AllowSleep();
            _timer.Stop();
            _keepAliveTimer.Stop();
            _notify.Visible = false; // remove the icon now rather than leaving a ghost
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                _exitWait.Unregister(null);
                _showWait.Unregister(null);
                _exitEvent.Dispose();
                _showEvent.Dispose();
                _timer.Dispose();
                _keepAliveTimer.Dispose();
                _notify.Dispose();
                _menu.Dispose();
                _renderer.Dispose();
                _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
