using System.Text.Json;
using TrayStats.Services;
using Xunit;

namespace TrayStats.Tests;

public sealed class HomeAssistantMqttDiscoveryTests
{
    [Fact]
    public void DeviceDisplayNamePreservesTheWindowsMachineName()
    {
        var device = MqttDeviceIdentity.Create("NormandySR2", "00112233445566778899aabbccddeeff");

        Assert.Equal("NormandySR2", device.Name);
        Assert.Equal("00112233445566778899aabbccddeeff", device.Id);
        Assert.Equal("normandysr2", device.Slug);
    }

    [Fact]
    public void RenamingDevicePreservesItsIdentityAndDiscoveryTopics()
    {
        const string persistentId = "00112233445566778899aabbccddeeff";
        var original = MqttDeviceIdentity.Create("NormandySR2", persistentId);
        var renamed = MqttDeviceIdentity.Create("Workshop PC", persistentId);
        var entity = new MqttEntityState("cpu_load", "CPU Load", "42");

        Assert.Equal(original.Id, renamed.Id);
        Assert.NotEqual(original.Slug, renamed.Slug);
        Assert.Equal(
            HomeAssistantMqttDiscovery.ConfigTopic("homeassistant", original, entity),
            HomeAssistantMqttDiscovery.ConfigTopic("homeassistant", renamed, entity));
        Assert.Equal(
            HomeAssistantMqttDiscovery.StateTopic(original, entity),
            HomeAssistantMqttDiscovery.StateTopic(renamed, entity));
    }

    [Fact]
    public void DifferentMachinesGetDistinctDiscoveryAndStateTopics()
    {
        var first = MqttDeviceIdentity.Create("NormandySR2", "00112233445566778899aabbccddeeff");
        var second = MqttDeviceIdentity.Create("GamingPC", "ffeeddccbbaa99887766554433221100");
        var entity = new MqttEntityState("cpu_load", "CPU Load", "42", Unit: "%", StateClass: "measurement");

        Assert.NotEqual(
            HomeAssistantMqttDiscovery.ConfigTopic("homeassistant", first, entity),
            HomeAssistantMqttDiscovery.ConfigTopic("homeassistant", second, entity));
        Assert.NotEqual(
            HomeAssistantMqttDiscovery.StateTopic(first, entity),
            HomeAssistantMqttDiscovery.StateTopic(second, entity));
    }

    [Fact]
    public void DiscoveryPayloadIncludesDeviceIdentityAndMeasurementMetadata()
    {
        var device = MqttDeviceIdentity.Create("NormandySR2", "00112233445566778899aabbccddeeff");
        var entity = new MqttEntityState(
            "cpu_load",
            "CPU Load",
            "42",
            Unit: "%",
            StateClass: "measurement");

        using var json = JsonDocument.Parse(
            HomeAssistantMqttDiscovery.BuildConfigJson("homeassistant", device, entity));
        var root = json.RootElement;

        Assert.Equal("CPU Load", root.GetProperty("name").GetString());
        Assert.Equal("sensor.normandysr2_cpu_load", root.GetProperty("default_entity_id").GetString());
        Assert.Equal("%", root.GetProperty("unit_of_measurement").GetString());
        Assert.Equal("measurement", root.GetProperty("state_class").GetString());
        Assert.Equal("traystats_" + device.Id + "_cpu_load", root.GetProperty("unique_id").GetString());
        Assert.Equal("NormandySR2", root.GetProperty("device").GetProperty("name").GetString());
        Assert.Equal(string.Empty, root.GetProperty("device").GetProperty("manufacturer").GetString());
        Assert.Equal(string.Empty, root.GetProperty("device").GetProperty("model").GetString());
    }
}
