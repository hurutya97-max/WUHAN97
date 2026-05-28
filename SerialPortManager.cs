using System.IO.Ports;
using System.Text;

namespace HostComputerApp;

public sealed class SerialPortManager : IDisposable
{
    private readonly AppLogger _logger;
    private readonly StringBuilder _buffer = new();
    private SerialPort? _serialPort;

    public event EventHandler<string>? CodeReceived;
    public event EventHandler<string>? StatusChanged;

    public bool IsOpen => _serialPort?.IsOpen == true;

    public SerialPortManager(AppLogger logger)
    {
        _logger = logger;
    }

    public void Open(ScannerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PortName))
        {
            throw new InvalidOperationException("请先选择扫码枪串口号。");
        }

        Close();
        _serialPort = new SerialPort(settings.PortName)
        {
            BaudRate = settings.BaudRate,
            DataBits = settings.DataBits,
            Parity = Enum.Parse<Parity>(settings.Parity),
            StopBits = Enum.Parse<StopBits>(settings.StopBits),
            NewLine = "\r",
            ReadTimeout = 1000,
            WriteTimeout = 1000,
            Encoding = Encoding.ASCII,
        };
        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.Open();
        _logger.Info($"扫码枪串口已打开：{settings.PortName},{settings.BaudRate},{settings.DataBits},{settings.Parity},{settings.StopBits}");
        StatusChanged?.Invoke(this, $"已连接 {settings.PortName}");
    }

    public void Close()
    {
        if (_serialPort is null)
        {
            return;
        }

        try
        {
            _serialPort.DataReceived -= SerialPort_DataReceived;
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            _logger.Info("扫码枪串口已关闭。");
        }
        finally
        {
            _serialPort.Dispose();
            _serialPort = null;
            StatusChanged?.Invoke(this, "未连接");
        }
    }

    public void SendTriggerCommand()
    {
        Send([0x4C, 0x4F, 0x4E, 0x0D], "LON");
    }

    public void SendStopCommand()
    {
        Send([0x4C, 0x4F, 0x46, 0x46, 0x0D], "LOFF");
    }

    private void Send(byte[] command, string name)
    {
        if (_serialPort?.IsOpen != true)
        {
            throw new InvalidOperationException("扫码枪串口未打开。");
        }

        _serialPort.Write(command, 0, command.Length);
        _logger.Info($"已发送霍尼韦尔扫码指令：{name}");
    }


    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = (SerialPort)sender;
            var text = port.ReadExisting();
            lock (_buffer)
            {
                _buffer.Append(text);
                ExtractLines();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"扫码枪数据接收异常：{ex.Message}");
        }
    }

    private void ExtractLines()
    {
        while (true)
        {
            var data = _buffer.ToString();
            var index = data.IndexOfAny(['\r', '\n']);
            if (index < 0)
            {
                return;
            }

            var code = data[..index].Trim();
            var next = index + 1;
            while (next < data.Length && (data[next] == '\r' || data[next] == '\n'))
            {
                next++;
            }

            _buffer.Clear();
            _buffer.Append(data[next..]);

            if (!string.IsNullOrWhiteSpace(code))
            {
                CodeReceived?.Invoke(this, code);
            }
        }
    }

    public void Dispose()
    {
        Close();
    }
}
