using System.Text.Json;
using System.Text.RegularExpressions;

namespace TrayStats.Services;

public sealed record MqttEntityState(
    string Id,
    string Name,
    string? State,
    string Component = "sensor",
    string? Unit = null,
    string? DeviceClass = null,
    string? StateClass = null,
    string? Icon = null,
    IReadOnlyDictionary<string, object?>? Attributes = null);

public sealed record MqttTelemetrySnapshot(
    string DeviceName,
    IReadOnlyList<MqttEntityState> Entities);

public sealed record MqttDeviceIdentity(string Name, string Id, string Slug)
{
    public static MqttDeviceIdentity Create(string machineName, string persistentId)
    {
        var name = string.IsNullOrWhiteSpace(machineName) ? "Windows PC" : machineName.Trim();
        var id = string.IsNullOrWhiteSpace(persistentId)
            ? Guid.NewGuid().ToString("N")
            : persistentId.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var slug = Slugify(name);
        return new MqttDeviceIdentity(name, id, slug);
    }

    public static string Slugify(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrEmpty(slug) ? "windows_pc" : slug;
    }
}

public static class HomeAssistantMqttDiscovery
{
    public static string ConfigTopic(string discoveryPrefix, MqttDeviceIdentity device, MqttEntityState entity)
        => $"{discoveryPrefix.TrimEnd('/')}/{entity.Component}/{device.Id}/{entity.Id}/config";

    public static string StateTopic(MqttDeviceIdentity device, MqttEntityState entity)
        => $"traystats/{device.Id}/state/{entity.Id}";

    public static string AvailabilityTopic(MqttDeviceIdentity device)
        => $"traystats/{device.Id}/availability";

    public static string AttributesTopic(MqttDeviceIdentity device, MqttEntityState entity)
        => $"traystats/{device.Id}/attributes/{entity.Id}";

    public static string BuildConfigJson(
        string discoveryPrefix,
        MqttDeviceIdentity device,
        MqttEntityState entity)
    {
        var config = new Dictionary<string, object?>
        {
            ["name"] = entity.Name,
            ["default_entity_id"] = $"{entity.Component}.{device.Slug}_{entity.Id}",
            ["unique_id"] = $"traystats_{device.Id}_{entity.Id}",
            ["state_topic"] = StateTopic(device, entity),
            ["availability_topic"] = AvailabilityTopic(device),
            ["payload_available"] = "online",
            ["payload_not_available"] = "offline",
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { $"traystats_{device.Id}" },
                ["name"] = device.Name,
                ["manufacturer"] = string.Empty,
                ["model"] = string.Empty
            }
        };

        if (!string.IsNullOrWhiteSpace(entity.Unit))
            config["unit_of_measurement"] = entity.Unit;
        if (!string.IsNullOrWhiteSpace(entity.DeviceClass))
            config["device_class"] = entity.DeviceClass;
        if (!string.IsNullOrWhiteSpace(entity.StateClass))
            config["state_class"] = entity.StateClass;
        if (!string.IsNullOrWhiteSpace(entity.Icon))
            config["icon"] = entity.Icon;
        if (entity.Component == "binary_sensor")
        {
            config["payload_on"] = "ON";
            config["payload_off"] = "OFF";
        }
        if (entity.Attributes is not null)
            config["json_attributes_topic"] = AttributesTopic(device, entity);

        return JsonSerializer.Serialize(config);
    }
}
