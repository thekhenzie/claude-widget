using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ClaudeUsageWidget.Native;
using ClaudeUsageWidget.Usage;

namespace ClaudeUsageWidget.UI;

public partial class DockedBarWindow : Window
{
    private const int BarWidth = 244;
    private const int BarHeight = 42;
    private const double TrackWidth = 92;

    private readonly UsagePoller _poller;
    private DispatcherTimer? _repositionTimer;
    private DispatcherTimer? _clockTimer;
    private UsageSnapshot? _lastSnapshot;
    private (int X, int Y)? _lastAppliedPosition;

    public DockedBarWindow(UsagePoller poller)
    {
        InitializeComponent();

        _poller = poller;
        _poller.SnapshotUpdated += OnSnapshotUpdated;
        _poller.ErrorOccurred += OnPollerError;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        Reposition();

        _repositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _repositionTimer.Tick += (_, _) => Reposition();
        _repositionTimer.Start();

        // Ticks the clock and the "reset in ..." countdowns between polls,
        // aligned to the system clock's actual minute boundary so the
        // displayed time changes the instant the real minute does.
        RefreshClockAndCountdowns();
        ScheduleNextClockTick();
    }

    private void ScheduleNextClockTick()
    {
        var now = DateTime.Now;
        var delay = TimeSpan.FromMilliseconds(60_000 - (now.Second * 1000 + now.Millisecond));

        _clockTimer = new DispatcherTimer { Interval = delay };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        _clockTimer!.Stop();
        _clockTimer.Tick -= OnClockTick;

        RefreshClockAndCountdowns();
        ScheduleNextClockTick(); // recomputed from "now" each time - self-correcting, no drift
    }

    private void Reposition()
    {
        var info = TaskbarLocator.Locate();
        if (info is null)
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var taskbarHeight = info.Value.Bottom - info.Value.Top;
        var x = info.Value.TrayLeft - BarWidth - 8;
        var y = info.Value.Top + Math.Max(0, (taskbarHeight - BarHeight) / 2);

        var positionChanged = _lastAppliedPosition is not { } last || last.X != x || last.Y != y;

        if (positionChanged)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HwndTopmost, x, y, BarWidth, BarHeight, NativeMethods.SwpNoActivate);
            _lastAppliedPosition = (x, y);
            return;
        }

        // Position is unchanged, but clicking the taskbar (Start menu, search,
        // tray flyouts) can shuffle z-order within the topmost band and leave
        // us covered without ever moving us. Only re-assert topmost - without
        // touching position/size - when we've actually been displaced, instead
        // of blindly forcing it every tick (which was causing visible flicker).
        var immediatelyAboveTaskbar = NativeMethods.GetWindow(info.Value.TaskbarHwnd, NativeMethods.GwHwndPrev);
        if (immediatelyAboveTaskbar != hwnd)
        {
            NativeMethods.SetWindowPos(
                hwnd, NativeMethods.HwndTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoActivate | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize);
        }
    }

    private void OnSnapshotUpdated(UsageSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _lastSnapshot = snapshot;

            SessionPctText.Text = FormatPercent(snapshot.SessionUtilization);
            WeeklyPctText.Text = FormatPercent(snapshot.WeeklyUtilization);
            SessionFill.Width = FormatFillWidth(snapshot.SessionUtilization);
            WeeklyFill.Width = FormatFillWidth(snapshot.WeeklyUtilization);

            var updatedAt = snapshot.FetchedAt.ToLocalTime().ToString("HH:mm");
            SessionTimeText.Text = updatedAt;
            WeeklyTimeText.Text = updatedAt;

            SessionPill.Opacity = 1.0;
            WeeklyPill.Opacity = 1.0;

            RootBorder.ToolTip = BuildTooltip(snapshot);
            RefreshClockAndCountdowns();
        });
    }

    private void OnPollerError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            // Keep showing the last good numbers, just dim them to signal staleness.
            SessionPill.Opacity = 0.4;
            WeeklyPill.Opacity = 0.4;
            RootBorder.ToolTip = $"Usage unavailable: {message}";
        });
    }

    private void RefreshClockAndCountdowns()
    {
        ClockText.Text = DateTimeOffset.Now.ToString("HH:mm");
        SessionResetText.Text = FormatResetIn(_lastSnapshot?.SessionResetsAt);
        WeeklyResetText.Text = FormatResetIn(_lastSnapshot?.WeeklyResetsAt);
    }

    private static string FormatPercent(double? utilization) =>
        utilization is { } value ? $"{Math.Clamp(value, 0, 1) * 100:0}%" : "--%";

    private static double FormatFillWidth(double? utilization) =>
        utilization is { } value ? TrackWidth * Math.Clamp(value, 0, 1) : 0;

    private static string FormatResetIn(DateTimeOffset? resetsAt)
    {
        if (resetsAt is not { } dt)
            return "reset --";

        var remaining = dt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "reset now";

        if (remaining.TotalDays >= 1)
            return $"reset {(int)remaining.TotalDays}d {remaining.Hours}h";

        if (remaining.TotalHours >= 1)
            return $"reset {(int)remaining.TotalHours}h {remaining.Minutes}min";

        return $"reset {Math.Max(1, remaining.Minutes)}min";
    }

    private static string BuildTooltip(UsageSnapshot s)
    {
        static string Format(double? utilization, DateTimeOffset? resetsAt, string label)
        {
            if (utilization is null)
                return $"{label}: unavailable";

            var resetText = resetsAt is { } dt ? $", resets {dt.ToLocalTime():MMM d, HH:mm}" : "";
            return $"{label}: {utilization * 100:0}%{resetText}";
        }

        return $"{Format(s.SessionUtilization, s.SessionResetsAt, "Session (5h)")}\n" +
               $"{Format(s.WeeklyUtilization, s.WeeklyResetsAt, "Weekly (7d)")}\n" +
               $"Updated {s.FetchedAt.ToLocalTime():HH:mm}";
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => _poller.TriggerNow();

    private void OnMenuButtonClick(object sender, MouseButtonEventArgs e)
    {
        RootBorder.ContextMenu.PlacementTarget = RootBorder;
        RootBorder.ContextMenu.IsOpen = true;
    }

    private void OnOpenUsagePageClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://claude.ai/settings/usage") { UseShellExecute = true });
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void OnExitClick(object sender, MouseButtonEventArgs e) => Application.Current.Shutdown();
}
