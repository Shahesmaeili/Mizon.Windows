using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Mizon.Windows.Common;

public static class TaskbarProgress
{
    private static readonly ITaskbarList4 _taskbarInstance;
    private static IntPtr _windowHandle;

    static TaskbarProgress()
    {
        _taskbarInstance = (ITaskbarList4)new CTaskbarList();
        _taskbarInstance.HrInit();
    }

    public static void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    public static void SetState(TBPFLAG state)
    {
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("TaskbarProgress not initialized. Call Initialize() first.");

        _taskbarInstance.SetProgressState(_windowHandle, state);
    }

    public static void SetValue(double currentValue, double maximumValue)
    {
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("TaskbarProgress not initialized. Call Initialize() first.");

        _taskbarInstance.SetProgressValue(_windowHandle, (ulong)currentValue, (ulong)maximumValue);
    }

    public static void Clear()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            _taskbarInstance.SetProgressState(_windowHandle, TBPFLAG.TBPF_NOPROGRESS);
        }
    }
    
    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CTaskbarList { }

    [ComImport]
    [Guid("c43dc798-95d1-4bea-9030-bb99e2983a1a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList4
    {
        void HrInit();
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
    }

    public enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }

}