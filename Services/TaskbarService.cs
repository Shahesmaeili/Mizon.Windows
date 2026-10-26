using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentSymbol = FluentIcons.Common.Symbol;
using Microsoft.UI.Dispatching;

namespace Mizon.Windows.Services;

public interface ITaskbarService
{
    void Initialize(IntPtr windowHandle);
    void SetProgressState(TaskbarProgressState state);
    void SetProgressValue(double currentValue, double maximumValue);
    void ClearProgress();
    void SetProgress(double percentage, TaskbarProgressState state = TaskbarProgressState.Normal);
    void NotifyWindowActivated();
    void SetPlaybackButtonState(AudioPlayerState state);
    event EventHandler? PlaybackToggleRequested;
    event EventHandler? FullScreenRequested;
}

public enum TaskbarProgressState
{

    NoProgress,
    Indeterminate,
    Normal,
    Error,
    Paused
}

public class TaskbarService : ITaskbarService
{
    private ITaskbarList4? _taskbarInstance;
    private readonly DispatcherQueue _dispatcherQueue;
    private IntPtr _windowHandle;
    private bool _isInitialized;
    private bool _thumbnailToolbarInitialized;
    private bool _thumbnailToolbarInitializing;
    private IntPtr _playIcon;
    private IntPtr _pauseIcon;
    private IntPtr _fullScreenIcon;
    private GCHandle _windowSubclassHandle;
    private static readonly SubclassProc WindowSubclassProcDelegate = WindowSubclassProc;
    private static readonly uint TaskbarButtonCreatedMessage = RegisterWindowMessage("TaskbarButtonCreated");

    private const uint PlayPauseButtonId = 1;
    private const uint FullScreenButtonId = 2;

    public TaskbarService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    public event EventHandler? PlaybackToggleRequested;
    public event EventHandler? FullScreenRequested;

    public void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;

        try
        {
            _taskbarInstance = (ITaskbarList4)new CTaskbarList();
            _taskbarInstance.HrInit();
            _isInitialized = true;
            InstallWindowMessageHandler();
            AppLog.Information("Taskbar COM service initialized and thumbnail-toolbar window handler installed.");
        }
        catch (COMException exception)
        {
            _taskbarInstance = null;
            _isInitialized = false;
            AppLog.Error("Taskbar COM service initialization failed.", exception);
        }

    }

    public void SetProgressState(TaskbarProgressState state)
    {
        RunOnUiThread(taskbar => taskbar.SetProgressState(_windowHandle, (TBPFLAG)state));
    }

    public void SetProgressValue(double currentValue, double maximumValue)
    {
        if (maximumValue <= 0)
            return;

        var completed = (ulong)Math.Clamp(Math.Round(currentValue), 0, maximumValue);
        var total = (ulong)Math.Round(maximumValue);
        RunOnUiThread(taskbar => taskbar.SetProgressValue(_windowHandle, completed, total));
    }

    public void SetProgress(double percentage, TaskbarProgressState state = TaskbarProgressState.Normal)
    {
        percentage = Math.Clamp(percentage, 0, 1);
        var completed = (ulong)Math.Round(percentage * 100);
        RunOnUiThread(taskbar =>
        {
            taskbar.SetProgressState(_windowHandle, (TBPFLAG)state);
            taskbar.SetProgressValue(_windowHandle, completed, 100);
        });
    }

    public void ClearProgress()
    {
        RunOnUiThread(taskbar => taskbar.SetProgressState(_windowHandle, TBPFLAG.TBPF_NOPROGRESS));
    }

    public void SetPlaybackButtonState(AudioPlayerState state)
    {
        if (!_thumbnailToolbarInitialized)
            return;

        var button = CreateButton(
            PlayPauseButtonId,
            state == AudioPlayerState.Playing ? _pauseIcon : _playIcon,
            state == AudioPlayerState.Playing ? "توقف موقت پخش" : "پخش آهنگ");

        RunOnUiThread(taskbar => taskbar.ThumbBarUpdateButtons(_windowHandle, 1, [button]));
    }

    public void NotifyWindowActivated()
    {
        // پیام TaskbarButtonCreated مسیر اصلی است. این مسیر fallback برای میزبان‌هایی
        // است که پیام را دیر ارسال می‌کنند یا در زمان ساخت HWND از دست می‌رود.
        _dispatcherQueue.TryEnqueue(() => _ = InitializeThumbnailToolbarAsync());
    }

    private async Task InitializeThumbnailToolbarAsync()
    {
        if (!_isInitialized || _thumbnailToolbarInitialized || _thumbnailToolbarInitializing)
            return;

        _thumbnailToolbarInitializing = true;

        try
        {
            AppLog.Information("Creating taskbar thumbnail-toolbar icons.");
            _playIcon = CreateSymbolIcon(FluentSymbol.Play);
            _pauseIcon = CreateSymbolIcon(FluentSymbol.Pause);
            _fullScreenIcon = CreateSymbolIcon(FluentSymbol.FullScreenMaximize);

            if (_playIcon == IntPtr.Zero || _pauseIcon == IntPtr.Zero || _fullScreenIcon == IntPtr.Zero)
                return;

            var buttons = new[]
            {
                CreateButton(PlayPauseButtonId, _playIcon, "پخش آهنگ"),
                CreateButton(FullScreenButtonId, _fullScreenIcon, "تمام‌صفحه کردن برنامه")
            };

            RunOnUiThread(taskbar => taskbar.ThumbBarAddButtons(_windowHandle, (uint)buttons.Length, buttons));
            _thumbnailToolbarInitialized = true;
            AppLog.Information("Taskbar thumbnail toolbar was added successfully.");
        }
        catch (Exception exception)
        {
            _thumbnailToolbarInitialized = false;
            AppLog.Error("Taskbar thumbnail toolbar creation failed.", exception);
        }
        finally
        {
            _thumbnailToolbarInitializing = false;
        }
    }

    private static THUMBBUTTON CreateButton(uint id, IntPtr icon, string tooltip) => new()
    {
        dwMask = THUMBBUTTONMASK.THB_ICON | THUMBBUTTONMASK.THB_TOOLTIP | THUMBBUTTONMASK.THB_FLAGS,
        iId = id,
        hIcon = icon,
        szTip = tooltip,
        dwFlags = THUMBBUTTONFLAGS.THBF_ENABLED
    };

    private static IntPtr CreateSymbolIcon(FluentSymbol symbol)
    {
        const int iconSize = 32;
        var pixels = new byte[iconSize * iconSize * 4];

        switch (symbol)
        {
            case FluentSymbol.Play:
                DrawPlaySymbol(pixels, iconSize);
                break;
            case FluentSymbol.Pause:
                FillRectangle(pixels, iconSize, 9, 7, 5, 18);
                FillRectangle(pixels, iconSize, 18, 7, 5, 18);
                break;
            case FluentSymbol.FullScreenMaximize:
                DrawFullScreenSymbol(pixels, iconSize);
                break;
        }

        var colorBitmap = CreateBitmap(iconSize, iconSize, 1, 32, pixels);
        var maskBitmap = CreateBitmap(iconSize, iconSize, 1, 1, null);
        if (colorBitmap == IntPtr.Zero || maskBitmap == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            return CreateIconIndirect(new ICONINFO
            {
                fIcon = true,
                hbmColor = colorBitmap,
                hbmMask = maskBitmap
            });
        }
        finally
        {
            DeleteObject(colorBitmap);
            DeleteObject(maskBitmap);
        }
    }

    // Thumbnail Toolbar فقط HICON می‌پذیرد؛ این‌ها معادل raster سفیدِ همان
    // Symbolهای FluentIcons هستند و با alpha شفاف برای taskbar ساخته می‌شوند.
    private static void DrawPlaySymbol(byte[] pixels, int iconSize)
    {
        const int left = 10;
        const int top = 6;
        const int height = 20;
        const int width = 15;

        for (var y = top; y < top + height; y++)
        {
            var distanceFromCenter = Math.Abs(y - ((top + height - 1) / 2d));
            var currentWidth = (int)Math.Round(width * (1d - (distanceFromCenter / (height / 2d))));
            for (var x = left; x <= left + currentWidth; x++)
                SetWhitePixel(pixels, iconSize, x, y);
        }
    }

    private static void DrawFullScreenSymbol(byte[] pixels, int iconSize)
    {
        const int inset = 7;
        const int cornerLength = 9;
        const int thickness = 3;
        var end = iconSize - inset;

        FillRectangle(pixels, iconSize, inset, inset, cornerLength, thickness);
        FillRectangle(pixels, iconSize, inset, inset, thickness, cornerLength);
        FillRectangle(pixels, iconSize, end - cornerLength, inset, cornerLength, thickness);
        FillRectangle(pixels, iconSize, end - thickness, inset, thickness, cornerLength);
        FillRectangle(pixels, iconSize, inset, end - thickness, cornerLength, thickness);
        FillRectangle(pixels, iconSize, inset, end - cornerLength, thickness, cornerLength);
        FillRectangle(pixels, iconSize, end - cornerLength, end - thickness, cornerLength, thickness);
        FillRectangle(pixels, iconSize, end - thickness, end - cornerLength, thickness, cornerLength);
    }

    private static void FillRectangle(byte[] pixels, int iconSize, int x, int y, int width, int height)
    {
        for (var currentY = y; currentY < y + height; currentY++)
        {
            for (var currentX = x; currentX < x + width; currentX++)
                SetWhitePixel(pixels, iconSize, currentX, currentY);
        }
    }

    private static void SetWhitePixel(byte[] pixels, int iconSize, int x, int y)
    {
        if (x < 0 || y < 0 || x >= iconSize || y >= iconSize)
            return;

        var index = ((y * iconSize) + x) * 4;
        pixels[index] = 255;
        pixels[index + 1] = 255;
        pixels[index + 2] = 255;
        pixels[index + 3] = 255;
    }

    private void RunOnUiThread(Action<ITaskbarList4> action)
    {
        if (!_isInitialized || _windowHandle == IntPtr.Zero || _taskbarInstance is null)
            return;

        var taskbar = _taskbarInstance;

        if (_dispatcherQueue.HasThreadAccess)
            action(taskbar);
        else
            _dispatcherQueue.TryEnqueue(() => action(taskbar));
    }

    internal void HandleWindowCommand(nint wParam)
    {
        var notification = (uint)((wParam.ToInt64() >> 16) & 0xffff);
        if (notification != THBN_CLICKED)
            return;

        switch ((uint)(wParam.ToInt64() & 0xffff))
        {
            case PlayPauseButtonId:
                PlaybackToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
            case FullScreenButtonId:
                FullScreenRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void InstallWindowMessageHandler()
    {
        if (_windowSubclassHandle.IsAllocated)
            return;

        _windowSubclassHandle = GCHandle.Alloc(this);
        if (!SetWindowSubclass(
                _windowHandle,
                WindowSubclassProcDelegate,
                1,
                (UIntPtr)GCHandle.ToIntPtr(_windowSubclassHandle).ToInt64()))
        {
            _windowSubclassHandle.Free();
            AppLog.Information($"Taskbar thumbnail-toolbar window handler installation failed. Win32Error='{Marshal.GetLastWin32Error()}'.");
        }
    }

    private static IntPtr WindowSubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message == WM_COMMAND && referenceData != UIntPtr.Zero)
        {
            var serviceHandle = GCHandle.FromIntPtr((IntPtr)referenceData.ToUInt64());
            if (serviceHandle.Target is TaskbarService service)
                service.HandleWindowCommand(wParam);
        }

        if (message == TaskbarButtonCreatedMessage && referenceData != UIntPtr.Zero)
        {
            var serviceHandle = GCHandle.FromIntPtr((IntPtr)referenceData.ToUInt64());
            if (serviceHandle.Target is TaskbarService service)
            {
                AppLog.Information("Received TaskbarButtonCreated notification.");
                _ = service.InitializeThumbnailToolbarAsync();
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    #region COM Interop
    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList { }

    [ComImport]
    [Guid("C43DC798-95D1-4BEA-9030-BB99E2983A1A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList4
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint tbatFlags);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, [In] THUMBBUTTON[] pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, [In] THUMBBUTTON[] pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
        void SetTabProperties(IntPtr hwndTab, uint stpFlags);
    }

    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }

    [Flags]
    private enum THUMBBUTTONMASK : uint
    {
        THB_ICON = 0x2,
        THB_TOOLTIP = 0x4,
        THB_FLAGS = 0x8
    }

    [Flags]
    private enum THUMBBUTTONFLAGS : uint
    {
        THBF_ENABLED = 0x0
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct THUMBBUTTON
    {
        public THUMBBUTTONMASK dwMask;
        public uint iId;
        public uint iBitmap;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szTip;
        public THUMBBUTTONFLAGS dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private const uint THBN_CLICKED = 0x1800;
    private const uint WM_COMMAND = 0x0111;

    private delegate IntPtr SubclassProc(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, byte[]? lpvBits);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconIndirect(in ICONINFO iconInfo);
    #endregion
}
