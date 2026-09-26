using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SrweCli;

internal readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
}

internal sealed record WindowState(nint Handle, int ProcessId, string Title, PixelRect Window, PixelRect Client,
    long Style, long ExStyle);

internal static class NativeWindow
{
    public static void EnablePerMonitorDpiAwareness() => _ = SetProcessDpiAwarenessContext((nint)(-4));

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsThickFrame = 0x00040000;
    private const long WsDlgFrame = 0x00400000;
    private const long WsBorder = 0x00800000;
    private const long WsExDlgModalFrame = 0x00000001;
    private const long WsExWindowEdge = 0x00000100;
    private const long WsExClientEdge = 0x00000200;
    private const long WsExStaticEdge = 0x00020000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;
    private const int SwRestore = 9;
    private const uint WmExitSizeMove = 0x0232;
    private const uint SmtoAbortIfHung = 0x0002;

    public static bool Exists(nint handle) => handle != 0 && IsWindow(handle);

    public static WindowState Inspect(nint handle)
    {
        if (!Exists(handle)) throw new InvalidOperationException("The target window no longer exists.");
        if (!GetWindowRect(handle, out var outer) || !GetClientRect(handle, out var client))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not inspect the window rectangle.");

        var clientOrigin = new Point();
        if (!ClientToScreen(handle, ref clientOrigin))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Could not locate the client area.");

        var title = new StringBuilder(Math.Max(256, GetWindowTextLength(handle) + 1));
        _ = GetWindowText(handle, title, title.Capacity);
        _ = GetWindowThreadProcessId(handle, out var pid);
        return new WindowState(handle, checked((int)pid), title.ToString(),
            new PixelRect(outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top),
            new PixelRect(clientOrigin.X, clientOrigin.Y, client.Right - client.Left, client.Bottom - client.Top),
            GetStyle(handle, GwlStyle), GetStyle(handle, GwlExStyle));
    }

    public static WindowState Apply(nint handle, PixelRect target, bool targetClient, bool borderless,
        long? style, long? exStyle, bool exitSizeMove)
    {
        if (target.Width <= 0 || target.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(target), "Width and height must be positive.");

        if (IsIconic(handle)) _ = ShowWindow(handle, SwRestore);

        var before = Inspect(handle);
        var nextStyle = style ?? before.Style;
        var nextExStyle = exStyle ?? before.ExStyle;
        if (borderless)
        {
            nextStyle &= ~(WsThickFrame | WsDlgFrame | WsBorder);
            nextExStyle &= ~(WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge);
        }

        if (nextStyle != before.Style) SetStyle(handle, GwlStyle, nextStyle);
        if (nextExStyle != before.ExStyle) SetStyle(handle, GwlExStyle, nextExStyle);
        if (nextStyle != before.Style || nextExStyle != before.ExStyle)
            Move(handle, new PixelRect(0, 0, 0, 0), SwpNoMove | SwpNoSize | SwpFrameChanged);

        for (var attempt = 0; attempt < (targetClient ? 4 : 1); attempt++)
        {
            var current = Inspect(handle);
            PixelRect outer;
            if (targetClient)
            {
                outer = new PixelRect(
                    checked(target.X - (current.Client.X - current.Window.X)),
                    checked(target.Y - (current.Client.Y - current.Window.Y)),
                    checked(target.Width + (current.Window.Width - current.Client.Width)),
                    checked(target.Height + (current.Window.Height - current.Client.Height)));
            }
            else outer = target;

            Move(handle, outer, 0);
            var result = Inspect(handle);
            if ((targetClient ? result.Client : result.Window) == target) break;
            if (attempt == 3 || !targetClient)
                throw new InvalidOperationException($"Window did not reach the requested {(targetClient ? "client" : "outer")} rectangle. " +
                    $"Requested {target}; actual {(targetClient ? result.Client : result.Window)}.");
        }

        if (exitSizeMove && SendMessageTimeout(handle, WmExitSizeMove, 0, 0, SmtoAbortIfHung, 2000, out _) == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WM_EXITSIZEMOVE timed out or failed.");

        return Inspect(handle);
    }

    private static void Move(nint handle, PixelRect rect, uint extraFlags)
    {
        var flags = SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpNoSendChanging | extraFlags;
        if (!SetWindowPos(handle, 0, rect.X, rect.Y, rect.Width, rect.Height, flags))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowPos failed.");
    }

    private static long GetStyle(nint handle, int index)
    {
        var value = IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index).ToInt64() : GetWindowLong32(handle, index);
        var error = Marshal.GetLastPInvokeError();
        if (value == 0 && error != 0) throw new Win32Exception(error, "GetWindowLongPtr failed.");
        return unchecked((uint)value);
    }

    private static void SetStyle(nint handle, int index, long value)
    {
        if (IntPtr.Size == 8) _ = SetWindowLongPtr64(handle, index, (nint)value);
        else _ = SetWindowLong32(handle, index, unchecked((int)value));
        var error = Marshal.GetLastPInvokeError();
        if (error != 0) throw new Win32Exception(error, "SetWindowLongPtr failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint handle, ref Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(nint handle, StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowTextLength(nint handle);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr64(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr64(nint handle, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(nint handle, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint handle, uint message, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);
}
