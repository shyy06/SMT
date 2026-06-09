using System.ComponentModel;

namespace SMT;

/// <summary>
/// Stock management form — delete and modify stocks.
/// Edits config.txt directly; changes take effect immediately.
/// </summary>
public class StockManageForm : Form
{
    private ListView _listView = null!;
    private Button _btnAdd = null!;
    private Button _btnEdit = null!;
    private Button _btnDelete = null!;
    private Button _btnClose = null!;
    private List<StockEntry> _stocks = new();

    public StockManageForm()
    {
        InitializeComponent();
        LoadStocks();
    }

    private void InitializeComponent()
    {
        this.Text = "管理股票";
        this.Size = new Size(460, 380);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.WhiteSmoke;

        var lblTitle = new Label
        {
            Text = "已添加的股票列表：",
            Location = new Point(15, 15),
            Size = new Size(200, 25),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _listView = new ListView
        {
            Location = new Point(15, 45),
            Size = new Size(415, 250),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            Font = new Font("Microsoft YaHei UI", 9f),
            GridLines = true,
            BorderStyle = BorderStyle.FixedSingle
        };
        _listView.Columns.Add("序号", 50, HorizontalAlignment.Center);
        _listView.Columns.Add("股票名称", 140, HorizontalAlignment.Left);
        _listView.Columns.Add("股票代码", 100, HorizontalAlignment.Center);
        _listView.Columns.Add("完整代码", 100, HorizontalAlignment.Center);
        _listView.SelectedIndexChanged += OnSelectionChanged;
        _listView.DoubleClick += OnEdit;

        _btnAdd = new Button
        {
            Text = "添加",
            Location = new Point(15, 310),
            Size = new Size(80, 30),
        };
        _btnAdd.Click += OnAdd;

        _btnEdit = new Button
        {
            Text = "修改",
            Location = new Point(110, 310),
            Size = new Size(80, 30),
            Enabled = false
        };
        _btnEdit.Click += OnEdit;

        _btnDelete = new Button
        {
            Text = "删除",
            Location = new Point(205, 310),
            Size = new Size(80, 30),
            Enabled = false
        };
        _btnDelete.Click += OnDelete;

        _btnClose = new Button
        {
            Text = "关闭",
            Location = new Point(350, 310),
            Size = new Size(80, 30),
            DialogResult = DialogResult.Cancel
        };

        this.Controls.AddRange(new Control[] {
            lblTitle, _listView, _btnAdd, _btnEdit, _btnDelete, _btnClose
        });
    }

    private void LoadStocks()
    {
        _stocks = StockConfig.Load();
        _listView.Items.Clear();

        for (int i = 0; i < _stocks.Count; i++)
        {
            var s = _stocks[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(s.Name);
            item.SubItems.Add(s.Code.Substring(2)); // number part
            item.SubItems.Add(s.Code); // full code e.g. sh600519
            item.Tag = i;
            _listView.Items.Add(item);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        bool hasSelection = _listView.SelectedItems.Count > 0;
        _btnEdit.Enabled = hasSelection;
        _btnDelete.Enabled = hasSelection;
    }

    private void OnAdd(object? sender, EventArgs e)
    {
        using var dialog = new StockConfigForm();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _stocks.Add(new StockEntry
            {
                Name = dialog.SelectedStockName,
                Code = dialog.SelectedStockCode
            });
            StockConfig.Save(_stocks);
            LoadStocks();
        }
    }

    private void OnEdit(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;

        int index = (int)_listView.SelectedItems[0].Tag;

        using var dialog = new StockConfigForm();
        dialog.SetEditMode(_stocks[index].Name, _stocks[index].Code);

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _stocks[index] = new StockEntry
            {
                Name = dialog.SelectedStockName,
                Code = dialog.SelectedStockCode
            };
            StockConfig.Save(_stocks);
            LoadStocks();
        }
    }

    private void OnDelete(object? sender, EventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;

        int index = (int)_listView.SelectedItems[0].Tag;
        var stock = _stocks[index];
        var result = MessageBox.Show(
            $"确定要删除「{stock.Name}」({stock.Code}) 吗？",
            "确认删除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _stocks.RemoveAt(index);
            StockConfig.Save(_stocks);
            LoadStocks();
        }
    }
}
