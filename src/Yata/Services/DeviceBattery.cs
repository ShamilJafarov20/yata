using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Yata.Helpers;

namespace Yata.Services;

public enum DeviceBatteryKind { Headphones, Mouse, Keyboard, Controller, Phone, Other }

/// <summary>A connected device that reports its battery. Id is stable across reads (the PnP container).</summary>
internal sealed record DeviceBatteryInfo(string Id, string Name, int Percent, DeviceBatteryKind Kind);

/// <summary>One PnP device node, as read; several of them make up one physical device.</summary>
internal sealed record DeviceNode(string InstanceId, string? ContainerId, string? Name, int? Percent,
    IReadOnlyList<string> Categories, uint? ClassOfDevice);

/// <summary>
/// Battery of connected Bluetooth devices (headphones, mice, keyboards, controllers), from the
/// level Windows itself shows in Settings → Bluetooth. Read straight from the device nodes with
/// cfgmgr32: the lean WinRT projection has no Windows.Devices.Enumeration. Only devices that are
/// connected and report a level are listed; a paired one that's switched off has none.
/// </summary>
internal static class DeviceBattery
{
    private const uint CR_SUCCESS = 0;
    private const uint CR_BUFFER_SMALL = 0x1A;
    private const uint CM_GETIDLIST_FILTER_PRESENT = 0x100;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    private const uint DEVPROP_TYPE_BYTE = 0x03;
    private const uint DEVPROP_TYPE_UINT32 = 0x07;
    private const uint DEVPROP_TYPE_GUID = 0x0D;
    private const uint DEVPROP_TYPE_STRING = 0x12;
    private const uint DEVPROP_TYPE_STRING_LIST = 0x2012;

    private static readonly DevPropKey BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly DevPropKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DevPropKey DeviceDescKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);
    private static readonly DevPropKey ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static readonly DevPropKey CategoryKey = new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 90);
    private static readonly DevPropKey ClassOfDeviceKey = new(new Guid("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A"), 10);

    // The PC itself: every built-in device sits in this container, none of them is a gadget.
    private static readonly Guid LocalMachineContainer = new("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    private static bool _warned;

    /// <summary>Connected devices with a battery level, one per physical device. Never throws.</summary>
    public static IReadOnlyList<DeviceBatteryInfo> Read()
    {
        try
        {
            var nodes = new List<DeviceNode>();
            foreach (string id in PresentDeviceIds())
            {
                if (CM_Locate_DevNodeW(out uint devInst, id, CM_LOCATE_DEVNODE_NORMAL) != CR_SUCCESS)
                    continue; // Gone since the list was taken.

                Guid? container = GetProperty(devInst, ContainerIdKey, DEVPROP_TYPE_GUID) is { Length: 16 } g ? new Guid(g) : null;
                if (container == LocalMachineContainer)
                    continue;

                int? percent = GetProperty(devInst, BatteryKey, DEVPROP_TYPE_BYTE) is { Length: 1 } b && b[0] <= 100 ? b[0] : null;
                string? name = GetString(devInst, FriendlyNameKey) ?? GetString(devInst, DeviceDescKey);
                uint? cod = GetProperty(devInst, ClassOfDeviceKey, DEVPROP_TYPE_UINT32) is { Length: 4 } c ? BitConverter.ToUInt32(c) : null;
                IReadOnlyList<string> categories = GetProperty(devInst, CategoryKey, DEVPROP_TYPE_STRING_LIST) is { } list
                    ? MultiSz(Encoding.Unicode.GetString(list))
                    : Array.Empty<string>();
                nodes.Add(new DeviceNode(id, container?.ToString(), name, percent, categories, cod));
            }
            return Merge(nodes);
        }
        catch (Exception ex)
        {
            // Polled: say it once, not every few seconds.
            if (!_warned)
            {
                _warned = true;
                Log.Warn("devices", "Não deu para ler a bateria dos dispositivos Bluetooth", ex);
            }
            return Array.Empty<DeviceBatteryInfo>();
        }
    }

    /// <summary>
    /// One entry per physical device: a headset shows up as several nodes (hands-free, A2DP,
    /// AVRCP…) in one container, and the level may sit on any of them.
    /// </summary>
    internal static IReadOnlyList<DeviceBatteryInfo> Merge(IEnumerable<DeviceNode> nodes)
    {
        var result = new List<DeviceBatteryInfo>();
        foreach (var group in nodes.GroupBy(n => n.ContainerId ?? n.InstanceId, StringComparer.OrdinalIgnoreCase))
        {
            int? percent = group.Min(n => n.Percent);
            if (percent == null)
                continue;
            // The bare device name: "AirPods Pro" over "AirPods Pro Hands-Free".
            string? name = group.Select(n => n.Name?.Trim()).Where(n => !string.IsNullOrEmpty(n))
                .OrderBy(n => n!.Length).FirstOrDefault();
            if (name == null)
                continue;
            var kind = Classify(group.SelectMany(n => n.Categories), group.Select(n => n.ClassOfDevice).FirstOrDefault(c => c != null), name);
            result.Add(new DeviceBatteryInfo(group.Key, name, percent.Value, kind));
        }
        return result;
    }

    /// <summary>What the device is: the container category Windows assigned, the Bluetooth class of device, then the name.</summary>
    internal static DeviceBatteryKind Classify(IEnumerable<string> categories, uint? classOfDevice, string name)
    {
        foreach (string category in categories)
            if (FromText(category) is { } kind)
                return kind;
        if (classOfDevice is { } cod && FromClassOfDevice(cod) is { } fromCod)
            return fromCod;
        return FromText(name) ?? DeviceBatteryKind.Other;
    }

    // Matches both categories ("Communication.Headset.Bluetooth", "Input.Mouse") and names.
    private static DeviceBatteryKind? FromText(string text)
    {
        string t = text.ToLowerInvariant();
        if (t.Contains("headset") || t.Contains("headphone") || t.Contains("earbud") || t.Contains("buds") || t.Contains("airpods"))
            return DeviceBatteryKind.Headphones;
        if (t.Contains("mouse") || t.Contains("trackpad") || t.Contains("touchpad"))
            return DeviceBatteryKind.Mouse;
        if (t.Contains("keyboard"))
            return DeviceBatteryKind.Keyboard;
        if (t.Contains("controller") || t.Contains("gamepad") || t.Contains("gaming") || t.Contains("joystick")
            || t.Contains("dualsense") || t.Contains("dualshock") || t.Contains("joy-con"))
            return DeviceBatteryKind.Controller;
        if (t.Contains("phone"))
            return DeviceBatteryKind.Phone;
        return null;
    }

    // Bluetooth Assigned Numbers, "Class of Device": major class in bits 8-12, minor in 2-7.
    private static DeviceBatteryKind? FromClassOfDevice(uint cod)
    {
        uint major = (cod >> 8) & 0x1F;
        uint minor = (cod >> 2) & 0x3F;
        switch (major)
        {
            case 0x02:
                return DeviceBatteryKind.Phone;
            case 0x04 when minor is 0x01 or 0x02 or 0x06: // Wearable headset, hands-free, headphones.
                return DeviceBatteryKind.Headphones;
            case 0x05:
                if ((minor & 0x0F) is 0x01 or 0x02) // Joystick, gamepad.
                    return DeviceBatteryKind.Controller;
                return (minor >> 4) switch
                {
                    0x01 or 0x03 => DeviceBatteryKind.Keyboard, // Keyboard, combo keyboard + pointer.
                    0x02 => DeviceBatteryKind.Mouse,
                    _ => null,
                };
            default:
                return null;
        }
    }

    private static IEnumerable<string> PresentDeviceIds()
    {
        // The list can grow between the two calls (a device plugged in); try again then.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out uint length, null, CM_GETIDLIST_FILTER_PRESENT) != CR_SUCCESS)
                break;
            var buffer = new char[length];
            uint result = CM_Get_Device_ID_ListW(null, buffer, length, CM_GETIDLIST_FILTER_PRESENT);
            if (result == CR_BUFFER_SMALL)
                continue;
            if (result != CR_SUCCESS)
                break;
            return MultiSz(new string(buffer));
        }
        return Array.Empty<string>();
    }

    private static string[] MultiSz(string text) => text.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    private static string? GetString(uint devInst, DevPropKey key) =>
        GetProperty(devInst, key, DEVPROP_TYPE_STRING) is { } bytes
            ? Encoding.Unicode.GetString(bytes).TrimEnd('\0') is { Length: > 0 } s ? s : null
            : null;

    /// <summary>The raw value of a property of the expected type; null when missing or of another type.</summary>
    private static byte[]? GetProperty(uint devInst, DevPropKey key, uint expectedType)
    {
        uint size = 0;
        uint result = CM_Get_DevNode_PropertyW(devInst, ref key, out uint type, null, ref size, 0);
        if (result != CR_BUFFER_SMALL || type != expectedType || size == 0)
            return null;
        var buffer = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, ref key, out type, buffer, ref size, 0) != CR_SUCCESS || type != expectedType)
            return null;
        return size == buffer.Length ? buffer : buffer[..(int)size];
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid fmtid, uint pid)
    {
        public readonly Guid Fmtid = fmtid;
        public readonly uint Pid = pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(uint devInst, ref DevPropKey key, out uint propertyType,
        [Out] byte[]? buffer, ref uint bufferSize, uint flags);
}

public enum DeviceBatteryEventKind { Connected, Low, Disconnected }

/// <summary>Something worth a notch message. Info is null only for <see cref="DeviceBatteryEventKind.Disconnected"/>.</summary>
internal sealed record DeviceBatteryEvent(DeviceBatteryEventKind Kind, string Id, string Name, DeviceBatteryInfo? Info)
{
    public static DeviceBatteryEvent Connected(DeviceBatteryInfo info) => new(DeviceBatteryEventKind.Connected, info.Id, info.Name, info);

    public static DeviceBatteryEvent Low(DeviceBatteryInfo info) => new(DeviceBatteryEventKind.Low, info.Id, info.Name, info);

    public static DeviceBatteryEvent Disconnected(string id, string name) => new(DeviceBatteryEventKind.Disconnected, id, name, null);
}

/// <summary>
/// Turns successive <see cref="DeviceBattery.Read"/> snapshots into events. Pure: no I/O, no clock.
/// Devices already there at startup aren't announced as connected; a low battery is, once per
/// drop, even right after connecting, and again only after it went back above <see cref="RearmAbove"/>.
/// </summary>
internal sealed class DeviceBatteryTracker
{
    public const int LowAt = 15;
    public const int RearmAbove = 25;

    private Dictionary<string, DeviceBatteryInfo> _current = new();
    // Kept across disconnects, so a flaky link bouncing at 10% doesn't warn each time it comes back.
    private readonly HashSet<string> _warnedLow = new();
    private bool _started;

    public IReadOnlyList<DeviceBatteryInfo> Current { get; private set; } = Array.Empty<DeviceBatteryInfo>();

    public IReadOnlyList<DeviceBatteryEvent> Update(IReadOnlyList<DeviceBatteryInfo> snapshot)
    {
        var events = new List<DeviceBatteryEvent>();
        var next = new Dictionary<string, DeviceBatteryInfo>();
        foreach (var info in snapshot)
        {
            if (!next.TryAdd(info.Id, info))
                continue;
            if (_started && !_current.ContainsKey(info.Id))
                events.Add(DeviceBatteryEvent.Connected(info));
            if (info.Percent > RearmAbove)
                _warnedLow.Remove(info.Id);
            else if (info.Percent <= LowAt && _warnedLow.Add(info.Id))
                events.Add(DeviceBatteryEvent.Low(info));
        }
        foreach (var gone in _current.Values)
            if (!next.ContainsKey(gone.Id))
                events.Add(DeviceBatteryEvent.Disconnected(gone.Id, gone.Name));

        _current = next;
        Current = next.Values.ToList();
        _started = true;
        return events;
    }
}
