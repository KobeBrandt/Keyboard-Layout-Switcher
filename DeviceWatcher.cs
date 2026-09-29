using System.Runtime.InteropServices;

namespace KeyboardLayoutSwitcher;

/// <summary>
/// Hidden window that reports keyboard connect/disconnect events and, while capturing,
/// which keyboard a key press came from.
/// </summary>
internal sealed class DeviceWatcher : NativeWindow, IDisposable
{
    public event EventHandler? DevicesChanged;
    public event EventHandler<string>? KeyFromDevice;

    private bool _capturing;

    public DeviceWatcher()
    {
        CreateHandle(new CreateParams { Caption = "KeyboardLayoutSwitcher.DeviceWatcher" });
        Register(capture: false);
    }

    /// <summary>Start reporting the device of each key press, even when another app has focus.</summary>
    public void StartCapture()
    {
        _capturing = true;
        Register(capture: true);
    }

    public void StopCapture()
    {
        _capturing = false;
        Register(capture: false);
    }

    private void Register(bool capture)
    {
        var flags = Native.RIDEV_DEVNOTIFY | (capture ? Native.RIDEV_INPUTSINK : 0);
        var devices = new[]
        {
            // Usage page 1 (generic desktop), usage 6 (keyboard).
            new Native.RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x06, dwFlags = flags, hwndTarget = Handle },
        };
        Native.RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_INPUT_DEVICE_CHANGE:
            case Native.WM_DEVICECHANGE:
                DevicesChanged?.Invoke(this, EventArgs.Empty);
                break;

            case Native.WM_INPUT when _capturing:
                uint size = (uint)Marshal.SizeOf<Native.RAWINPUTHEADER>();
                if (Native.GetRawInputData(m.LParam, Native.RID_HEADER, out var header, ref size, size) != uint.MaxValue
                    && header.dwType == Native.RIM_TYPEKEYBOARD
                    && header.hDevice != IntPtr.Zero)
                {
                    var path = Keyboards.GetDevicePath(header.hDevice);
                    if (path != null)
                        KeyFromDevice?.Invoke(this, path);
                }
                break;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
