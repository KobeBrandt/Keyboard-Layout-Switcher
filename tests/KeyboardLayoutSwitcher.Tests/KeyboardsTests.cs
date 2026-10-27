using Xunit;

namespace KeyboardLayoutSwitcher.Tests;

public class KeyboardsTests
{
    [Fact]
    public void ToHardwareId_DropsPrefixInstanceAndClassGuid()
    {
        var path = @"\\?\HID#VID_046D&PID_C52B&MI_00#7&2a1b&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

        Assert.Equal("HID#VID_046D&PID_C52B&MI_00", Keyboards.ToHardwareId(path));
    }

    [Fact]
    public void ToHardwareId_IsUpperCase()
    {
        Assert.Equal("HID#VID_046D&PID_C52B", Keyboards.ToHardwareId(@"\\?\hid#vid_046d&pid_c52b#7&2a1b#{guid}"));
    }

    [Fact]
    public void ToHardwareId_SamePortIndependentOfInstance()
    {
        var port1 = @"\\?\HID#VID_046D&PID_C232#2&1a87&0&0000#{guid}";
        var port2 = @"\\?\HID#VID_046D&PID_C232#5&99ff&0&0000#{guid}";

        Assert.Equal(Keyboards.ToHardwareId(port1), Keyboards.ToHardwareId(port2));
    }

    [Fact]
    public void ToHardwareId_KeepsPathWithoutSeparators()
    {
        Assert.Equal("ACPI", Keyboards.ToHardwareId("acpi"));
    }

    [Fact]
    public void ToInstanceId_ConvertsInterfacePath()
    {
        var path = @"\\?\HID#VID_046D&PID_C232#2&1a87&0&0000#{884b96c3-56ef-11d1-bc8c-00a0c91405dd}";

        Assert.Equal(@"HID\VID_046D&PID_C232\2&1a87&0&0000", Keyboards.ToInstanceId(path));
    }

    [Fact]
    public void DescribeHardwareId_UsbKeyboard()
    {
        Assert.Equal("Keyboard 046D:C232", Keyboards.DescribeHardwareId("HID#VID_046D&PID_C232"));
    }

    [Fact]
    public void DescribeHardwareId_BluetoothKeyboard()
    {
        var id = "HID#{00001124-0000-1000-8000-00805F9B34FB}_VID&0002046D_PID&B342";

        Assert.Equal("Keyboard 046D:B342", Keyboards.DescribeHardwareId(id));
    }

    [Fact]
    public void DescribeHardwareId_BuiltInKeyboard()
    {
        Assert.Equal("Built-in keyboard", Keyboards.DescribeHardwareId("ACPI#PNP0303"));
    }

    [Fact]
    public void DescribeHardwareId_UnknownFallsBack()
    {
        Assert.Equal("Keyboard", Keyboards.DescribeHardwareId("HID#SOMETHING"));
    }

    [Fact]
    public void DescribeHardwareId_TruncatedIdFallsBack()
    {
        Assert.Equal("Keyboard", Keyboards.DescribeHardwareId("HID#VID_04"));
    }
}
