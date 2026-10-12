using System.Text.Json;
using System.Windows.Threading;
using MQTTnet;
using MQTTnet.Protocol;
using TrayStats.Helpers;
using TrayStats.ViewModels;

namespace TrayStats.Services;

public sealed class HomeAssistantMqttPublisher : IDisposable
{
    private const string DiscoveryPrefix = "homeassistant";
    private static readonly TimeSpan PublishInterval = TimeSpan.FromSeconds(5);

    private readonly DashboardViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly HashSet<string> _publishedEntityKeys = new(StringComparer.Ordinal);
    private Settings _settings;
    private IMqttClient? _client;
    private MqttDeviceIdentity? _identity;
    private DateTime _nextConnectionAttempt = DateTime.MinValue;
    private DateTime _connectedAt;
    private int _snapshotCount;
    private bool _disposed;
    private string _status = "Disabled";

    public event Action<string>? StatusChanged;

    public string Status => _status;

    public HomeAssistantMqttPublisher(DashboardViewModel viewModel, Settings settings)
    {
        _viewModel = viewModel;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = PublishInterval
        };
        _timer.Tick += OnTimerTick;
    }

    public async Task ApplySettingsAsync(Settings settings)
    {
        if (_disposed)
            return;

        _timer.Stop();
        await _operationLock.WaitAsync();
        try
        {
            await DisconnectAsync(publishOffline: true);
            _settings = settings;
            _identity = MqttDeviceIdentity.Create(settings.MqttDeviceName, settings.MqttDeviceId);
            _publishedEntityKeys.Clear();
            _snapshotCount = 0;

            if (!settings.MqttEnabled)
            {
                SetStatus("Disabled");
                _viewModel.SetTelemetryActive(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.MqttHost)
                || settings.MqttPort is < 1 or > 65535
                || string.IsNullOrWhiteSpace(settings.MqttUsername)
                || string.IsNullOrEmpty(settings.MqttPassword))
            {
                SetStatus("Configuration needed");
                _viewModel.SetTelemetryActive(false);
                return;
            }

            _viewModel.SetTelemetryActive(true);
            _nextConnectionAttempt = DateTime.MinValue;
            _timer.Start();
        }
        finally
        {
            _operationLock.Release();
        }

        await PublishTickAsync();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        await PublishTickAsync();
    }

    private async Task PublishTickAsync()
    {
        if (_disposed || !_settings.MqttEnabled || _identity is null)
            return;

        if (!await _operationLock.WaitAsync(0))
            return;

        try
        {
            if (_client is null || !_client.IsConnected)
            {
                if (DateTime.UtcNow < _nextConnectionAttempt)
                {
                    SetStatus("Reconnecting");
                    return;
                }

                if (!await ConnectAsync())
                    return;
            }

            await ApplyDiscoverySchemaMigrationAsync();

            var snapshot = _viewModel.CreateMqttSnapshot();
            var currentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in snapshot.Entities)
            {
                var key = EntityKey(entity);
                currentKeys.Add(key);

                if (_publishedEntityKeys.Add(key))
                {
                    await PublishAsync(
                        HomeAssistantMqttDiscovery.ConfigTopic(DiscoveryPrefix, _identity, entity),
                        HomeAssistantMqttDiscovery.BuildConfigJson(DiscoveryPrefix, _identity, entity),
                        retain: true);
                }

                await PublishAsync(
                    HomeAssistantMqttDiscovery.StateTopic(_identity, entity),
                    entity.State ?? string.Empty,
                    retain: true);

                if (entity.Attributes is not null)
                {
                    await PublishAsync(
                        HomeAssistantMqttDiscovery.AttributesTopic(_identity, entity),
                        JsonSerializer.Serialize(entity.Attributes),
                        retain: true);
                }
            }

            _snapshotCount++;
            if (_snapshotCount >= 4)
                await RemoveStaleEntitiesAsync(currentKeys);

            if (DateTime.UtcNow - _connectedAt > TimeSpan.FromSeconds(10))
                SetStatus("Connected");
        }
        catch (Exception ex)
        {
            SetStatus($"Publish error: {SafeMessage(ex)}");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<bool> ConnectAsync()
    {
        _nextConnectionAttempt = DateTime.UtcNow.AddSeconds(10);

        try
        {
            _client?.Dispose();
            var factory = new MqttClientFactory();
            _client = factory.CreateMqttClient();

            var availabilityTopic = HomeAssistantMqttDiscovery.AvailabilityTopic(_identity!);
            var options = new MqttClientOptionsBuilder()
                .WithClientId($"traystats-{_identity!.Id}")
                .WithTcpServer(_settings.MqttHost, _settings.MqttPort)
                .WithCredentials(_settings.MqttUsername, _settings.MqttPassword)
                .WithTimeout(TimeSpan.FromSeconds(5))
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .WithWillTopic(availabilityTopic)
                .WithWillPayload("offline")
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.ConnectAsync(options, CancellationToken.None);
            _connectedAt = DateTime.UtcNow;
            _publishedEntityKeys.Clear();

            await PublishAsync(availabilityTopic, "online", retain: true);
            SetStatus("Connected");
            return true;
        }
        catch (Exception ex)
        {
            _client?.Dispose();
            _client = null;
            SetStatus($"Connection failed: {SafeMessage(ex)}");
            return false;
        }
    }

    private async Task RemoveStaleEntitiesAsync(IReadOnlySet<string> currentKeys)
    {
        var previous = _settings.MqttKnownEntities.ToHashSet(StringComparer.Ordinal);
        if (previous.SetEquals(currentKeys))
            return;

        foreach (var oldKey in previous.Except(currentKeys, StringComparer.Ordinal))
        {
            var parts = oldKey.Split('/', 2);
            if (parts.Length != 2)
                continue;

            var entity = new MqttEntityState(parts[1], parts[1], null, Component: parts[0]);
            await PublishAsync(HomeAssistantMqttDiscovery.ConfigTopic(DiscoveryPrefix, _identity!, entity), string.Empty, retain: true);
            await PublishAsync(HomeAssistantMqttDiscovery.StateTopic(_identity!, entity), string.Empty, retain: true);
            await PublishAsync(HomeAssistantMqttDiscovery.AttributesTopic(_identity!, entity), string.Empty, retain: true);
        }

        _settings.MqttKnownEntities = currentKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        SettingsStore.Save(_settings);
    }

    private async Task ApplyDiscoverySchemaMigrationAsync()
    {
        if (_settings.MqttDiscoverySchemaVersion >= 1)
            return;

        var previous = _settings.MqttKnownEntities.ToArray();
        foreach (var key in previous)
        {
            var parts = key.Split('/', 2);
            if (parts.Length != 2)
                continue;

            var entity = new MqttEntityState(parts[1], parts[1], null, Component: parts[0]);
            await PublishAsync(HomeAssistantMqttDiscovery.ConfigTopic(DiscoveryPrefix, _identity!, entity), string.Empty, retain: true);
            await PublishAsync(HomeAssistantMqttDiscovery.StateTopic(_identity!, entity), string.Empty, retain: true);
            await PublishAsync(HomeAssistantMqttDiscovery.AttributesTopic(_identity!, entity), string.Empty, retain: true);
        }

        if (previous.Length > 0)
            await Task.Delay(500);

        _settings.MqttKnownEntities = Array.Empty<string>();
        _settings.MqttDiscoverySchemaVersion = 1;
        SettingsStore.Save(_settings);
        _publishedEntityKeys.Clear();
        _snapshotCount = 0;
    }

    private async Task PublishAsync(string topic, string payload, bool retain)
    {
        if (_client is null || !_client.IsConnected)
            return;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build();
        await _client.PublishAsync(message, CancellationToken.None);
    }

    private async Task DisconnectAsync(bool publishOffline)
    {
        if (_client is null)
            return;

        try
        {
            if (_client.IsConnected && publishOffline && _identity is not null)
            {
                await PublishAsync(HomeAssistantMqttDiscovery.AvailabilityTopic(_identity), "offline", retain: true);
                await _client.DisconnectAsync();
            }
            else if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }
        }
        catch
        {
            // The broker's last-will message marks the device offline if graceful shutdown fails.
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    public static async Task<(bool Success, string Error)> TestConnectionAsync(
        string host,
        int port,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        IMqttClient? client = null;
        try
        {
            client = new MqttClientFactory().CreateMqttClient();
            var options = new MqttClientOptionsBuilder()
                .WithClientId($"traystats-test-{Guid.NewGuid():N}")
                .WithTcpServer(host, port)
                .WithCredentials(username, password)
                .WithTimeout(TimeSpan.FromSeconds(5))
                .Build();

            await client.ConnectAsync(options, cancellationToken);
            await client.DisconnectAsync();
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, SafeMessage(ex));
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static string EntityKey(MqttEntityState entity) => $"{entity.Component}/{entity.Id}";

    private static string SafeMessage(Exception exception)
        => string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

    private void SetStatus(string status)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
            return;
        _status = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        var cleanupTask = Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                await DisconnectAsync(publishOffline: true);
            }
            finally
            {
                _operationLock.Release();
            }
        });

        if (cleanupTask.Wait(TimeSpan.FromSeconds(3)))
        {
            _operationLock.Dispose();
        }
    }
}
