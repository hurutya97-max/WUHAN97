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

        await EnsureConnectedAsync(linked.Token);
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

    public async Task<ushort> ReadSingleRegisterAsync(string registerText, CancellationToken cancellationToken)
    {
        var address = ParseRegisterAddress(registerText);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        await EnsureConnectedAsync(linked.Token);
        var stream = _client.GetStream();
        var transaction = unchecked(++_transactionId);
        var frame = BuildReadHoldingRegisterFrame(transaction, _unitId, address);
        await stream.WriteAsync(frame, linked.Token);

        var response = await ReadExactAsync(stream, 11, linked.Token);
        if (response[7] == 0x83)
        {
            throw new InvalidOperationException($"Modbus异常码：0x{response[8]:X2}");
        }

        if (response[0] != frame[0] || response[1] != frame[1] || response[7] != 0x03 || response[8] != 0x02)
        {
            throw new InvalidOperationException("Modbus读取响应校验失败。");
        }

        return (ushort)((response[9] << 8) | response[10]);
    }

    public async Task WriteSingleCoilAsync(string coilText, bool value, CancellationToken cancellationToken)
    {
        var address = ParseCoilAddress(coilText);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        await EnsureConnectedAsync(linked.Token);
        var stream = _client.GetStream();
        var transaction = unchecked(++_transactionId);
        var frame = BuildWriteSingleCoilFrame(transaction, _unitId, address, value);
        await stream.WriteAsync(frame, linked.Token);

        var response = await ReadExactAsync(stream, 12, linked.Token);
        if (response[7] == 0x85)
        {
            throw new InvalidOperationException($"Modbus异常码：0x{response[8]:X2}");
        }

        if (response[0] != frame[0] || response[1] != frame[1] || response[7] != 0x05)
        {
            throw new InvalidOperationException("Modbus线圈写入响应校验失败。");
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

    private static byte[] BuildReadHoldingRegisterFrame(ushort transactionId, byte unitId, ushort address)
    {
        return
        [
            (byte)(transactionId >> 8), (byte)transactionId,
            0x00, 0x00,
            0x00, 0x06,
            unitId,
            0x03,
            (byte)(address >> 8), (byte)address,
            0x00, 0x01,
        ];
    }

    private static byte[] BuildWriteSingleCoilFrame(ushort transactionId, byte unitId, ushort address, bool value)
    {
        return
        [
            (byte)(transactionId >> 8), (byte)transactionId,
            0x00, 0x00,
            0x00, 0x06,
            unitId,
            0x05,
            (byte)(address >> 8), (byte)address,
            value ? (byte)0xFF : (byte)0x00, 0x00,
        ];
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.Connected)
        {
            return;
        }

        await _client.ConnectAsync(_host, _port, cancellationToken);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var response = new byte[length];
        var read = 0;
        while (read < response.Length)
        {
            var count = await stream.ReadAsync(response.AsMemory(read, response.Length - read), cancellationToken);
            if (count == 0)
            {
                throw new IOException("Modbus连接已关闭。");
            }
            read += count;
        }

        return response;
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

    private static ushort ParseCoilAddress(string coilText)
    {
        var text = coilText.Trim().ToUpperInvariant();
        var match = Regex.Match(text, @"^(M|C|COIL)?(?<address>\d+)$");
        if (!match.Success)
        {
            throw new FormatException("PLC线圈地址格式不正确，例如 M838 或 838。");
        }

        var address = int.Parse(match.Groups["address"].Value);
        if (address is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(coilText), "Modbus线圈地址超出范围。");
        }

        return (ushort)address;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
