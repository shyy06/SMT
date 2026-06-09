namespace SMT;

/// <summary>
/// Dialog for adding/editing a stock — single input field with autocomplete.
/// </summary>
public class StockConfigForm : Form
{
    private TextBox _txtSearch = null!;
    private ListBox _lstSuggestions = null!;
    private Label _lblHint = null!;
    private Button _btnOK = null!;
    private Button _btnCancel = null!;

    private readonly StockPriceService _service = new();
    private readonly System.Windows.Forms.Timer _debounceTimer = new();
    private List<StockSuggestion> _suggestions = new();

    public string SelectedStockName { get; private set; } = string.Empty;
    public string SelectedStockCode { get; private set; } = string.Empty;

    public StockConfigForm()
    {
        InitializeComponent();
        _debounceTimer.Interval = 300;
        _debounceTimer.Tick += DebouncedSearch;
    }

    private void InitializeComponent()
    {
        this.Text = "添加股票";
        this.Size = new Size(380, 320);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.WhiteSmoke;

        _lblHint = new Label
        {
            Text = "输入股票名称或代码（如：茅台 / 600519）：",
            Location = new Point(15, 15),
            Size = new Size(340, 25),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 9f)
        };

        _txtSearch = new TextBox
        {
            Location = new Point(15, 45),
            Size = new Size(340, 28),
            Font = new Font("Microsoft YaHei UI", 10f),
            PlaceholderText = "输入中文名称或数字代码..."
        };
        _txtSearch.TextChanged += OnSearchTextChanged;
        _txtSearch.KeyDown += OnSearchKeyDown;

        _lstSuggestions = new ListBox
        {
            Location = new Point(15, 80),
            Size = new Size(340, 150),
            Font = new Font("Microsoft YaHei UI", 9f),
            IntegralHeight = false,
            Visible = false
        };
        _lstSuggestions.MouseDoubleClick += OnSuggestionSelected;
        _lstSuggestions.KeyDown += OnSuggestionKeyDown;

        _btnOK = new Button
        {
            Text = "确定",
            Location = new Point(200, 240),
            Size = new Size(75, 30),
            DialogResult = DialogResult.OK,
            Enabled = false
        };
        _btnOK.Click += BtnOK_Click;

        _btnCancel = new Button
        {
            Text = "取消",
            Location = new Point(285, 240),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel
        };

        this.Controls.AddRange(new Control[] {
            _lblHint, _txtSearch, _lstSuggestions, _btnOK, _btnCancel
        });

        this.AcceptButton = _btnOK;
        this.CancelButton = _btnCancel;
    }

    /// <summary>
    /// Pre-fill the form for editing an existing stock.
    /// </summary>
    public void SetEditMode(string name, string code)
    {
        SelectedStockName = name;
        SelectedStockCode = code;
        _txtSearch.Text = $"{name} ({code})";
        _btnOK.Enabled = true;
    }

    // ── Search with debounce ──────────────────────────

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async void DebouncedSearch(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        string keyword = _txtSearch.Text.Trim();

        if (keyword.Length < 1)
        {
            _lstSuggestions.Visible = false;
            _suggestions.Clear();
            return;
        }

        _lstSuggestions.Visible = true;
        _lstSuggestions.Items.Clear();
        _lstSuggestions.Items.Add("搜索中...");
        _lstSuggestions.Enabled = false;

        _suggestions = await _service.SearchStocksAsync(keyword);

        _lstSuggestions.Items.Clear();
        _lstSuggestions.Enabled = true;

        if (_suggestions.Count == 0)
        {
            _lstSuggestions.Items.Add("未找到匹配股票，请尝试其他关键词");
            _lstSuggestions.Enabled = false;
        }
        else
        {
            foreach (var s in _suggestions)
                _lstSuggestions.Items.Add(s.Display);
        }

        _btnOK.Enabled = false;
        SelectedStockName = "";
        SelectedStockCode = "";
    }

    // ── Keyboard navigation ─────────────────────────────

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            e.SuppressKeyPress = true;
            if (_lstSuggestions.Visible && _lstSuggestions.Items.Count > 0)
            {
                _lstSuggestions.Focus();
                _lstSuggestions.SelectedIndex = 0;
            }
        }
        else if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            if (_suggestions.Count == 1)
            {
                SelectSuggestion(0);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else if (_suggestions.Count > 1)
            {
                _lstSuggestions.Focus();
                _lstSuggestions.SelectedIndex = 0;
            }
        }
    }

    private void OnSuggestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            if (_lstSuggestions.SelectedIndex >= 0)
            {
                SelectSuggestion(_lstSuggestions.SelectedIndex);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }

    private void OnSuggestionSelected(object? sender, MouseEventArgs e)
    {
        int index = _lstSuggestions.IndexFromPoint(e.Location);
        if (index >= 0 && index < _suggestions.Count)
        {
            SelectSuggestion(index);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    private void SelectSuggestion(int index)
    {
        if (index < 0 || index >= _suggestions.Count) return;
        var sel = _suggestions[index];
        SelectedStockName = sel.Name;
        SelectedStockCode = sel.Code;
        _txtSearch.Text = sel.Display;
        _btnOK.Enabled = true;
    }

    // ── OK button ─────────────────────────────────────

    private void BtnOK_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedStockCode))
        {
            string raw = _txtSearch.Text.Trim();
            if (raw.Length >= 6 && raw.All(c => char.IsDigit(c)))
            {
                SelectedStockName = raw;
                SelectedStockCode = StockConfig.FormatStockCode(raw);
            }
            else
            {
                MessageBox.Show("请从下拉列表中选择一只股票，或输入有效的6位数字代码。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
