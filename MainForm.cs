using System.Drawing.Drawing2D;

namespace SMT;

/// <summary>
/// SMT (Stock Monitoring Tool) — compact desktop stock ticker.
/// Single-line cyclic display: shows 1 stock at a time, cycles every 2 seconds.
/// Config file: config.txt in application directory.
/// </summary>
public class MainForm : Form
{
    // ── Window metrics ─────────────────────────────
    private const int DefaultWidth = 226;
    private const int DefaultHeight = 34;

    // ── State ──────────────────────────────────────
    private bool _isLocked = false;
    private bool _isPenetrating = false;
    private bool _isTopMost = true;
    private bool _isDragging = false;
    private Point _dragStartPoint;

    // ── Stock data ─────────────────────────────────
    private List<StockEntry> _configStocks = new();
    private readonly StockPriceService _priceService = new();
    private List<StockPrice> _prices = new();
    private int _currentIndex = 0;

    // ── Timers ─────────────────────────────────────
    private readonly System.Windows.Forms.Timer _fetchTimer = new();
    private readonly System.Windows.Forms.Timer _cycleTimer = new();

    // ── UI controls ────────────────────────────────
    private NotifyIcon _trayIcon = null!;
    private ContextMenuStrip _trayMenu = null!;
    private ToolStripMenuItem _menuLock = null!;
    private ToolStripMenuItem _menuPenetrate = null!;
    private ToolStripMenuItem _menuTopMost = null!;
    private ToolStripMenuItem _menuExit = null!;
    private Label _lblStock = null!;
    private ContextMenuStrip _windowMenu = null!;

    public MainForm()
    {
        InitializeForm();
        InitializeTrayIcon();
        InitializeDisplayLabel();
        InitializeTimers();
        LoadConfigAndStart();
    }

    // ══════════════════════════════════════════════════════
    //  Form Setup — compact single-line window
    // ══════════════════════════════════════════════════════
    private void InitializeForm()
    {
        this.Size = new Size(DefaultWidth, DefaultHeight);
        this.MinimumSize = new Size(160, DefaultHeight);
        this.MaximumSize = new Size(400, DefaultHeight);

        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.TopMost = true;

        var screen = Screen.PrimaryScreen;
        if (screen != null)
        {
            this.Location = new Point(
                screen.WorkingArea.Right - this.Width - 10,
                screen.WorkingArea.Top + 10);
        }

        this.BackColor = Color.FromArgb(244, 244, 244);
        this.Padding = new Padding(8, 0, 8, 0);
    }

    // ══════════════════════════════════════════════════════
    //  Display Label — single line, centered
    // ══════════════════════════════════════════════════════
    private void InitializeDisplayLabel()
    {
        _lblStock = new Label
        {
            Text = "右键添加股票 →",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(130, 130, 130),
            BackColor = Color.Transparent,
            Cursor = Cursors.Default
        };

        // Right-click menu on label
        _windowMenu = new ContextMenuStrip();
        var menuAdd = new ToolStripMenuItem("添加股票", null, OnAddStock);
        var menuManage = new ToolStripMenuItem("管理股票", null, OnManageStocks);
        _windowMenu.Items.Add(menuAdd);
        _windowMenu.Items.Add(menuManage);
        _lblStock.ContextMenuStrip = _windowMenu;

        // Drag support
        _lblStock.MouseDown += OnMouseDown;
        _lblStock.MouseMove += OnMouseMove;
        _lblStock.MouseUp += OnMouseUp;

        this.Controls.Add(_lblStock);
    }

    // ══════════════════════════════════════════════════════
    //  Timers: fetch prices (5s) + cycle display (2s)
    // ══════════════════════════════════════════════════════
    private void InitializeTimers()
    {
        // Fetch prices every 1 second (1 QPS)
        _fetchTimer.Interval = 1000;
        _fetchTimer.Tick += async (s, e) => await FetchPrices();
        _fetchTimer.Start();

        // Cycle to next stock every 2 seconds (only if multiple stocks)
        _cycleTimer.Interval = 2000;
        _cycleTimer.Tick += (s, e) => CycleToNext();
        _cycleTimer.Start();
    }

    // ══════════════════════════════════════════════════════
    //  System Tray Icon
    // ══════════════════════════════════════════════════════
    private void InitializeTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();

        _menuLock = new ToolStripMenuItem("窗口锁定") { CheckOnClick = true };
        _menuLock.Click += (s, e) => ToggleLock();

        _menuPenetrate = new ToolStripMenuItem("鼠标穿透") { CheckOnClick = true };
        _menuPenetrate.Click += (s, e) => TogglePenetration();

        _menuTopMost = new ToolStripMenuItem("窗口置顶") { CheckOnClick = true, Checked = true };
        _menuTopMost.Click += (s, e) => ToggleTopMost();

        _menuExit = new ToolStripMenuItem("退出", null, (s, e) => ExitApplication());

        _trayMenu.Items.Add(_menuLock);
        _trayMenu.Items.Add(_menuPenetrate);
        _trayMenu.Items.Add(_menuTopMost);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(_menuExit);

        _trayIcon = new NotifyIcon
        {
            Text = "SMT — 股票盯盘",
            Icon = CreateTrayIcon(),
            ContextMenuStrip = _trayMenu,
            Visible = true
        };

        _trayIcon.MouseDoubleClick += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                this.Show();
                this.Activate();
            }
        };
    }

    private Icon CreateTrayIcon()
    {
        int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var bgBrush = new SolidBrush(Color.FromArgb(44, 62, 80));
        g.FillEllipse(bgBrush, 1, 1, size - 2, size - 2);

        // Red = up (涨), Green = down (跌) — Chinese convention
        using var upPen = new Pen(Color.FromArgb(220, 50, 50), 2.5f);
        using var downPen = new Pen(Color.FromArgb(46, 180, 80), 2.5f);

        g.DrawLine(upPen, 6, 20, 14, 10);
        g.DrawLine(upPen, 14, 10, 22, 16);
        g.DrawLine(downPen, 22, 16, 28, 22);

        IntPtr hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        Win32API.DestroyIcon(hIcon);
        return icon;
    }

    // ══════════════════════════════════════════════════════
    //  Config & Price Fetch
    // ══════════════════════════════════════════════════════
    private void LoadConfigAndStart()
    {
        _configStocks = StockConfig.Load();
        _ = FetchPrices();
    }

    private async Task FetchPrices()
    {
        if (_configStocks.Count == 0)
        {
            ShowHint("右键添加股票 →");
            return;
        }

        _prices = await _priceService.FetchPricesAsync(_configStocks);

        // Clamp index
        if (_prices.Count == 0)
        {
            ShowHint("获取数据中...");
            return;
        }

        if (_currentIndex >= _prices.Count)
            _currentIndex = 0;

        RenderCurrentStock();
    }

    // ══════════════════════════════════════════════════════
    //  Single-Line Cyclic Display
    // ══════════════════════════════════════════════════════
    /// <summary>
    /// Switch to next stock (called every 2 seconds by _cycleTimer).
    /// If only 1 stock, do nothing (stay on that stock).
    /// </summary>
    private void CycleToNext()
    {
        if (_prices.Count <= 1) return;  // nothing to cycle

        _currentIndex = (_currentIndex + 1) % _prices.Count;
        RenderCurrentStock();
    }

    /// <summary>
    /// Render the current stock on the single display line.
    /// Format: "名称 ¥价格 ↑+涨跌幅%"
    /// Color: 涨=红色, 跌=绿色
    /// </summary>
    private void RenderCurrentStock()
    {
        if (this.InvokeRequired)
        {
            this.Invoke(() => RenderCurrentStock());
            return;
        }

        if (_prices.Count == 0) return;
        if (_currentIndex >= _prices.Count) _currentIndex = 0;

        var price = _prices[_currentIndex];

        if (price.CurrentPrice == 0)
        {
            ShowHint($"{price.Name} — 数据异常");
            return;
        }

        string arrow = price.IsUp ? "↑" : "↓";
        string sign = price.ChangePercent >= 0 ? "+" : "";
        string changeStr = $"{sign}{price.ChangePercent:F2}%";

        // Single line: "贵州茅台 ¥1850.00 ↑+2.35%"
        _lblStock.Text = $"{price.Name}  ¥{price.CurrentPrice:F2}  {arrow}{changeStr}";
        _lblStock.TextAlign = ContentAlignment.MiddleCenter;

        // Color: 涨→红色, 跌→绿色 (Chinese A-share convention)
        _lblStock.ForeColor = price.IsUp
            ? Color.FromArgb(220, 30, 30)   // red = up
            : Color.FromArgb(30, 160, 60);  // green = down
    }

    private void ShowHint(string text)
    {
        if (this.InvokeRequired)
        {
            this.Invoke(() => ShowHint(text));
            return;
        }

        _lblStock.Text = text;
        _lblStock.ForeColor = Color.FromArgb(130, 130, 130);
        _lblStock.TextAlign = ContentAlignment.MiddleCenter;
    }

    // ══════════════════════════════════════════════════════
    //  Drag to Move
    // ══════════════════════════════════════════════════════
    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (_isLocked) return;
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            _dragStartPoint = new Point(e.X, e.Y);
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDragging || _isLocked) return;
        if (e.Button == MouseButtons.Left)
        {
            this.Left += e.X - _dragStartPoint.X;
            this.Top += e.Y - _dragStartPoint.Y;
        }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        _isDragging = false;
    }

    // ══════════════════════════════════════════════════════
    //  Tray Menu Handlers
    // ══════════════════════════════════════════════════════
    private void ToggleLock()
    {
        _isLocked = !_isLocked;
        _menuLock.Checked = _isLocked;
        FlashHint(_isLocked ? "已锁定" : "已解锁");
    }

    private void TogglePenetration()
    {
        _isPenetrating = !_isPenetrating;
        _menuPenetrate.Checked = _isPenetrating;
        Win32API.SetMousePenetration(this.Handle, _isPenetrating);
        FlashHint(_isPenetrating ? "鼠标穿透已启用" : "鼠标穿透已关闭");
    }

    private void ToggleTopMost()
    {
        _isTopMost = !_isTopMost;
        _menuTopMost.Checked = _isTopMost;
        this.TopMost = _isTopMost;
        Win32API.SetTopMost(this.Handle, _isTopMost);
        FlashHint(_isTopMost ? "窗口置顶已启用" : "取消置顶");
    }

    private async void FlashHint(string hint)
    {
        var original = _lblStock.Text;
        var originalColor = _lblStock.ForeColor;

        _lblStock.Text = hint;
        _lblStock.ForeColor = Color.FromArgb(100, 100, 100);
        _lblStock.TextAlign = ContentAlignment.MiddleCenter;

        await Task.Delay(1200);

        if (_lblStock.Text == hint)
        {
            _lblStock.Text = original;
            _lblStock.ForeColor = originalColor;
            RenderCurrentStock();
        }
    }

    // ══════════════════════════════════════════════════════
    //  Add / Manage Stocks
    // ══════════════════════════════════════════════════════
    private void OnAddStock(object? sender, EventArgs e)
    {
        using var dialog = new StockConfigForm();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _configStocks.Add(new StockEntry
            {
                Name = dialog.SelectedStockName,
                Code = dialog.SelectedStockCode
            });
            StockConfig.Save(_configStocks);
            _currentIndex = 0;
            _ = FetchPrices();
        }
    }

    private void OnManageStocks(object? sender, EventArgs e)
    {
        using var dialog = new StockManageForm();
        dialog.ShowDialog();
        // Reload config in case it was changed
        _configStocks = StockConfig.Load();
        _currentIndex = 0;
        _ = FetchPrices();
    }

    // ══════════════════════════════════════════════════════
    //  Exit
    // ══════════════════════════════════════════════════════
    private void ExitApplication()
    {
        _fetchTimer.Stop();
        _cycleTimer.Stop();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        Application.Exit();
    }

    // ══════════════════════════════════════════════════════
    //  Form events
    // ══════════════════════════════════════════════════════
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            base.OnFormClosing(e);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(190, 190, 190), 1f);
        var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
        using var path = GetRoundedRect(rect, 6);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    private GraphicsPath GetRoundedRect(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }
}
