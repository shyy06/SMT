using System.Runtime.InteropServices;

namespace SMT;

/// <summary>
/// Windows API declarations for window manipulation.
/// </summary>
public static class Win32API
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOOLWINDOW = 0x80;

    public const int LWA_COLORKEY = 0x1;
    public const int LWA_ALPHA = 0x2;

    public const int SWP_NOMOVE = 0x2;
    public const int SWP_NOSIZE = 0x1;
    public const int HWND_TOPMOST = -1;
    public const int HWND_NOTOPMOST = -2;

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Set the window to be click-through (mouse penetration).
    /// When enabled, mouse events pass through to windows beneath.
    /// </summary>
    public static void SetMousePenetration(IntPtr handle, bool enable)
    {
        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        if (enable)
        {
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            SetWindowLong(handle, GWL_EXSTYLE, exStyle);
            // Set alpha to 220 (slightly translucent) and make non-content areas
            // transparent — the transparency color key is not used here, just alpha.
            SetLayeredWindowAttributes(handle, 0, 220, LWA_ALPHA);
        }
        else
        {
            exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            exStyle &= ~WS_EX_TRANSPARENT;
            exStyle &= ~WS_EX_LAYERED;
            SetWindowLong(handle, GWL_EXSTYLE, exStyle);
        }
    }

    /// <summary>
    /// Set window always-on-top.
    /// </summary>
    public static void SetTopMost(IntPtr handle, bool topMost)
    {
        SetWindowPos(handle, topMost ? HWND_TOPMOST : HWND_NOTOPMOST,
            0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }
}
