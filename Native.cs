using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeyboardLayoutSwitcher;

internal static partial class Native
{
    public const int WM_INPUT = 0x00FF;
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int WM_DEVICECHANGE = 0x0219;
    public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;

    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RID_HEADER = 0x10000005;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;

    public const uint SPI_SETDEFAULTINPUTLANG = 0x005A;
    public const uint SPIF_SENDCHANGE = 0x02;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public IntPtr hDevice;
        public uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, [Out] char[]? pData, ref uint pcbSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputData(IntPtr hRawInput, uint uiCommand, out RAWINPUTHEADER pData, ref uint pcbSize, uint cbSizeHeader);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfo(uint uiAction, uint uiParam, ref IntPtr pvParam, uint fWinIni);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetKeyboardLayout(uint idThread);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint idEventThread, uint dwmsEventTime);

    [LibraryImport("user32.dll")]
    public static partial IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc pfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [LibraryImport("hid.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetProductString(SafeFileHandle hidDeviceObject, [Out] char[] buffer, int bufferLength);

    [LibraryImport("hid.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetManufacturerString(SafeFileHandle hidDeviceObject, [Out] char[] buffer, int bufferLength);

    public const uint CR_SUCCESS = 0;
    public const uint DEVPROP_TYPE_BOOLEAN = 0x11;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    /// <summary>
    /// Device property that Bluetooth devices set to false while they are paired but not connected.
    /// The device itself stays present in Windows, so this is the only reliable disconnect signal.
    /// </summary>
    public static readonly DEVPROPKEY DEVPKEY_Device_IsConnected = new()
    {
        fmtid = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"),
        pid = 15,
    };

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Parent(out uint parentDevInst, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    public static partial uint CM_Get_DevNode_Property(uint devInst, ref DEVPROPKEY propertyKey, out uint propertyType,
        out byte buffer, ref uint bufferSize, uint flags);
}
