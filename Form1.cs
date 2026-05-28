using System.Data;
using System.Drawing.Drawing2D;
using System.IO.Ports;

namespace HostComputerApp;

public partial class Form1 : Form
{
    private readonly AppSettings _settings;
    private readonly ScanRecordRepository _repository;
    private readonly AppLogger _logger;
    private readonly SerialPortManager _scanner;
    private readonly BindingSource _scanBinding = new();
    private readonly BindingSource _logBinding = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new();
    private readonly System.Windows.Forms.Timer _scanTimeoutTimer = new();
    private readonly System.Windows.Forms.Timer _plcPollTimer = new();
    private readonly List<Button> _navButtons = [];

    private NoHeaderTabControl _mainTabs = null!;
    private TextBox _txtPlcIp = null!;
    private NumericUpDown _numPlcPort = null!;
    private NumericUpDown _numPlcUnit = null!;
    private TextBox _txtAlarmRegister = null!;
    private NumericUpDown _numAlarmValue = null!;
    private ComboBox _cmbSerialPort = null!;
    private ComboBox _cmbBaudRate = null!;
    private ComboBox _cmbParity = null!;
    private ComboBox _cmbStopBits = null!;
    private NumericUpDown _numDataBits = null!;
    private NumericUpDown _numScanTimeout = null!;
    private TextBox _txtEapIp = null!;
    private NumericUpDown _numEapPort = null!;
    private TextBox _txtDeviceId = null!;
    private TextBox _txtEquipmentId = null!;
    private CheckBox _chkEapActive = null!;
    private Label _lblPlcStatus = null!;
    private Label _lblEapStatus = null!;
    private Label _lblScannerStatus = null!;
    private Label _lblScanProgress = null!;
    private DataGridView _gridScans = null!;
    private DataGridView _gridLogs = null!;
    private Button _btnOpenScanner = null!;
    private Button _btnRescan = null!;
    private Button _btnManualUpload = null!;
    private Button _btnSkip = null!;
    private TextBox _txtManualCode = null!;

    private bool _plcPollBusy;
    private bool _scanInProgress;
    private bool _manualActionRequired;
    private int _currentPortId;
    private DateTime _scanDeadline;
    private DateTime _lastPlcPollErrorLog = DateTime.MinValue;

    public Form1()
    {
        InitializeComponent();

        _logger = new AppLogger();
        _settings = AppSettings.Load(_logger);
        _repository = new ScanRecordRepository(_logger);
        _scanner = new SerialPortManager(_logger);
        _scanner.CodeReceived += Scanner_CodeReceived;
        _scanner.StatusChanged += (_, status) => SafeUi(() => SetStatus(_lblScannerStatus, status, status.Contains("已连接")));

        BuildUi();
        LoadSettingsToUi();
        RefreshScanGrid();
        RefreshLogGrid();

        _clockTimer.Interval = 1000;
        _clockTimer.Tick += (_, _) => RefreshLogGrid();
        _clockTimer.Start();

        _scanTimeoutTimer.Interval = 300;
        _scanTimeoutTimer.Tick += ScanTimeoutTimer_Tick;

        _plcPollTimer.Interval = 500;
        _plcPollTimer.Tick += PlcPollTimer_Tick;
        _plcPollTimer.Start();
    }

    private void BuildUi()
    {
        Text = "芯联键合上位机";
        MinimumSize = new Size(1180, 760);
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 251);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var title = new Label
        {
            Text = "芯联键合上位机",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
            ForeColor = Color.FromArgb(21, 35, 56),
            Location = new Point(0, 3),
        };
        var subTitle = new Label
        {
            Text = "PLC Modbus | EAP SECS/GEM | Honeywell Serial Scanner | SQLite Trace",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(92, 107, 129),
            Location = new Point(3, 38),
        };
        header.Controls.Add(title);
        header.Controls.Add(subTitle);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _mainTabs = new NoHeaderTabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10F),
        };
        _mainTabs.TabPages.Add(BuildDashboardTab());
        _mainTabs.TabPages.Add(BuildPlcTab());
        _mainTabs.TabPages.Add(BuildEapTab());
        _mainTabs.TabPages.Add(BuildScannerTab());
        _mainTabs.TabPages.Add(BuildDataTab());
        _mainTabs.TabPages.Add(BuildLogsTab());

        body.Controls.Add(BuildSideMenu(), 0, 0);
        body.Controls.Add(_mainTabs, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        Controls.Add(root);
    }

    private Panel BuildSideMenu()
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(23, 36, 58),
            Padding = new Padding(10, 14, 10, 12),
            Margin = new Padding(0, 0, 10, 0),
        };

        var title = new Label
        {
            Text = "功能菜单",
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = Color.FromArgb(174, 187, 207),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        panel.Controls.Add(title);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 292,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
        };

        AddNavButton(nav, 0, "▣", "监控总览");
        AddNavButton(nav, 1, "↔", "PLC 配置");
        AddNavButton(nav, 2, "◇", "EAP SECS/GEM");
        AddNavButton(nav, 3, "⌁", "扫码枪配置");
        AddNavButton(nav, 4, "▦", "扫码数据");
        AddNavButton(nav, 5, "≡", "本地日志");
        panel.Controls.Add(nav);

        var footer = new Label
        {
            Text = "SQLite 本地追溯\nModbus TCP 报警",
            Dock = DockStyle.Bottom,
            Height = 52,
            ForeColor = Color.FromArgb(135, 151, 176),
            Font = new Font("Microsoft YaHei UI", 8.5F),
            TextAlign = ContentAlignment.BottomLeft,
        };
        panel.Controls.Add(footer);

        SetActiveNav(0);
        return panel;
    }

    private void AddNavButton(FlowLayoutPanel panel, int pageIndex, string icon, string text)
    {
        var button = new Button
        {
            Text = $"{icon}  {text}",
            Tag = pageIndex,
            Width = 150,
            Height = 38,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(219, 228, 242),
            BackColor = Color.FromArgb(23, 36, 58),
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(14, 0, 0, 0),
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) =>
        {
            _mainTabs.SelectedIndex = pageIndex;
            SetActiveNav(pageIndex);
        };
        _navButtons.Add(button);
        panel.Controls.Add(button);
    }

    private void SetActiveNav(int pageIndex)
    {
        foreach (var button in _navButtons)
        {
            var active = button.Tag is int index && index == pageIndex;
            button.BackColor = active ? Color.FromArgb(37, 99, 235) : Color.FromArgb(23, 36, 58);
            button.ForeColor = active ? Color.White : Color.FromArgb(219, 228, 242);
        }
    }

    private TabPage BuildDashboardTab()
    {
        var page = CreatePage("监控");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblPlcStatus = StatusPill("未连接");
        _lblEapStatus = StatusPill("未连接");
        _lblScannerStatus = StatusPill("未连接");
        var statusFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        statusFlow.Controls.Add(StatusCard("PLC 通讯", _lblPlcStatus, "D50触发后读取D52 / D51结果"));
        statusFlow.Controls.Add(StatusCard("EAP 通讯", _lblEapStatus, "SECS/GEM 参数配置"));
        statusFlow.Controls.Add(StatusCard("扫码枪", _lblScannerStatus, "霍尼韦尔串口扫码"));
        layout.Controls.Add(statusFlow, 0, 0);

        var scanPanel = CardPanel();
        scanPanel.Dock = DockStyle.Fill;
        scanPanel.Padding = new Padding(14);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 104, FlowDirection = FlowDirection.LeftToRight };
        var btnSimulate = SecondaryButton("模拟扫码");
        btnSimulate.Click += (_, _) => HandleScannerCode($"SIM-{DateTime.Now:HHmmssfff}");
        _txtManualCode = TextBox("");
        _txtManualCode.Width = 210;
        _txtManualCode.PlaceholderText = "手动输入产品码";
        _btnRescan = SecondaryButton("重新扫码");
        _btnRescan.Click += async (_, _) => await HandleManualActionAsync("重新扫码", _settings.Plc.RescanCoil, null, false);
        _btnManualUpload = PrimaryButton("上传手输码");
        _btnManualUpload.Click += async (_, _) => await UploadManualCodeAsync();
        _btnSkip = SecondaryButton("跳过当前位");
        _btnSkip.Click += async (_, _) => await HandleManualActionAsync("跳过当前扫码位", _settings.Plc.SkipCoil, string.Empty, true);
        _lblScanProgress = new Label
        {
            Text = "等待PLC触发 D50=1",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 420,
            Height = 42,
            ForeColor = Color.FromArgb(48, 63, 84),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        flow.Controls.Add(btnSimulate);
        flow.Controls.Add(_btnRescan);
        flow.Controls.Add(_txtManualCode);
        flow.Controls.Add(_btnManualUpload);
        flow.Controls.Add(_btnSkip);
        flow.Controls.Add(_lblScanProgress);
        SetManualActionsEnabled(false);

        _gridScans = BuildGrid();
        _gridScans.DataSource = _scanBinding;
        scanPanel.Controls.Add(_gridScans);
        scanPanel.Controls.Add(flow);
        layout.Controls.Add(scanPanel, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPlcTab()
    {
        var page = CreatePage("PLC配置");
        page.AutoScroll = false;
        var layout = ConfigPageLayout();
        var panel = ConfigPanel("PLC Modbus TCP", "配置PLC连接参数。D50触发扫码，D52表示PORTID，D51写入扫码成功。");

        var grid = FormGrid(1);
        _txtPlcIp = TextBox("192.168.1.10");
        _numPlcPort = Number(1, 65535, 502);
        _numPlcUnit = Number(1, 247, 1);
        _txtAlarmRegister = TextBox("D3000");
        _numAlarmValue = Number(0, 65535, 1);

        AddField(grid, "PLC IP", _txtPlcIp);
        AddField(grid, "Modbus端口", _numPlcPort);
        AddField(grid, "站号 UnitId", _numPlcUnit);
        AddField(grid, "报警寄存器", _txtAlarmRegister);
        AddField(grid, "报警写入值", _numAlarmValue);

        var buttons = ActionBar();
        var save = PrimaryButton("保存配置");
        save.Click += (_, _) => SaveSettingsFromUi();
        var test = SecondaryButton("测试报警回写");
        test.Click += async (_, _) => await WriteAlarmAsync("手动测试报警回写");
        buttons.Controls.Add(test);
        buttons.Controls.Add(save);

        AddConfigBody(panel, grid, buttons);
        layout.Controls.Add(panel, 0, 0);
        layout.Controls.Add(InfoPanel("扫码握手", ["D50=1：PLC触发单个产品码扫码", "只有D50触发后才读取D52", "触发后上位机复位D50=0", "D52：当前PORTID，取值1/2/3/4", "扫码成功后D51=1", "异常人工处理：M838重扫，M839手输上传，M835跳过"]), 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildEapTab()
    {
        var page = CreatePage("EAP SECS/GEM");
        page.AutoScroll = false;
        var layout = ConfigPageLayout();
        var panel = ConfigPanel("EAP SECS/GEM", "保存EAP连接参数，后续接入真实SECS/GEM库时直接读取这些配置。");

        var grid = FormGrid(1);
        _txtEapIp = TextBox("127.0.0.1");
        _numEapPort = Number(1, 65535, 5000);
        _txtDeviceId = TextBox("0");
        _txtEquipmentId = TextBox("BOND-001");
        _chkEapActive = new CheckBox { Text = "主动连接模式", AutoSize = true, ForeColor = Color.FromArgb(48, 63, 84) };

        AddField(grid, "EAP IP", _txtEapIp);
        AddField(grid, "EAP端口", _numEapPort);
        AddField(grid, "Device ID", _txtDeviceId);
        AddField(grid, "Equipment ID", _txtEquipmentId);
        AddField(grid, "连接模式", _chkEapActive);

        var buttons = ActionBar();
        var save = PrimaryButton("保存配置");
        save.Click += (_, _) => SaveSettingsFromUi();
        var test = SecondaryButton("连接测试");
        test.Click += (_, _) =>
        {
            SaveSettingsFromUi();
            SetStatus(_lblEapStatus, "配置已保存", true);
            _logger.Info("SECS/GEM配置已保存，实际通讯库可在EapClient中接入。");
            RefreshLogGrid();
        };
        buttons.Controls.Add(test);
        buttons.Controls.Add(save);

        AddConfigBody(panel, grid, buttons);
        layout.Controls.Add(panel, 0, 0);
        layout.Controls.Add(InfoPanel("SECS/GEM预留", ["连接模式：主动/被动", "设备号：Device ID", "设备名：Equipment ID", "后续可接入 S1F13、LinkTest、事件上报"]), 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildScannerTab()
    {
        var page = CreatePage("扫码枪配置");
        page.AutoScroll = false;
        var layout = ConfigPageLayout();
        var panel = ConfigPanel("霍尼韦尔扫码枪", "配置串口参数。PLC触发后发送 LON+CR 指令，等待扫码枪返回产品码。");

        var grid = FormGrid(1);
        _cmbSerialPort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Height = 32 };
        _cmbSerialPort.Items.AddRange(SerialPort.GetPortNames().OrderBy(x => x).Cast<object>().ToArray());
        _cmbBaudRate = Combo(["9600", "19200", "38400", "57600", "115200"]);
        _cmbParity = Combo(Enum.GetNames<Parity>());
        _cmbStopBits = Combo(Enum.GetNames<StopBits>().Where(x => x != nameof(StopBits.None)).ToArray());
        _numDataBits = Number(5, 8, 8);
        _numScanTimeout = Number(1, 120, 10);

        AddField(grid, "串口号", _cmbSerialPort);
        AddField(grid, "波特率", _cmbBaudRate);
        AddField(grid, "校验位", _cmbParity);
        AddField(grid, "停止位", _cmbStopBits);
        AddField(grid, "数据位", _numDataBits);
        AddField(grid, "单码超时(秒)", _numScanTimeout);

        var buttons = ActionBar();
        var save = PrimaryButton("保存配置");
        save.Click += (_, _) => SaveSettingsFromUi();
        _btnOpenScanner = SecondaryButton("打开串口");
        _btnOpenScanner.Click += (_, _) => ToggleScanner();
        var refresh = SecondaryButton("刷新串口");
        refresh.Click += (_, _) => RefreshSerialPorts();
        buttons.Controls.Add(refresh);
        buttons.Controls.Add(_btnOpenScanner);
        buttons.Controls.Add(save);

        AddConfigBody(panel, grid, buttons);
        layout.Controls.Add(panel, 0, 0);
        layout.Controls.Add(InfoPanel("扫码流程", ["1. PLC写D50=1触发扫码", "2. 上位机此时才读D52作为PORTID并复位D50", "3. 串口发送 LON + CR：4C 4F 4E 0D", "4. 收到产品码后保存SQLite并D51=1", "5. 无码时弹窗，开放重扫/手输/跳过按钮"]), 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildDataTab()
    {
        var page = CreatePage("扫码数据");
        var panel = CardPanel();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(18);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, FlowDirection = FlowDirection.RightToLeft };
        var refresh = SecondaryButton("刷新");
        refresh.Click += (_, _) => RefreshScanGrid();
        toolbar.Controls.Add(refresh);

        var grid = BuildGrid();
        grid.DataSource = _scanBinding;
        panel.Controls.Add(grid);
        panel.Controls.Add(toolbar);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildLogsTab()
    {
        var page = CreatePage("本地日志");
        var panel = CardPanel();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(18);
        _gridLogs = BuildGrid();
        _gridLogs.DataSource = _logBinding;
        panel.Controls.Add(_gridLogs);
        page.Controls.Add(panel);
        return page;
    }

    private void LoadSettingsToUi()
    {
        _txtPlcIp.Text = _settings.Plc.IpAddress;
        _numPlcPort.Value = _settings.Plc.Port;
        _numPlcUnit.Value = _settings.Plc.UnitId;
        _txtAlarmRegister.Text = _settings.Plc.AlarmRegister;
        _numAlarmValue.Value = _settings.Plc.AlarmValue;
        _txtEapIp.Text = _settings.Eap.IpAddress;
        _numEapPort.Value = _settings.Eap.Port;
        _txtDeviceId.Text = _settings.Eap.DeviceId;
        _txtEquipmentId.Text = _settings.Eap.EquipmentId;
        _chkEapActive.Checked = _settings.Eap.ActiveMode;
        SelectCombo(_cmbSerialPort, _settings.Scanner.PortName);
        SelectCombo(_cmbBaudRate, _settings.Scanner.BaudRate.ToString());
        SelectCombo(_cmbParity, _settings.Scanner.Parity);
        SelectCombo(_cmbStopBits, _settings.Scanner.StopBits);
        _numDataBits.Value = _settings.Scanner.DataBits;
        _numScanTimeout.Value = _settings.Scanner.TimeoutSeconds;
    }

    private void SaveSettingsFromUi()
    {
        _settings.Plc.IpAddress = _txtPlcIp.Text.Trim();
        _settings.Plc.Port = (int)_numPlcPort.Value;
        _settings.Plc.UnitId = (byte)_numPlcUnit.Value;
        _settings.Plc.AlarmRegister = _txtAlarmRegister.Text.Trim();
        _settings.Plc.AlarmValue = (ushort)_numAlarmValue.Value;
        _settings.Eap.IpAddress = _txtEapIp.Text.Trim();
        _settings.Eap.Port = (int)_numEapPort.Value;
        _settings.Eap.DeviceId = _txtDeviceId.Text.Trim();
        _settings.Eap.EquipmentId = _txtEquipmentId.Text.Trim();
        _settings.Eap.ActiveMode = _chkEapActive.Checked;
        _settings.Scanner.PortName = _cmbSerialPort.Text;
        _settings.Scanner.BaudRate = int.Parse(_cmbBaudRate.Text);
        _settings.Scanner.Parity = _cmbParity.Text;
        _settings.Scanner.StopBits = _cmbStopBits.Text;
        _settings.Scanner.DataBits = (int)_numDataBits.Value;
        _settings.Scanner.TimeoutSeconds = (int)_numScanTimeout.Value;
        _settings.Save(_logger);
        _logger.Info("配置已保存。");
        RefreshLogGrid();
    }

    private void ToggleScanner()
    {
        try
        {
            if (_scanner.IsOpen)
            {
                _scanner.Close();
                _btnOpenScanner.Text = "打开串口";
                return;
            }

            SaveSettingsFromUi();
            _scanner.Open(_settings.Scanner);
            _btnOpenScanner.Text = "关闭串口";
        }
        catch (Exception ex)
        {
            SetStatus(_lblScannerStatus, "连接失败", false);
            _logger.Error($"扫码枪串口打开失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "串口连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshLogGrid();
        }
    }

    private void Scanner_CodeReceived(object? sender, string code)
    {
        SafeUi(() => HandleScannerCode(code));
    }

    private void HandleScannerCode(string code)
    {
        code = code.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MarkScanException("扫码枪返回空码");
            return;
        }

        var portId = _currentPortId is >= 1 and <= 4 ? _currentPortId : 0;
        _scanTimeoutTimer.Stop();
        _scanInProgress = false;
        _manualActionRequired = false;
        SetManualActionsEnabled(false);

        _repository.Insert(new ScanRecord(0, DateTime.Now, string.Empty, portId, code, "OK"));
        _logger.Info($"收到产品码：PORTID={portId}，码={code}");
        _ = WriteScanResultAsync();
        ReportS6F11(code);
        UpdateScanProgress($"PORTID {portId} 扫码完成");

        RefreshScanGrid();
        RefreshLogGrid();
    }

    private async void ScanTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        if (!_scanInProgress || DateTime.Now < _scanDeadline)
        {
            return;
        }

        _scanTimeoutTimer.Stop();
        await Task.CompletedTask;
        MarkScanException("扫码超时或未扫到产品码");
    }

    private async void PlcPollTimer_Tick(object? sender, EventArgs e)
    {
        if (_plcPollBusy || _scanInProgress || _manualActionRequired)
        {
            return;
        }

        _plcPollBusy = true;
        try
        {
            using var client = new ModbusTcpAlarmClient(_settings.Plc.IpAddress, _settings.Plc.Port, _settings.Plc.UnitId);
            var trigger = await client.ReadSingleRegisterAsync(_settings.Plc.ScanTriggerRegister, CancellationToken.None);
            SetStatus(_lblPlcStatus, "PLC监听中", true);
            if (trigger != 1)
            {
                return;
            }

            var portId = await client.ReadSingleRegisterAsync(_settings.Plc.PortIdRegister, CancellationToken.None);
            await client.WriteSingleRegisterAsync(_settings.Plc.ScanTriggerRegister, 0, CancellationToken.None);
            await StartPlcTriggeredScanAsync((int)portId);
        }
        catch (Exception ex)
        {
            SetStatus(_lblPlcStatus, "PLC监听失败", false);
            if ((DateTime.Now - _lastPlcPollErrorLog).TotalSeconds >= 5)
            {
                _lastPlcPollErrorLog = DateTime.Now;
                _logger.Error($"PLC扫码触发监听异常：{ex.Message}");
                RefreshLogGrid();
            }
        }
        finally
        {
            _plcPollBusy = false;
        }
    }

    private async Task StartPlcTriggeredScanAsync(int portId)
    {
        if (portId is < 1 or > 4)
        {
            _logger.Warn($"PLC触发扫码，但D52 PORTID无效：{portId}");
            MarkScanException($"PORTID无效：{portId}");
            return;
        }

        _currentPortId = portId;
        _scanInProgress = true;
        _manualActionRequired = false;
        SetManualActionsEnabled(false);
        _scanDeadline = DateTime.Now.AddSeconds(_settings.Scanner.TimeoutSeconds);
        _scanTimeoutTimer.Start();
        UpdateScanProgress($"PORTID {portId} 已触发，等待霍尼韦尔返回");
        _logger.Info($"PLC触发扫码：PORTID={portId}，已复位{_settings.Plc.ScanTriggerRegister}。");

        try
        {
            _scanner.SendTriggerCommand();
        }
        catch (Exception ex)
        {
            _logger.Error($"发送霍尼韦尔扫码指令失败：{ex.Message}");
            MarkScanException("发送霍尼韦尔扫码指令失败");
        }

        await Task.CompletedTask;
        RefreshLogGrid();
    }

    private void MarkScanException(string reason)
    {
        _scanInProgress = false;
        _manualActionRequired = true;
        _scanTimeoutTimer.Stop();
        SetManualActionsEnabled(true);
        UpdateScanProgress($"PORTID {_currentPortId} 扫码异常，等待人工确认");
        _logger.Warn($"{reason}，PORTID={_currentPortId}，请人工确认。");
        RefreshLogGrid();
        MessageBox.Show(this, "扫码异常，请人工确认！", "扫码异常", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private async Task WriteScanResultAsync()
    {
        try
        {
            using var client = new ModbusTcpAlarmClient(_settings.Plc.IpAddress, _settings.Plc.Port, _settings.Plc.UnitId);
            await client.WriteSingleRegisterAsync(_settings.Plc.ScanResultRegister, 1, CancellationToken.None);
            SetStatus(_lblPlcStatus, "扫码结果已写入", true);
            _logger.Info($"已写入PLC扫码结果：{_settings.Plc.ScanResultRegister}=1");
        }
        catch (Exception ex)
        {
            SetStatus(_lblPlcStatus, "结果写入失败", false);
            _logger.Error($"PLC扫码结果写入失败：{ex.Message}");
        }

        RefreshLogGrid();
    }

    private async Task UploadManualCodeAsync()
    {
        var code = _txtManualCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show(this, "请输入产品码后再上传。", "手动上传", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        await HandleManualActionAsync("手动输入产品码", _settings.Plc.ManualUploadCoil, code, true);
    }

    private async Task HandleManualActionAsync(string action, string coil, string? productCode, bool finishCurrentPosition)
    {
        if (!_manualActionRequired)
        {
            return;
        }

        try
        {
            using var client = new ModbusTcpAlarmClient(_settings.Plc.IpAddress, _settings.Plc.Port, _settings.Plc.UnitId);
            await client.WriteSingleCoilAsync(coil, true, CancellationToken.None);
            _logger.Warn($"{action}，已写入PLC：{coil}=1，PORTID={_currentPortId}");

            if (finishCurrentPosition)
            {
                var status = string.IsNullOrEmpty(productCode) ? "SKIP" : "MANUAL";
                if (!string.IsNullOrWhiteSpace(productCode))
                {
                    _repository.Insert(new ScanRecord(0, DateTime.Now, string.Empty, _currentPortId, productCode, status));
                    ReportS6F11(productCode);
                    RefreshScanGrid();
                }

                _manualActionRequired = false;
                SetManualActionsEnabled(false);
                UpdateScanProgress($"PORTID {_currentPortId} 人工处理完成：{status}");
                _txtManualCode.Clear();
            }
            else
            {
                _manualActionRequired = false;
                SetManualActionsEnabled(false);
                UpdateScanProgress($"PORTID {_currentPortId} 已请求重新扫码");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"{action}失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, action, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        RefreshLogGrid();
    }

    private void ReportS6F11(string productCode)
    {
        SetStatus(_lblEapStatus, "S6F11已记录", true);
        _logger.Info($"S6F11上报：产品码={productCode}。SML=<L[3] <U4 DATAID> <U4 CEID> <L[1] <L[2] <U4 RPTID> <L[1] <A PRODUCTCODE>>>>>>。当前为预留日志，接入SECS/GEM库后在此发送。");
    }

    private async Task WriteAlarmAsync(string reason)
    {
        SaveSettingsFromUi();
        try
        {
            using var client = new ModbusTcpAlarmClient(_settings.Plc.IpAddress, _settings.Plc.Port, _settings.Plc.UnitId);
            await client.WriteSingleRegisterAsync(_settings.Plc.AlarmRegister, _settings.Plc.AlarmValue, CancellationToken.None);
            SetStatus(_lblPlcStatus, "报警已写入", true);
            _logger.Warn($"{reason}，已写入PLC：{_settings.Plc.AlarmRegister}={_settings.Plc.AlarmValue}");
        }
        catch (Exception ex)
        {
            SetStatus(_lblPlcStatus, "写入失败", false);
            _logger.Error($"{reason}，PLC报警写入失败：{ex.Message}");
        }

        RefreshLogGrid();
    }

    private void RefreshScanGrid()
    {
        _scanBinding.DataSource = _repository.QueryLatest(500);
    }

    private void RefreshLogGrid()
    {
        _logBinding.DataSource = _logger.ReadLatest(300);
    }

    private void RefreshSerialPorts()
    {
        var selected = _cmbSerialPort.Text;
        _cmbSerialPort.Items.Clear();
        _cmbSerialPort.Items.AddRange(SerialPort.GetPortNames().OrderBy(x => x).Cast<object>().ToArray());
        SelectCombo(_cmbSerialPort, selected);
    }

    private void UpdateScanProgress(string? text = null)
    {
        _lblScanProgress.Text = text ?? "等待PLC触发 D50=1";
    }

    private void SetManualActionsEnabled(bool enabled)
    {
        if (_btnRescan is null || _btnManualUpload is null || _btnSkip is null || _txtManualCode is null)
        {
            return;
        }

        _btnRescan.Enabled = enabled;
        _btnManualUpload.Enabled = enabled;
        _btnSkip.Enabled = enabled;
        _txtManualCode.Enabled = enabled;
    }

    private static TabPage CreatePage(string text)
    {
        return new TabPage(text)
        {
            BackColor = Color.FromArgb(244, 247, 251),
            Padding = new Padding(12),
        };
    }

    private static Panel CardPanel() => new RoundedPanel
    {
        BackColor = Color.White,
        Margin = new Padding(8),
    };

    private static TableLayoutPanel ConfigPageLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 670));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    private static TableLayoutPanel ConfigPanel(string title, string description)
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            BackColor = Color.White,
            Padding = new Padding(24),
            Margin = new Padding(8, 8, 10, 8),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));

        var header = new Panel { Dock = DockStyle.Fill };
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 39, 63),
            Location = new Point(0, 0),
        };
        var descriptionLabel = new Label
        {
            Text = description,
            AutoSize = false,
            Width = 560,
            Height = 30,
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.FromArgb(91, 106, 126),
            Location = new Point(0, 34),
        };
        header.Controls.Add(titleLabel);
        header.Controls.Add(descriptionLabel);
        outer.Controls.Add(header, 0, 0);
        return outer;
    }

    private static Panel InfoPanel(string title, string[] lines)
    {
        var panel = CardPanel();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(22);
        panel.Margin = new Padding(0, 8, 8, 8);

        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 39, 63),
        };
        panel.Controls.Add(titleLabel);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 210,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0),
        };

        foreach (var line in lines)
        {
            content.Controls.Add(new Label
            {
                Text = line,
                AutoSize = false,
                Width = 310,
                Height = 30,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.FromArgb(71, 86, 107),
                TextAlign = ContentAlignment.MiddleLeft,
            });
        }

        var status = new Label
        {
            Text = "当前状态：等待操作",
            Dock = DockStyle.Bottom,
            Height = 42,
            BackColor = Color.FromArgb(238, 243, 249),
            ForeColor = Color.FromArgb(37, 73, 130),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
        };
        panel.Controls.Add(status);
        panel.Controls.Add(content);
        return panel;
    }

    private static void AddConfigBody(TableLayoutPanel panel, Control form, Control actions)
    {
        panel.Controls.Add(form, 0, 1);
        panel.Controls.Add(actions, 0, 2);
    }

    private static FlowLayoutPanel ActionBar()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
        };
    }

    private static Panel StatusCard(string title, Label status, string caption)
    {
        var panel = CardPanel();
        panel.Width = 210;
        panel.Height = 126;
        panel.Padding = new Padding(18);
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 39, 63),
            Location = new Point(18, 18),
        };
        status.Location = new Point(18, 56);
        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = true,
            ForeColor = Color.FromArgb(108, 121, 140),
            Location = new Point(18, 96),
        };
        panel.Controls.Add(titleLabel);
        panel.Controls.Add(status);
        panel.Controls.Add(captionLabel);
        return panel;
    }

    private static Label StatusPill(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = 110,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            BackColor = Color.FromArgb(230, 236, 245),
            ForeColor = Color.FromArgb(82, 96, 116),
        };
    }

    private static void SetStatus(Label label, string text, bool ok)
    {
        label.Text = text;
        label.BackColor = ok ? Color.FromArgb(220, 245, 232) : Color.FromArgb(255, 228, 226);
        label.ForeColor = ok ? Color.FromArgb(25, 118, 75) : Color.FromArgb(190, 49, 45);
    }

    private static DataGridView BuildGrid()
    {
        return new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(238, 243, 249),
                ForeColor = Color.FromArgb(40, 54, 75),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            },
        };
    }

    private static TableLayoutPanel FormGrid(int columns)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = columns * 2,
            AutoSize = true,
            Tag = columns,
        };

        for (var i = 0; i < columns; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, columns == 1 ? 500 : 240));
        }

        return grid;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control input)
    {
        var columns = grid.Tag is int count && count > 0 ? count : 1;
        var fieldIndex = grid.Controls.Count / 2;
        var column = fieldIndex % columns * 2;
        var actualRow = fieldIndex / columns;
        if (grid.RowStyles.Count <= actualRow)
        {
            grid.RowCount = actualRow + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        }

        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(72, 86, 105),
        };
        input.Dock = DockStyle.Fill;
        input.Margin = new Padding(0, 7, 0, 7);
        grid.Controls.Add(lbl, column, actualRow);
        grid.Controls.Add(input, column + 1, actualRow);
    }

    private static TextBox TextBox(string text) => new()
    {
        Text = text,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static NumericUpDown Number(decimal min, decimal max, decimal value) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        BorderStyle = BorderStyle.FixedSingle,
    };

    private static ComboBox Combo(IEnumerable<string> values)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(values.Cast<object>().ToArray());
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    private static Button PrimaryButton(string text) => Button(text, Color.FromArgb(28, 105, 212), Color.White);

    private static Button SecondaryButton(string text) => Button(text, Color.FromArgb(232, 239, 249), Color.FromArgb(35, 61, 95));

    private static Button Button(string text, Color back, Color fore)
    {
        return new Button
        {
            Text = text,
            Width = 126,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            Margin = new Padding(0, 0, 10, 0),
        };
    }

    private static void SelectCombo(ComboBox combo, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
            return;
        }

        var index = combo.Items.IndexOf(value);
        if (index >= 0)
        {
            combo.SelectedIndex = index;
        }
        else if (combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _scanner.Dispose();
        base.OnFormClosing(e);
    }
}

internal sealed class RoundedPanel : Panel
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var path = new GraphicsPath();
        var radius = 8;
        var rect = ClientRectangle;
        rect.Width -= 1;
        rect.Height -= 1;
        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
        using var pen = new Pen(Color.FromArgb(223, 230, 240));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class NoHeaderTabControl : TabControl
{
    protected override void WndProc(ref Message m)
    {
        const int tcmAdjustRect = 0x1328;
        if (m.Msg == tcmAdjustRect && !DesignMode)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }
}
