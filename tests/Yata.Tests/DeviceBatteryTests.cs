using Yata.Services;

namespace Yata.Tests;

/// <summary>Connected / low / disconnected transitions, and folding device nodes into devices.</summary>
public class DeviceBatteryTests
{
    private static DeviceBatteryInfo Pods(int percent) => new("pods", "AirPods Pro", percent, DeviceBatteryKind.Headphones);

    private static DeviceBatteryInfo Mouse(int percent) => new("mouse", "MX Master 3", percent, DeviceBatteryKind.Mouse);

    private static DeviceBatteryInfo[] Snap(params DeviceBatteryInfo[] devices) => devices;

    private static DeviceBatteryEventKind[] Kinds(IReadOnlyList<DeviceBatteryEvent> events) =>
        events.Select(e => e.Kind).ToArray();

    [Fact]
    public void Devices_there_at_startup_are_not_news()
    {
        var tracker = new DeviceBatteryTracker();

        Assert.Empty(tracker.Update(Snap(Pods(80), Mouse(60))));
        Assert.Equal(2, tracker.Current.Count);
    }

    [Fact]
    public void A_device_appearing_later_is_connected_once()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap());

        var events = tracker.Update(Snap(Pods(80)));
        Assert.Equal(new[] { DeviceBatteryEventKind.Connected }, Kinds(events));
        Assert.Equal(80, events[0].Info!.Percent);
        Assert.Empty(tracker.Update(Snap(Pods(79))));
    }

    [Fact]
    public void A_vanished_device_is_disconnected_with_its_name()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap(Pods(80), Mouse(60)));

        var events = tracker.Update(Snap(Mouse(60)));

        var gone = Assert.Single(events);
        Assert.Equal(DeviceBatteryEventKind.Disconnected, gone.Kind);
        Assert.Equal("pods", gone.Id);
        Assert.Equal("AirPods Pro", gone.Name);
        Assert.Null(gone.Info);
        Assert.Equal("mouse", Assert.Single(tracker.Current).Id);
    }

    [Fact]
    public void Low_fires_once_per_drop()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap(Mouse(20)));

        Assert.Equal(new[] { DeviceBatteryEventKind.Low }, Kinds(tracker.Update(Snap(Mouse(15)))));
        Assert.Empty(tracker.Update(Snap(Mouse(10))));
        Assert.Empty(tracker.Update(Snap(Mouse(5))));
    }

    [Fact]
    public void Low_rearms_only_above_25_percent()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap(Mouse(14)));

        Assert.Empty(tracker.Update(Snap(Mouse(25)))); // Charging a bit isn't enough...
        Assert.Empty(tracker.Update(Snap(Mouse(12))));
        Assert.Empty(tracker.Update(Snap(Mouse(26)))); // ...charged it is.
        Assert.Equal(new[] { DeviceBatteryEventKind.Low }, Kinds(tracker.Update(Snap(Mouse(15)))));
    }

    [Fact]
    public void Already_low_at_startup_still_warns()
    {
        var tracker = new DeviceBatteryTracker();

        Assert.Equal(new[] { DeviceBatteryEventKind.Low }, Kinds(tracker.Update(Snap(Pods(9)))));
    }

    [Fact]
    public void Connecting_low_says_both_connected_then_low()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap());

        Assert.Equal(new[] { DeviceBatteryEventKind.Connected, DeviceBatteryEventKind.Low },
            Kinds(tracker.Update(Snap(Pods(10)))));
    }

    [Fact]
    public void Reconnecting_still_low_does_not_warn_again()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap(Pods(10)));
        tracker.Update(Snap());

        Assert.Equal(new[] { DeviceBatteryEventKind.Connected }, Kinds(tracker.Update(Snap(Pods(10)))));
    }

    [Fact]
    public void Duplicate_ids_in_a_snapshot_count_once()
    {
        var tracker = new DeviceBatteryTracker();
        tracker.Update(Snap());

        Assert.Single(tracker.Update(Snap(Pods(80), Pods(80))));
        Assert.Single(tracker.Current);
    }

    // ---- Merge / Classify --------------------------------------------------------------

    private const string Container = "0fefae23-fffa-5d99-8a61-a945e00c5073";
    private const uint HeadsetCod = 0x240404; // Audio/video, wearable headset (the AirPods on the dev PC).

    private static DeviceNode Node(string id, string? name, int? percent = null, string? container = Container,
        string[]? categories = null, uint? cod = null) =>
        new(id, container, name, percent, categories ?? Array.Empty<string>(), cod);

    [Fact]
    public void Nodes_of_one_headset_make_one_device()
    {
        var devices = DeviceBattery.Merge(new[]
        {
            Node("BTHENUM\\DEV_X", "AirPods Pro", categories: new[] { "Communication.Headset.Bluetooth" }, cod: HeadsetCod),
            Node("BTHENUM\\{110E}", "AirPods Pro Avrcp Transport", cod: HeadsetCod),
            Node("BTHHFENUM\\X", "AirPods Pro Hands-Free", percent: 70),
            Node("BTHENUM\\{110B}", "AirPods Pro"),
        });

        var pods = Assert.Single(devices);
        Assert.Equal(Container, pods.Id);
        Assert.Equal("AirPods Pro", pods.Name);
        Assert.Equal(70, pods.Percent);
        Assert.Equal(DeviceBatteryKind.Headphones, pods.Kind);
    }

    [Fact]
    public void Devices_without_a_level_are_left_out()
    {
        Assert.Empty(DeviceBattery.Merge(new[] { Node("A", "AirPods Pro"), Node("B", "AirPods Pro Hands-Free") }));
    }

    [Fact]
    public void Nodes_without_a_container_stand_alone()
    {
        var devices = DeviceBattery.Merge(new[]
        {
            Node("A", "Keyboard K380", percent: 50, container: null),
            Node("B", "Xbox Wireless Controller", percent: 30, container: null),
        });

        Assert.Equal(new[] { "A", "B" }, devices.Select(d => d.Id));
        Assert.Equal(new[] { DeviceBatteryKind.Keyboard, DeviceBatteryKind.Controller }, devices.Select(d => d.Kind));
    }

    [Theory]
    [InlineData("Communication.Headset.Bluetooth", DeviceBatteryKind.Headphones)]
    [InlineData("Input.Mouse", DeviceBatteryKind.Mouse)]
    [InlineData("Input.Keyboard", DeviceBatteryKind.Keyboard)]
    [InlineData("Input.Gaming", DeviceBatteryKind.Controller)]
    [InlineData("Communication.Phone", DeviceBatteryKind.Phone)]
    public void Category_decides_first(string category, DeviceBatteryKind expected)
    {
        Assert.Equal(expected, DeviceBattery.Classify(new[] { category }, null, "Some gadget"));
    }

    [Theory]
    [InlineData(0x240404u, DeviceBatteryKind.Headphones)] // Audio/video, wearable headset.
    [InlineData(0x240418u, DeviceBatteryKind.Headphones)] // Audio/video, headphones.
    [InlineData(0x240414u, DeviceBatteryKind.Other)]      // Audio/video, loudspeaker.
    [InlineData(0x002540u, DeviceBatteryKind.Keyboard)]   // Peripheral, keyboard.
    [InlineData(0x002580u, DeviceBatteryKind.Mouse)]      // Peripheral, pointing device.
    [InlineData(0x002508u, DeviceBatteryKind.Controller)] // Peripheral, gamepad.
    [InlineData(0x5A020Cu, DeviceBatteryKind.Phone)]      // Phone, smartphone.
    public void Class_of_device_decides_without_a_category(uint cod, DeviceBatteryKind expected)
    {
        Assert.Equal(expected, DeviceBattery.Classify(Array.Empty<string>(), cod, "Some gadget"));
    }

    [Theory]
    [InlineData("Galaxy Buds2 Pro", DeviceBatteryKind.Headphones)]
    [InlineData("WH-1000XM4 Headphones", DeviceBatteryKind.Headphones)]
    [InlineData("Logitech Mouse", DeviceBatteryKind.Mouse)]
    [InlineData("DualSense Wireless Controller", DeviceBatteryKind.Controller)]
    [InlineData("Luna's iPhone", DeviceBatteryKind.Phone)]
    [InlineData("JBL Flip 5", DeviceBatteryKind.Other)]
    public void Name_is_the_last_resort(string name, DeviceBatteryKind expected)
    {
        Assert.Equal(expected, DeviceBattery.Classify(Array.Empty<string>(), null, name));
    }
}
