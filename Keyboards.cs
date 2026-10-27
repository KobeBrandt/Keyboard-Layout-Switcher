using System.Runtime.InteropServices;

namespace KeyboardLayoutSwitcher;

/// <summary>A keyboard currently attached to the system, as reported by the Raw Input API.</summary>
public sealed record ConnectedKeyboard(string DevicePath, string HardwareId);

internal static class Keyboards
{
    /// <summary>
    /// Lists every keyboard interface that is currently connected.
    /// Paired Bluetooth keyboards that are switched off or out of range are left out.
    /// </summary>
    public static List<ConnectedKeyboard> GetConnected() =>
        GetPresent().Where(k => IsConnected(k.DevicePath)).ToList();

    /// <summary>
    /// Checks the device and its parents for the "is connected" property.
    /// Bluetooth keyboards stay present in Windows after they disconnect, but this property turns false.
    /// Devices without the property (USB, built-in) count as connected while present.
    /// </summary>
    public static bool IsConnected(string devicePath)
    {
        if (Native.CM_Locate_DevNode(out var devInst, ToInstanceId(devicePath), 0) != Native.CR_SUCCESS)
            return true;

        var key = Native.DEVPKEY_Device_IsConnected;
        for (var depth = 0; depth < 6; depth++)
        {
            uint size = 1;
            if (Native.CM_Get_DevNode_Property(devInst, ref key, out var type, out var value, ref size, 0) == Native.CR_SUCCESS
                && type == Native.DEVPROP_TYPE_BOOLEAN)
                return value != 0;
            if (Native.CM_Get_Parent(out devInst, devInst, 0) != Native.CR_SUCCESS)
                break;
        }
        return true;
    }

    /// <summary>
    /// Converts a device interface path such as <c>\\?\HID#VID_046D&amp;PID_C232#2&amp;1a87&amp;0&amp;0000#{guid}</c>
    /// to its device instance ID <c>HID\VID_046D&amp;PID_C232\2&amp;1a87&amp;0&amp;0000</c>.
    /// </summary>
    internal static string ToInstanceId(string devicePath)
    {
        var trimmed = devicePath.StartsWith(@"\\?\") ? devicePath[4..] : devicePath;
        var classGuid = trimmed.LastIndexOf("#{", StringComparison.Ordinal);
        if (classGuid > 0)
            trimmed = trimmed[..classGuid];
        return trimmed.Replace('#', '\\');
    }

    private static List<ConnectedKeyboard> GetPresent()
    {
        var result = new List<ConnectedKeyboard>();
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<Native.RAWINPUTDEVICELIST>();
        if (Native.GetRawInputDeviceList(null, ref count, size) != 0 || count == 0)
            return result;

        var devices = new Native.RAWINPUTDEVICELIST[count];
        if (Native.GetRawInputDeviceList(devices, ref count, size) == uint.MaxValue)
            return result;

        foreach (var device in devices.Take((int)count))
        {
            if (device.dwType != Native.RIM_TYPEKEYBOARD)
                continue;
            var path = GetDevicePath(device.hDevice);
            if (!string.IsNullOrEmpty(path))
                result.Add(new ConnectedKeyboard(path, ToHardwareId(path)));
        }
        return result;
    }

    public static string? GetDevicePath(IntPtr hDevice)
    {
        uint chars = 0;
        Native.GetRawInputDeviceInfo(hDevice, Native.RIDI_DEVICENAME, null, ref chars);
        if (chars == 0)
            return null;
        var buffer = new char[chars + 1];
        var copied = Native.GetRawInputDeviceInfo(hDevice, Native.RIDI_DEVICENAME, buffer, ref chars);
        if (copied == uint.MaxValue)
            return null;
        return new string(buffer).TrimEnd('\0');
    }

    /// <summary>
    /// Reduces a device path such as
    /// <c>\\?\HID#VID_046D&amp;PID_C52B&amp;MI_00#7&amp;2a1b&amp;0&amp;0000#{guid}</c>
    /// to the part that identifies the model (<c>HID#VID_046D&amp;PID_C52B&amp;MI_00</c>).
    /// The instance part is dropped because it changes when the keyboard moves to another USB port.
    /// </summary>
    public static string ToHardwareId(string devicePath)
    {
        var trimmed = devicePath.StartsWith(@"\\?\") ? devicePath[4..] : devicePath;
        var parts = trimmed.Split('#');
        return (parts.Length >= 2 ? $"{parts[0]}#{parts[1]}" : trimmed).ToUpperInvariant();
    }

    /// <summary>Reads the product name that a HID keyboard reports, e.g. "USB Keyboard".</summary>
    public static string? TryGetProductName(string devicePath)
    {
        try
        {
            // Access 0 is enough to query HID strings, even for keyboards that the system opens exclusively.
            using var handle = Native.CreateFile(devicePath, 0, 3 /* read | write share */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
            if (handle.IsInvalid)
                return null;

            var product = new char[256];
            var manufacturer = new char[256];
            var hasProduct = Native.HidD_GetProductString(handle, product, product.Length * 2);
            var hasManufacturer = Native.HidD_GetManufacturerString(handle, manufacturer, manufacturer.Length * 2);

            var productText = hasProduct ? ToText(product) : "";
            var manufacturerText = hasManufacturer ? ToText(manufacturer) : "";
            if (productText.Length == 0)
                return null;
            if (manufacturerText.Length > 0 && !productText.Contains(manufacturerText, StringComparison.OrdinalIgnoreCase))
                return $"{manufacturerText} {productText}";
            return productText;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Text up to the first null character, trimmed.</summary>
    private static string ToText(char[] buffer)
    {
        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end >= 0 ? end : buffer.Length).Trim();
    }

    /// <summary>Builds a fallback display name from the vendor and product IDs in the hardware ID.</summary>
    public static string DescribeHardwareId(string hardwareId)
    {
        var vid = FindToken(hardwareId, "VID_") ?? FindToken(hardwareId, "VID&0002");
        var pid = FindToken(hardwareId, "PID_") ?? FindToken(hardwareId, "PID&");
        if (hardwareId.StartsWith("ACPI", StringComparison.OrdinalIgnoreCase))
            return "Built-in keyboard";
        if (vid != null && pid != null)
            return $"Keyboard {vid}:{pid}";
        return "Keyboard";
    }

    private static string? FindToken(string text, string prefix)
    {
        var index = text.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        var start = index + prefix.Length;
        var length = Math.Min(4, text.Length - start);
        return length == 4 ? text.Substring(start, 4).ToUpperInvariant() : null;
    }
}
