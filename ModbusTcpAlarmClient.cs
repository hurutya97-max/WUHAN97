using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace HostComputerApp;

public sealed class ModbusTcpAlarmClient : IDisposable
{
    private static ushort _transactionId;
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly TcpClient _client = new();

    public ModbusTcpAlarmClient(string host, int port, byte unitId)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
    }

    public async Task WriteSingleRegisterAsync(string registerText, ushort value, CancellationToken cancellationToken)
    {
        var address = ParseRegisterAddress(registerText);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        await _client.ConnectAsync(_host, _port, linked.Token);
        var stream = _client.GetStream();
        var transaction = unchecked(++_transactionId);
        var frame = BuildWriteSingleRegisterFrame(transaction, _unitId, address, value);
        await stream.WriteAsync(frame, linked.Token);

        var response = new byte[12];
        var read = 0;
        while (read < response.Length)
        {
            var count = await stream.ReadAsync(response.AsMemory(read, response.Length - read), linked.Token);
            if (count == 0)
            {
                throw new IOException("Modbus连接已关闭。");
            }
            read += count;
        }

        if (response[7] == 0x86)
        {
            throw new InvalidOperationException($"Modbus异常码：0x{response[8]:X2}");
        }

        if (response[0] != frame[0] || response[1] != frame[1] || response[7] != 0x06)
        {
            throw new InvalidOperationException("Modbus写入响应校验失败。");
        }
    }

    private static byte[] BuildWriteSingleRegisterFrame(ushort transactionId, byte unitId, ushort address, ushort value)
    {
        return
        [
            (byte)(transactionId >> 8), (byte)transactionId,
            0x00, 0x00,
            0x00, 0x06,
            unitId,
            0x06,
            (byte)(address >> 8), (byte)address,
            (byte)(value >> 8), (byte)value,
        ];
    }

    private static ushort ParseRegisterAddress(string registerText)
    {
        var text = registerText.Trim().ToUpperInvariant();
        var match = Regex.Match(text, @"^(D|HR|R)?(?<address>\d+)$");
        if (!match.Success)
        {
            throw new FormatException("PLC报警寄存器格式不正确，例如 D3000 或 3000。");
        }

        var address = int.Parse(match.Groups["address"].Value);
        if (address is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(registerText), "Modbus寄存器地址超出范围。");
        }

        return (ushort)address;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
