namespace ClaudeUsageWidget.Native;

/// <summary>
/// Windows won't let a third-party app inject controls into Explorer's actual
/// taskbar surface, so instead we locate the taskbar and its notification area
/// via Win32 and place our own top-most window flush against it.
/// </summary>
internal static class TaskbarLocator
{
    public readonly record struct TaskbarInfo(int Left, int Top, int Right, int Bottom, int TrayLeft, IntPtr TaskbarHwnd);

    public static TaskbarInfo? Locate()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
            return null;

        // Best-effort: dock flush against the left edge of the notification area.
        // If it can't be found (Explorer internals vary by Windows build), fall
        // back to the taskbar's own right edge.
        var trayLeft = taskbarRect.Right;
        var tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect))
        {
            trayLeft = trayRect.Left;
        }

        return new TaskbarInfo(taskbarRect.Left, taskbarRect.Top, taskbarRect.Right, taskbarRect.Bottom, trayLeft, taskbar);
    }
}
