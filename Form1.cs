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
    private readonly List<string> _sessionCodes = [];

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
    private Button _btnStartScan = null!;

    private string? _currentBatchId;
    private int _targetScanCount;
    private DateTime _scanDeadline;

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
    }

    private void BuildUi()
    {
        Text = "芯联键合上位机";
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 251);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        var title = new Label
        {
            Text = "芯联键合上位机",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 21F, FontStyle.Bold),
            ForeColor = Color.FromArgb(21, 35, 56),
            Location = new Point(0, 8),
        };
        var subTitle = new Label
        {
            Text = "PLC Modbus | EAP SECS/GEM | Keyence Serial Scanner | SQLite Trace",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(92, 107, 129),
            Location = new Point(3, 47),
        };
        header.Controls.Add(title);
        header.Controls.Add(subTitle);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10F),
            Padding = new Point(18, 8),
        };
        tabs.TabPages.Add(BuildDashboardTab());
        tabs.TabPages.Add(BuildPlcTab());
        tabs.TabPages.Add(BuildEapTab());
        tabs.TabPages.Add(BuildScannerTab());
        tabs.TabPages.Add(BuildDataTab());
        tabs.TabPages.Add(BuildLogsTab());

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);
    }

    private TabPage BuildDashboardTab()
    {
        var page = CreatePage("监控");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblPlcStatus = StatusPill("未连接");
        _lblEapStatus = StatusPill("未连接");
        _lblScannerStatus = StatusPill("未连接");
        layout.Controls.Add(StatusCard("PLC 通讯", _lblPlcStatus, "Modbus TCP 报警回写"), 0, 0);
        layout.Controls.Add(StatusCard("EAP 通讯", _lblEapStatus, "SECS/GEM 参数配置"), 1, 0);
        layout.Controls.Add(StatusCard("扫码枪", _lblScannerStatus, "基恩士串口扫码"), 2, 0);

        var scanPanel = CardPanel();
        scanPanel.Dock = DockStyle.Fill;
        scanPanel.Padding = new Padding(18);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 70, FlowDirection = FlowDirection.LeftToRight };
        _btnStartScan = PrimaryButton("开始四码扫描");
        _btnStartScan.Click += (_, _) => StartScanSession(4);
        var btnSimulate = SecondaryButton("模拟扫码");
        btnSimulate.Click += (_, _) => HandleScannerCode($"SIM-{DateTime.Now:HHmmssfff}");
        _lblScanProgress = new Label
        {
            Text = "未开始",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 360,
            Height = 42,
            ForeColor = Color.FromArgb(48, 63, 84),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
        };
        flow.Controls.Add(_btnStartScan);
        flow.Controls.Add(btnSimulate);
        flow.Controls.Add(_lblScanProgress);

        _gridScans = BuildGrid();
        _gridScans.DataSource = _scanBinding;
        scanPanel.Controls.Add(_gridScans);
        scanPanel.Controls.Add(flow);
        layout.SetColumnSpan(scanPanel, 3);
        layout.Controls.Add(scanPanel, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPlcTab()
    {
        var page = CreatePage("PLC配置");
        var panel = CardPanel();
        panel.Dock = DockStyle.Top;
        panel.Height = 310;
        panel.Padding = new Padding(24);

        var grid = FormGrid(2);
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

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft };
        var save = PrimaryButton("保存配置");
        save.Click += (_, _) => SaveSettingsFromUi();
        var test = SecondaryButton("测试报警回写");
        test.Click += async (_, _) => await WriteAlarmAsync("手动测试报警回写");
        buttons.Controls.Add(save);
        buttons.Controls.Add(test);

        panel.Controls.Add(grid);
        panel.Controls.Add(buttons);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildEapTab()
    {
        var page = CreatePage("EAP SECS/GEM");
        var panel = CardPanel();
        panel.Dock = DockStyle.Top;
        panel.Height = 350;
        panel.Padding = new Padding(24);

        var grid = FormGrid(2);
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

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft };
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
        buttons.Controls.Add(save);
        buttons.Controls.Add(test);

        panel.Controls.Add(grid);
        panel.Controls.Add(buttons);
        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildScannerTab()
    {
        var page = CreatePage("扫码枪配置");
        var panel = CardPanel();
        panel.Dock = DockStyle.Top;
        panel.Height = 360;
        panel.Padding = new Padding(24);

        var grid = FormGrid(2);
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
        AddField(grid, "四码超时(秒)", _numScanTimeout);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft };
        var save = PrimaryButton("保存配置");
        save.Click += (_, _) => SaveSettingsFromUi();
        _btnOpenScanner = SecondaryButton("打开串口");
        _btnOpenScanner.Click += (_, _) => ToggleScanner();
        var refresh = SecondaryButton("刷新串口");
        refresh.Click += (_, _) => RefreshSerialPorts();
        buttons.Controls.Add(save);
        buttons.Controls.Add(_btnOpenScanner);
        buttons.Controls.Add(refresh);

        panel.Controls.Add(grid);
        panel.Controls.Add(buttons);
        page.Controls.Add(panel);
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

    private void StartScanSession(int count)
    {
        SaveSettingsFromUi();
        _targetScanCount = count;
        _sessionCodes.Clear();
        _currentBatchId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        _scanDeadline = DateTime.Now.AddSeconds(_settings.Scanner.TimeoutSeconds);
        _scanTimeoutTimer.Start();
        UpdateScanProgress();
        _logger.Info($"开始扫码批次：{_currentBatchId}，目标数量：{count}。");
        RefreshLogGrid();
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
            _ = WriteAlarmAsync("扫码枪返回空码");
            return;
        }

        var batchId = _currentBatchId ?? DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var position = _sessionCodes.Count + 1;
        _repository.Insert(new ScanRecord(0, DateTime.Now, batchId, position, code, "OK"));
        _logger.Info($"收到产品码：批次={batchId}，位置={position}，码={code}");

        if (_currentBatchId is not null)
        {
            _sessionCodes.Add(code);
            if (_sessionCodes.Count >= _targetScanCount)
            {
                _scanTimeoutTimer.Stop();
                _currentBatchId = null;
                _logger.Info("四码扫描完成。");
            }
            UpdateScanProgress();
        }

        RefreshScanGrid();
        RefreshLogGrid();
    }

    private async void ScanTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentBatchId is null || DateTime.Now < _scanDeadline)
        {
            return;
        }

        _scanTimeoutTimer.Stop();
        var missing = Math.Max(0, _targetScanCount - _sessionCodes.Count);
        var batch = _currentBatchId;
        _currentBatchId = null;
        _repository.Insert(new ScanRecord(0, DateTime.Now, batch, _sessionCodes.Count + 1, string.Empty, $"NG: 超时未扫到{missing}个码"));
        _logger.Warn($"扫码超时：批次={batch}，已扫={_sessionCodes.Count}，缺少={missing}。");
        UpdateScanProgress();
        RefreshScanGrid();
        RefreshLogGrid();
        await WriteAlarmAsync("扫码超时或未扫到码");
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

    private void UpdateScanProgress()
    {
        _lblScanProgress.Text = _currentBatchId is null
            ? "未开始"
            : $"批次 {_currentBatchId}：{_sessionCodes.Count}/{_targetScanCount}";
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

    private static Panel StatusCard(string title, Label status, string caption)
    {
        var panel = CardPanel();
        panel.Dock = DockStyle.Fill;
        panel.Padding = new Padding(20);
        var titleLabel = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 39, 63),
            Location = new Point(20, 20),
        };
        status.Location = new Point(20, 62);
        var captionLabel = new Label
        {
            Text = caption,
            AutoSize = true,
            ForeColor = Color.FromArgb(108, 121, 140),
            Location = new Point(20, 104),
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
        };

        for (var i = 0; i < columns; i++)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        }

        return grid;
    }

    private static void AddField(TableLayoutPanel grid, string label, Control input)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var column = (row % 2) * 2;
        var actualRow = row / 2;
        if (grid.RowStyles.Count <= actualRow)
        {
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
        input.Margin = new Padding(0, 8, 18, 8);
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
            Margin = new Padding(8),
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
