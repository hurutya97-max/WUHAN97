# 芯联键合上位机

这是一个 .NET 8 WinForms 上位机骨架，包含：

- PLC Modbus TCP 配置与报警寄存器写入
- EAP SECS/GEM 参数配置界面
- 基恩士扫码枪串口配置与接收
- 四个产品码扫码流程、超时报警
- SQLite 本地扫码数据保存和表格显示
- 本地日志记录和日志界面

## 运行

```powershell
dotnet run
```

## 本地文件

运行后会在程序输出目录下生成：

- `config.json`：PLC、EAP、扫码枪配置
- `Data/scans.db`：SQLite 扫码数据
- `Logs/app-yyyyMMdd.log`：本地日志

## PLC 报警

报警寄存器支持类似 `D3000` 或 `3000` 的写法。当前实现使用 Modbus TCP 功能码 `0x06` 写单个保持寄存器，寄存器数值使用界面中的“报警写入值”。

## SECS/GEM

当前版本先实现配置界面和保存逻辑。真实通讯库可以在后续新增 `EapClient` 类接入，例如连接、Select.req、LinkTest、事件上报等逻辑。
