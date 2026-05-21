namespace HostComputerApp;

public partial class Form1
{
    private readonly List<Button> _uiShellButtons = [];

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyCompactUiShell();
    }

    private void ApplyCompactUiShell()
    {
        if (Controls.Find("CompactShell", true).Length > 0)
        {
            return;
        }

        var sourceTabs = FindFirst<TabControl>(this);
        if (sourceTabs is null || sourceTabs.GetType().Name == "NoHeaderTabControl")
        {
            return;
        }

        var pages = sourceTabs.TabPages.Cast<TabPage>().ToList();
        foreach (var page in pages)
        {
            sourceTabs.TabPages.Remove(page);
            page.Padding = new Padding(10);
            page.AutoScroll = false;
            TightenPage(page);
        }

        var tabs = new CompactHiddenTabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 10F),
        };
        tabs.TabPages.AddRange(pages.ToArray());

        var shell = new TableLayoutPanel
        {
            Name = "CompactShell",
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(244, 247, 251),
            Padding = new Padding(14),
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        shell.Controls.Add(BuildCompactMenu(tabs), 0, 0);
        shell.Controls.Add(tabs, 1, 0);

        Controls.Clear();
        Controls.Add(shell);

        LoadSettingsToUi();
        RefreshScanGrid();
        RefreshLogGrid();
    }

    private Panel BuildCompactMenu(TabControl tabs)
    {
        var panel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(23, 36, 58),
            Padding = new Padding(10, 14, 10, 12),
        };

        panel.Controls.Add(new Label
        {
            Text = "功能菜单",
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = Color.FromArgb(174, 187, 207),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
        });

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 300,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 10, 0, 0),
        };

        var icons = new[] { "▣", "↔", "◇", "⌁", "▦", "≡" };
        for (var i = 0; i < tabs.TabPages.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                Text = $"{icons[Math.Min(i, icons.Length - 1)]}  {tabs.TabPages[i].Text}",
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
                tabs.SelectedIndex = index;
                SetCompactMenuActive(index);
            };
            _uiShellButtons.Add(button);
            nav.Controls.Add(button);
        }

        panel.Controls.Add(nav);
        panel.Controls.Add(new Label
        {
            Text = "SQLite 本地追溯\nModbus TCP 报警",
            Dock = DockStyle.Bottom,
            Height = 52,
            ForeColor = Color.FromArgb(135, 151, 176),
            Font = new Font("Microsoft YaHei UI", 8.5F),
            TextAlign = ContentAlignment.BottomLeft,
        });

        SetCompactMenuActive(0);
        return panel;
    }

    private void SetCompactMenuActive(int pageIndex)
    {
        foreach (var button in _uiShellButtons)
        {
            var index = _uiShellButtons.IndexOf(button);
            var active = index == pageIndex;
            button.BackColor = active ? Color.FromArgb(37, 99, 235) : Color.FromArgb(23, 36, 58);
            button.ForeColor = active ? Color.White : Color.FromArgb(219, 228, 242);
        }
    }

    private static void TightenPage(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is TableLayoutPanel table)
            {
                table.Padding = new Padding(Math.Min(table.Padding.Left, 8));
            }

            if (control is Panel panel)
            {
                panel.Margin = new Padding(6);
                if (panel.Dock == DockStyle.Top && panel.Height >= 300)
                {
                    panel.Dock = DockStyle.Fill;
                }
            }

            if (control is Button button)
            {
                button.Height = Math.Max(button.Height, 36);
            }

            TightenPage(control);
        }
    }

    private static T? FindFirst<T>(Control root) where T : Control
    {
        foreach (Control control in root.Controls)
        {
            if (control is T matched)
            {
                return matched;
            }

            var child = FindFirst<T>(control);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }
}

internal sealed class CompactHiddenTabControl : TabControl
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
