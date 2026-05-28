using System.Text.Json;

namespace HostComputerApp;

public sealed class AppSettings
{
    public PlcSettings Plc { get; set; } = new();
    public EapSettings Eap { get; set; } = new();
    public ScannerSettings Scanner { get; set; } = new();

    private static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "config.json");

    public static AppSettings Load(AppLogger logger)
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            logger.Error($"读取配置失败，已使用默认配置：{ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppLogger logger)
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            logger.Error($"保存配置失败：{ex.Message}");
            throw;
        }
    }
}

public sealed class PlcSettings
{
    public string IpAddress { get; set; } = "192.168.1.10";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public string AlarmRegister { get; set; } = "D3000";
    public ushort AlarmValue { get; set; } = 1;
    public string ScanTriggerRegister { get; set; } = "D50";
    public string ScanResultRegister { get; set; } = "D51";
    public string PortIdRegister { get; set; } = "D52";
    public string RescanCoil { get; set; } = "M838";
    public string ManualUploadCoil { get; set; } = "M839";
    public string SkipCoil { get; set; } = "M835";
}

public sealed class EapSettings
{
    public string IpAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5000;
    public string DeviceId { get; set; } = "0";
    public string EquipmentId { get; set; } = "BOND-001";
    public bool ActiveMode { get; set; } = true;
}

public sealed class ScannerSettings
{
    public string PortName { get; set; } = "";
    public int BaudRate { get; set; } = 9600;
    public string Parity { get; set; } = nameof(System.IO.Ports.Parity.None);
    public string StopBits { get; set; } = nameof(System.IO.Ports.StopBits.One);
    public int DataBits { get; set; } = 8;
    public int TimeoutSeconds { get; set; } = 10;
}
