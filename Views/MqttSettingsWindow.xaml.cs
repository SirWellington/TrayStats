using System.Windows;
using TrayStats.Helpers;
using TrayStats.Services;

namespace TrayStats.Views;

public partial class MqttSettingsWindow : Window
{
    public bool MqttEnabled { get; private set; }
    public string DeviceName { get; private set; } = string.Empty;
    public string BrokerHost { get; private set; } = string.Empty;
    public int BrokerPort { get; private set; } = 1883;
    public string Username { get; private set; } = string.Empty;
    public string Password { get; private set; } = string.Empty;

    public MqttSettingsWindow(Settings settings)
    {
        InitializeComponent();
        EnabledCheckBox.IsChecked = settings.MqttEnabled;
        DeviceNameTextBox.Text = settings.MqttDeviceName;
        HostTextBox.Text = settings.MqttHost;
        PortTextBox.Text = settings.MqttPort.ToString();
        UsernameTextBox.Text = settings.MqttUsername;
        PasswordBox.Password = settings.MqttPassword;
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadConnectionFields(out var host, out var port, out var username, out var password))
            return;

        TestButton.IsEnabled = false;
        StatusTextBlock.Text = "Connecting to the MQTT broker…";
        try
        {
            var result = await HomeAssistantMqttPublisher.TestConnectionAsync(host, port, username, password);
            StatusTextBlock.Text = result.Success
                ? "Connection succeeded. Home Assistant can use this broker address and login."
                : $"Connection failed: {result.Error}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Connection failed: {ex.Message}";
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadConnectionFields(out var host, out var port, out var username, out var password))
            return;

        MqttEnabled = EnabledCheckBox.IsChecked == true;
        DeviceName = DeviceNameTextBox.Text.Trim();
        BrokerHost = host;
        BrokerPort = port;
        Username = username;
        Password = password;
        DialogResult = true;
    }

    private bool TryReadConnectionFields(out string host, out int port, out string username, out string password)
    {
        host = HostTextBox.Text.Trim();
        username = UsernameTextBox.Text.Trim();
        password = PasswordBox.Password;
        port = 1883;

        if (!int.TryParse(PortTextBox.Text.Trim(), out port) || port is < 1 or > 65535)
        {
            StatusTextBlock.Text = "Enter a valid TCP port between 1 and 65535.";
            return false;
        }

        if (EnabledCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(host))
        {
            StatusTextBlock.Text = "Enter the broker host before enabling MQTT publishing.";
            return false;
        }

        if (EnabledCheckBox.IsChecked == true && string.IsNullOrWhiteSpace(DeviceNameTextBox.Text))
        {
            StatusTextBlock.Text = "Enter the Home Assistant device name.";
            return false;
        }

        if (EnabledCheckBox.IsChecked == true && (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)))
        {
            StatusTextBlock.Text = "Enter the MQTT username and password before enabling publishing.";
            return false;
        }

        StatusTextBlock.Text = string.Empty;
        return true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
