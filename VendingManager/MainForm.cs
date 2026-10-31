using System.Text;
using System.Text.Json;

namespace VendingManager;

public sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly string _configPath;
    private readonly ConsolidationStore _store;
    private readonly ComboBox _vending = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly Label _location = new();
    private readonly Label _total = Card("CRÉDITOS REGISTRADOS");
    private readonly Label _ok = Card("EXITOSOS");
    private readonly Label _error = Card("CON ERROR");
    private readonly Label _pending = Card("PENDIENTES");
    private readonly Label _unknown = Card("SIN ESTADO");
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(232, 237, 243), EnableHeadersVisualStyles = false };
    private VendingConfig? _active;
    private readonly string _databasePath;

    public MainForm(AppConfig config, string configPath, string databasePath)
    {
        _config = config; _configPath = configPath; _databasePath = databasePath; _store = new ConsolidationStore(databasePath);
        Text = "Vending Manager"; Width = 1200; Height = 780; MinimumSize = new Size(950, 650);
        BackColor = Color.FromArgb(246, 248, 251); Font = new Font("Segoe UI", 10);
        BuildUi();
        LoadVendings();
        _from.Value = DateTime.Today; _to.Value = DateTime.Today;
        if (_config.Vendings.Count == 0) Shown += (_, _) => Configure();
        else Shown += async (_, _) => await SelectVendingAsync();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Color.FromArgb(20, 42, 68), Padding = new Padding(28, 18, 24, 16) };
        var title = new Label { Text = "Vending Manager", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 22), AutoSize = true, Location = new Point(24, 12) };
        var subtitle = new Label { Text = "Seguimiento de créditos por vending", ForeColor = Color.FromArgb(190, 207, 224), AutoSize = true, Location = new Point(28, 58) };
        header.Controls.Add(title); header.Controls.Add(subtitle);

        var root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 20) };
        var selection = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, WrapContents = false, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0) };
        selection.Controls.Add(Field("Vending", _vending)); selection.Controls.Add(Field("Desde", _from)); selection.Controls.Add(Field("Hasta", _to));
        var refresh = Button("Consultar", Color.FromArgb(26, 113, 219)); refresh.Click += async (_, _) => await RefreshReportAsync();
        var settings = Button("Configurar vending", Color.FromArgb(235, 240, 246)); settings.Click += (_, _) => Configure();
        var export = Button("Exportar CSV", Color.FromArgb(235, 240, 246)); export.Click += (_, _) => ExportCsv();
        selection.Controls.Add(refresh); selection.Controls.Add(export); selection.Controls.Add(settings);
        _vending.SelectedIndexChanged += async (_, _) => await SelectVendingAsync();

        _location.Text = "Selecciona una vending para ver su ubicación y reporte.";
        _location.Dock = DockStyle.Top; _location.Height = 34; _location.ForeColor = Color.FromArgb(91, 108, 126); _location.Padding = new Padding(2, 8, 0, 0);

        var cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 105, Padding = new Padding(0, 4, 0, 8), WrapContents = false };
        foreach (var label in new[] { _total, _ok, _error, _pending, _unknown }) { label.Margin = new Padding(0, 0, 12, 0); cards.Controls.Add(label); }

        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Date", HeaderText = "FECHA", Width = 130, DefaultCellStyle = new DataGridViewCellStyle { Format = "dd/MM/yyyy" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Total", HeaderText = "CRÉDITOS REGISTRADOS", Width = 190, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Success", HeaderText = "EXITOSOS", Width = 140, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", ForeColor = Color.FromArgb(20, 128, 85) } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Error", HeaderText = "CON ERROR", Width = 140, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", ForeColor = Color.FromArgb(190, 54, 54) } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Pending", HeaderText = "PENDIENTES", Width = 130, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Unknown", HeaderText = "SIN ESTADO", Width = 130, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Date", HeaderText = "DETALLE", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var footer = new Label { Text = "Los archivos .log originales se conservan. Consolidado local: " + _databasePath, Dock = DockStyle.Bottom, Height = 28, ForeColor = Color.FromArgb(113, 126, 140), Padding = new Padding(2, 7, 0, 0), Font = new Font("Segoe UI", 8.5f) };

        root.Controls.Add(_grid); root.Controls.Add(footer); root.Controls.Add(cards); root.Controls.Add(_location); root.Controls.Add(selection);
        Controls.Add(root); Controls.Add(header);
    }

    private static Control Field(string title, Control input)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Width = input.Width + 4, Height = 58, WrapContents = false, Margin = new Padding(4, 0, 12, 0) };
        panel.Controls.Add(new Label { Text = title, ForeColor = Color.FromArgb(90, 104, 120), Height = 18, AutoSize = true });
        panel.Controls.Add(input); return panel;
    }
    private static Button Button(string text, Color color)
    {
        var b = new Button { Text = text, Width = text.Length > 12 ? 148 : 118, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = color, ForeColor = color == Color.FromArgb(26, 113, 219) ? Color.White : Color.FromArgb(38, 56, 76), Margin = new Padding(4, 12, 8, 0), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; return b;
    }
    private static Label Card(string caption) => new() { Text = caption + "\n—", Size = new Size(190, 82), BackColor = Color.White, ForeColor = Color.FromArgb(33, 56, 80), Font = new Font("Segoe UI Semibold", 12), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12) };

    private void LoadVendings()
    {
        _vending.DataSource = null; _vending.DataSource = _config.Vendings.ToList(); _vending.DisplayMember = nameof(VendingConfig.Name);
    }

    private async Task SelectVendingAsync()
    {
        if (_vending.SelectedItem is not VendingConfig selected || !IsHandleCreated) return;
        using var login = new LoginForm(selected.Name);
        if (login.ShowDialog(this) != DialogResult.OK || !selected.Verify(login.Username, login.Password))
        {
            _active = null;
            _location.Text = "Acceso no validado. Selecciona la vending e ingresa sus credenciales.";
            return;
        }
        _active = selected;
        _location.Text = $"Ubicación: {selected.Location}    •    Log: {selected.LogPath}";
        await RefreshReportAsync();
    }

    private async Task RefreshReportAsync()
    {
        if (_active is null) return;
        try
        {
            var imported = await _store.SyncAsync(_active);
            var start = DateOnly.FromDateTime(_from.Value.Date);
            var end = DateOnly.FromDateTime(_to.Value.Date);
            if (end < start) { MessageBox.Show(this, "La fecha Hasta debe ser igual o posterior a Desde."); return; }
            var rows = await _store.QueryAsync(_active.Name, start, end);
            _grid.DataSource = rows.Select(x => new { x.Date, x.Total, x.Success, x.Error, x.Pending, x.Unknown, Detail = "" }).ToList();
            var total = rows.Sum(x => x.Total);
            _total.Text = $"CRÉDITOS REGISTRADOS\n{total:N2}";
            _ok.Text = $"EXITOSOS\n{rows.Sum(x => x.Success):N2}";
            _error.Text = $"CON ERROR\n{rows.Sum(x => x.Error):N2}";
            _pending.Text = $"PENDIENTES\n{rows.Sum(x => x.Pending):N2}";
            _unknown.Text = $"SIN ESTADO\n{rows.Sum(x => x.Unknown):N2}";
            Text = $"Vending Manager • {imported} registros leídos";
        }
        catch (Exception ex) { MessageBox.Show(this, "No se pudo leer o consolidar el archivo. Revisa que Google Drive haya terminado de sincronizarlo.\n\n" + ex.Message, "Error de lectura", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Configure()
    {
        using var form = new VendingSettingsForm(_config);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _config.Save(_configPath); LoadVendings(); _active = null;
            if (_config.Vendings.Count > 0) _vending.SelectedIndex = 0;
        }
    }

    private void ExportCsv()
    {
        if (_active is null) { MessageBox.Show(this, "Primero ingresa a una vending."); return; }
        using var dialog = new SaveFileDialog { Filter = "Archivo CSV (*.csv)|*.csv", FileName = $"{_active.Name}_{_from.Value:yyyyMMdd}_{_to.Value:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var rows = (_grid.DataSource as System.Collections.IEnumerable)?.Cast<object>().ToList() ?? [];
        var sb = new StringBuilder("Fecha,Registrados,Exitosos,Error,Pendientes,Sin estado\r\n");
        foreach (dynamic row in rows)
            sb.AppendLine($"{((DateTime)row.Date):yyyy-MM-dd},{row.Total},{row.Success},{row.Error},{row.Pending},{row.Unknown}");
        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
        MessageBox.Show(this, "Reporte CSV exportado.");
    }
}

internal sealed class LoginForm : Form
{
    private readonly TextBox _user = new() { Width = 250 };
    private readonly TextBox _pass = new() { Width = 250, UseSystemPasswordChar = true };
    public string Username => _user.Text;
    public string Password => _pass.Text;
    public LoginForm(string vending)
    {
        Text = "Acceso a " + vending; Width = 350; Height = 215; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(22), WrapContents = false };
        layout.Controls.Add(new Label { Text = "Usuario", AutoSize = true }); layout.Controls.Add(_user);
        layout.Controls.Add(new Label { Text = "Contraseña", AutoSize = true }); layout.Controls.Add(_pass);
        var ok = new Button { Text = "Ingresar", DialogResult = DialogResult.OK, Width = 100 };
        layout.Controls.Add(ok); Controls.Add(layout); AcceptButton = ok;
    }
}

internal sealed class VendingSettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly ListBox _items = new() { Dock = DockStyle.Fill };
    public VendingSettingsForm(AppConfig config)
    {
        _config = config; Text = "Configurar archivos y accesos"; Width = 700; Height = 420; StartPosition = FormStartPosition.CenterParent;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var add = new Button { Text = "Agregar vending", Width = 130 }; add.Click += (_, _) => Edit(null);
        var edit = new Button { Text = "Editar", Width = 90 }; edit.Click += (_, _) => { if (_items.SelectedItem is VendingConfig v) Edit(v); };
        var remove = new Button { Text = "Eliminar", Width = 90 }; remove.Click += (_, _) => { if (_items.SelectedItem is VendingConfig v && MessageBox.Show(this, $"¿Eliminar {v.Name} de la configuración? Los datos consolidados no se borran.", "Confirmar", MessageBoxButtons.YesNo) == DialogResult.Yes) { _config.Vendings.Remove(v); Reload(); }; };
        var done = new Button { Text = "Guardar y cerrar", Width = 130, DialogResult = DialogResult.OK };
        buttons.Controls.Add(done); buttons.Controls.Add(remove); buttons.Controls.Add(edit); buttons.Controls.Add(add);
        Controls.Add(_items); Controls.Add(buttons); Reload();
    }
    private void Reload() { _items.DataSource = null; _items.DataSource = _config.Vendings.ToList(); _items.DisplayMember = nameof(VendingConfig.Name); }
    private void Edit(VendingConfig? item)
    {
        using var dialog = new EditVendingForm(item);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is { } saved)
        {
            if (item is null) _config.Vendings.Add(saved);
            else { item.Name=saved.Name; item.Location=saved.Location; item.LogPath=saved.LogPath; item.Username=saved.Username; if (!string.IsNullOrEmpty(saved.PasswordHash)) { item.PasswordHash=saved.PasswordHash; item.PasswordSalt=saved.PasswordSalt; } }
            Reload();
        }
    }
}

internal sealed class EditVendingForm : Form
{
    private readonly TextBox _name = new() { Width=330 };
    private readonly TextBox _location = new() { Width=330 };
    private readonly TextBox _path = new() { Width=270 };
    private readonly TextBox _username = new() { Width=330 };
    private readonly TextBox _password = new() { Width=330, UseSystemPasswordChar=true };
    private readonly VendingConfig? _existing;
    public VendingConfig? Result { get; private set; }
    public EditVendingForm(VendingConfig? existing)
    {
        _existing=existing; Text=existing is null ? "Nueva vending" : "Editar vending"; Width=500; Height=390; StartPosition=FormStartPosition.CenterParent; FormBorderStyle=FormBorderStyle.FixedDialog;
        if (existing is not null) { _name.Text=existing.Name; _location.Text=existing.Location; _path.Text=existing.LogPath; _username.Text=existing.Username; }
        var panel=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(18), ColumnCount=2, RowCount=6 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        AddRow(panel,0,"Nombre",_name); AddRow(panel,1,"Ubicación",_location);
        var browse=new Button { Text="Examinar…", Width=90 }; browse.Click += (_,_) => { using var d=new OpenFileDialog { Filter="Logs (*.log)|*.log|Todos (*.*)|*.*" }; if(d.ShowDialog(this)==DialogResult.OK) _path.Text=d.FileName; };
        var pathPanel=new FlowLayoutPanel { Dock=DockStyle.Fill, WrapContents=false }; pathPanel.Controls.Add(_path); pathPanel.Controls.Add(browse);
        AddRow(panel,2,"Archivo .log",pathPanel); AddRow(panel,3,"Usuario",_username); AddRow(panel,4,"Contraseña",_password);
        var save=new Button { Text="Guardar", Width=100, DialogResult=DialogResult.OK }; save.Click += (_,_) => SaveResult();
        panel.Controls.Add(save,1,5); Controls.Add(panel); AcceptButton=save;
        if (existing is not null) _password.PlaceholderText="Dejar vacío para conservar contraseña";
    }
    private static void AddRow(TableLayoutPanel p,int row,string label,Control control)
    {
        p.RowStyles.Add(new RowStyle(SizeType.Absolute,48)); p.Controls.Add(new Label { Text=label, AutoSize=true, Anchor=AnchorStyles.Left },0,row); control.Anchor=AnchorStyles.Left|AnchorStyles.Right; p.Controls.Add(control,1,row);
    }
    private void SaveResult()
    {
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_path.Text) || string.IsNullOrWhiteSpace(_username.Text) ||
            (_existing is null && string.IsNullOrWhiteSpace(_password.Text)))
        {
            MessageBox.Show(this,"Completa nombre, archivo, usuario y contraseña."); DialogResult=DialogResult.None; return;
        }
        Result=new VendingConfig { Name=_name.Text.Trim(), Location=_location.Text.Trim(), LogPath=_path.Text.Trim(), Username=_username.Text.Trim() };
        if (!string.IsNullOrWhiteSpace(_password.Text)) Result.SetPassword(_password.Text);
        else if (_existing is not null) { Result.PasswordHash=_existing.PasswordHash; Result.PasswordSalt=_existing.PasswordSalt; }
    }
}