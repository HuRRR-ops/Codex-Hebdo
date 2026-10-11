using Microsoft.Win32;
using System.Text.Json;

namespace CodexHebdo;

internal static class OptionsWindow
{
    private static int opening;
    private static OptionsForm? form;
    internal static void Open()
    {
        if (Interlocked.CompareExchange(ref opening,1,0) != 0) {
            var f = Volatile.Read(ref form);
            if (f is { IsHandleCreated: true, IsDisposed: false })
                try { f.BeginInvoke(() => { f.WindowState = FormWindowState.Normal; f.Activate(); }); } catch (InvalidOperationException) { }
            return;
        }
        var thread = new Thread(() => {
            try {
                using var f = new OptionsForm(); Volatile.Write(ref form,f); Application.Run(f);
            } finally { Volatile.Write(ref form,null); Interlocked.Exchange(ref opening,0); }
        }) { IsBackground = true, Name = "Codex Hebdo options" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
}

internal sealed class OptionsForm : Form
{
    private Preferences draft = PreferenceStore.Saved;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Label details = new() { AutoSize = true, MaximumSize = new Size(560,0), Padding = new Padding(12), Font = new Font("Segoe UI",11) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private bool rebuilding;
    private bool fitting;
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
    private readonly FlowLayoutPanel buttons = new() { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
    private int D(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
    private Padding Pad(int value) => new(D(value));
    private bool? startupDraft;
    private string T(string value) => Strings.Get(value,draft.Language);
    private CheckBox startup = new();
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly bool offscreen;
    internal OptionsForm(bool offscreen = false)
    {
        this.offscreen = offscreen;
        Text = "Codex Hebdo - Options"; Font = new Font("Segoe UI",10);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        ClientSize = new Size(D(640),D(560));
        StartPosition = FormStartPosition.CenterScreen; ShowIcon = false;
        layout.Padding = Pad(12);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Aperçu en direct dans la barre des tâches · Annuler restaure les réglages enregistrés.", Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(D(610),0), Margin = new Padding(0,0,0,D(16)) },0,0);
        layout.Controls.Add(tabs,0,1);
        buttons.Padding = new Padding(0,D(12),0,0);
        buttons.Controls.Add(Button("Fermer",() => Close()));
        buttons.Controls.Add(Button("Appliquer",Apply));
        buttons.Controls.Add(Button("Annuler",() => { draft = PreferenceStore.Saved; PreferenceStore.Preview(draft); Close(); }));
        buttons.Controls.Add(Button("Par défaut",() => { draft = new() { Language=draft.Language }; Rebuild(); Preview(); }));
        layout.Controls.Add(buttons,0,2); Controls.Add(layout);
        FormClosed += (_,_) => { timer.Dispose(); PreferenceStore.Preview(PreferenceStore.Saved); };
        tabs.SelectedIndexChanged += (_,_) => { if (!rebuilding) FitContent(); };
        Shown += (_,_) => FitContent();
        DpiChanged += (_,_) => { Rebuild(); FitContent(); };
        Rebuild(); timer.Tick += (_,_) => UpdateDetails(); timer.Start(); UpdateDetails();
    }
    private Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(D(94),D(30)) }; b.Click += (_,_) => action(); return b;
    }
    private FlowLayoutPanel Page(TabControl tabs, string title)
    {
        var tab = new TabPage(title) { Padding = Pad(10) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        tab.Controls.Add(flow); tabs.TabPages.Add(tab); return flow;
    }
    private void Preview() { if (!rebuilding) PreferenceStore.Preview(draft); }
    private CheckBox Check(string title, bool value, Action<bool> change)
    {
        var b = new CheckBox { Text = title, Checked = value, AutoSize = true, Margin = new Padding(D(4),D(8),D(4),D(8)) };
        b.CheckedChanged += (_,_) => { change(b.Checked); Preview(); }; return b;
    }
    private Control Number(string title, int value, int min, int max, Action<int> change, int labelWidth = 300)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(D(4),D(6),D(4),D(6)) };
        row.Controls.Add(new Label { Text = title, Width = D(labelWidth), AutoSize = false, Height = D(28), TextAlign = ContentAlignment.MiddleLeft });
        var n = new SizeInput { Minimum = min, Maximum = max, Value = value, Width = D(75) };
        n.ValueChanged += (_,_) => { if (!rebuilding) { change((int)n.Value); Preview(); } };
        // Commit a complete valid typed value immediately, not only after focus leaves the field.
        n.TextChanged += (_,_) => {
            if (!rebuilding && int.TryParse(n.Text,out int entered) && entered >= min && entered <= max && n.Value != entered)
                n.Value = entered;
        };
        row.Controls.Add(n); return row;
    }
    private Control ColorButton(string title, int value, Action<int> change)
    {
        var b = Button(title,() => { }); b.Width = D(200); b.BackColor = Color.FromArgb(unchecked((int)0xff000000) | value);
        b.ForeColor = b.BackColor.GetBrightness() < .5 ? Color.White : Color.Black;
        b.Click += (_,_) => {
            using var dialog = new ColorDialog { Color = b.BackColor, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            b.BackColor = dialog.Color; b.ForeColor = dialog.Color.GetBrightness()<.5?Color.White:Color.Black;
            change(dialog.Color.ToArgb() & 0xffffff); Preview(); Rebuild();
        };
        return b;
    }
    private void Style(FlowLayoutPanel page, string title, Func<TextStyle> get, Action<TextStyle> set)
    {
        var box = new GroupBox { Text = title, MinimumSize = new Size(D(570),0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Pad(12), Margin = new Padding(0,0,0,D(10)) };
        var flow = new FlowLayoutPanel { Location = new Point(D(12),D(24)), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        box.Controls.Add(flow);
        var checks = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        checks.Controls.Add(Check("Afficher",get().Visible,v => set(get() with { Visible=v })));
        checks.Controls.Add(Check("Gras",get().Bold,v => set(get() with { Bold=v })));
        checks.Controls.Add(Check("Italique",get().Italic,v => set(get() with { Italic=v })));
        checks.Controls.Add(Check("Couleur automatique",get().AutoColor,v => set(get() with { AutoColor=v })));
        flow.Controls.Add(checks);
        var appearance = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        appearance.Controls.Add(Number("Taille du texte (DIP)",get().Size,8,20,v => set(get() with { Size=v }),150));
        appearance.Controls.Add(ColorButton("Couleur personnalisée",get().Color,v => set(get() with { Color=v, AutoColor=false })));
        flow.Controls.Add(appearance);
        page.Controls.Add(box);
    }
    private void Rebuild()
    {
        rebuilding = true; int selected = Math.Max(0,tabs.SelectedIndex);
        // Preserve the details label while disposing the old pages and their controls.
        details.Parent?.Controls.Remove(details);
        while (tabs.TabPages.Count > 0) { var old = tabs.TabPages[0]; tabs.TabPages.Remove(old); old.Dispose(); }
        var text = Page(tabs,"Textes");
        Style(text,"Hebdo",() => draft.Label,v => draft=draft with { Label=v });
        Style(text,"Pourcentage restant",() => draft.Percent,v => draft=draft with { Percent=v });
        Style(text,"Réinitialisations ↻",() => draft.Credits,v => draft=draft with { Credits=v });
        var logo = Page(tabs,"Logo");
        logo.Controls.Add(Check("Afficher le logo",draft.LogoVisible,v => draft=draft with { LogoVisible=v }));
        logo.Controls.Add(Number("Taille du logo (DIP)",draft.LogoSize,12,32,v => draft=draft with { LogoSize=v }));
        logo.Controls.Add(new Label { Text = draft.LogoPng is null ? "Logo d’origine" : "Logo personnalisé enregistré dans l’application", AutoSize = true });
        logo.Controls.Add(Button("Importer un PNG / SVG…",() => {
            using var dialog = new OpenFileDialog { Filter = T("Logo PNG ou SVG|*.png;*.svg"), Title = T("Choisir un logo") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { draft=draft with { LogoPng=LogoImport.Load(dialog.FileName) }; Rebuild(); Preview(); }
            catch (Exception e) when (e is not OutOfMemoryException) { MessageBox.Show(this,T(e.Message),T("Import du logo"),MessageBoxButtons.OK,MessageBoxIcon.Information); }
        }));
        logo.Controls.Add(Check("Teinter le logo personnalisé (sinon : couleurs d’origine)",draft.TintLogo,v => draft=draft with { TintLogo=v }));
        logo.Controls.Add(ColorButton("Couleur du logo",draft.LogoColor<0?0x282828:draft.LogoColor,v => draft=draft with { LogoColor=v, TintLogo=true }));
        logo.Controls.Add(Button("Logo d’origine / thème Windows",() => { draft=draft with { LogoPng=null, LogoColor=-1, TintLogo=false }; Rebuild(); Preview(); }));
        var spacing = Page(tabs,"Disposition");
        spacing.Controls.Add(Number("Espace entre le logo et le texte",draft.LogoGap,0,24,v => draft=draft with { LogoGap=v }));
        spacing.Controls.Add(Number("Espace entre le % et ↻",draft.CounterGap,0,24,v => draft=draft with { CounterGap=v }));
        spacing.Controls.Add(Number("Espace entre les deux lignes",draft.LineGap,0,8,v => draft=draft with { LineGap=v }));
        spacing.Controls.Add(Check("Centrer Hebdo sur le pourcentage seulement",draft.AlignPercent,v => draft=draft with { AlignPercent=v }));
        spacing.Controls.Add(new Label { Text = "Par défaut, Hebdo est centré au-dessus de l’ensemble « %  ↻ ».", AutoSize = true });
        var colors = Page(tabs,"Couleurs auto");
        colors.Controls.Add(new Label { Text = "Pourcentage : vert > 50 %, jaune > 20 %, rouge ≤ 20 %.\nRéinitialisations : rouge à 0, vert à partir de 1.\nUne information indisponible reste neutre.", AutoSize = true, Margin = new Padding(D(4),D(8),D(4),D(18)) });
        colors.Controls.Add(ColorButton("Vert",draft.Good,v => draft=draft with { Good=v }));
        colors.Controls.Add(ColorButton("Jaune",draft.Warning,v => draft=draft with { Warning=v }));
        colors.Controls.Add(ColorButton("Rouge",draft.Bad,v => draft=draft with { Bad=v }));
        var general = Page(tabs,"Général");
        general.Controls.Add(new Label { Text = "Langue / Language", AutoSize = true });
        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = D(240) };
        language.Items.AddRange(["Français", "English"]); language.SelectedIndex = draft.Language == "en" ? 1 : 0;
        language.SelectedIndexChanged += (_,_) => { if (!rebuilding) { draft=draft with { Language=language.SelectedIndex == 1 ? "en" : "fr" }; Rebuild(); Preview(); } };
        general.Controls.Add(language);
        general.Controls.Add(details);
        general.Controls.Add(Button("Actualiser maintenant",() => { Program.RequestRefresh(); UpdateDetails(); }));
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startup = new CheckBox { Text = "Démarrer avec Windows", Checked = startupDraft ?? (key?.GetValue("CodexHebdo") is string), AutoSize = true, Margin = Pad(12) };
        startup.CheckedChanged += (_,_) => startupDraft=startup.Checked;
        general.Controls.Add(startup);
        general.Controls.Add(new Label { Text = "Ouverture des options : double-clic uniquement.\nUn clic simple ou un clic droit n’ouvre aucun menu.\nLes modifications non appliquées sont annulées à la fermeture.", AutoSize = true, Margin = Pad(12) });
        tabs.SelectedIndex = Math.Min(selected,tabs.TabPages.Count-1); Strings.Translate(this,draft.Language); UpdateDetails(); rebuilding = false; FitContent();
    }
    private void UpdateDetails()
    {
        string value = Program.Details(draft.Language);
        if (details.Text == value) return;
        details.Text = value;
        if (!rebuilding && tabs.SelectedIndex == 4) FitContent();
    }
    private void FitContent()
    {
        if (fitting || rebuilding || tabs.SelectedTab?.Controls[0] is not FlowLayoutPanel page) return;
        fitting = true;
        try {
            layout.Padding = Pad(12); details.MaximumSize = new Size(D(560),0);
            page.PerformLayout();
            var wanted = page.GetPreferredSize(Size.Empty);
            int width = Math.Max(D(620),wanted.Width + tabs.SelectedTab.Padding.Horizontal + layout.Padding.Horizontal + D(16));
            var header = layout.GetControlFromPosition(0,0)!;
            header.MaximumSize = new Size(width-layout.Padding.Horizontal,0);
            int headerHeight = header.GetPreferredSize(new Size(header.MaximumSize.Width,0)).Height + header.Margin.Vertical;
            int footerHeight = buttons.GetPreferredSize(new Size(width,0)).Height + buttons.Margin.Vertical;
            int height = wanted.Height + tabs.SelectedTab.Padding.Vertical + tabs.ItemSize.Height + headerHeight + footerHeight + layout.Padding.Vertical + D(20);
            var screen = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(Math.Min(width,screen.Width-D(40)),Math.Min(height,screen.Height-D(70)));
            if (Visible && !offscreen) Location = new Point(Math.Clamp(Left,screen.Left,Math.Max(screen.Left,screen.Right-Width)),Math.Clamp(Top,screen.Top,Math.Max(screen.Top,screen.Bottom-Height)));
        } finally { fitting = false; }
    }
    private void Apply()
    {
        try {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (startup.Checked) {
                key.SetValue("CodexHebdo",'"'+Environment.ProcessPath+'"');
                var installation = Path.Combine(AppContext.BaseDirectory,"installation.json");
                if (File.Exists(installation)) {
                    using var d = JsonDocument.Parse(File.ReadAllText(installation));
                    if (d.RootElement.TryGetProperty("WindhawkPath",out var p)) {
                        var exe=Path.Combine(p.GetString()!,"windhawk.exe");
                        if (File.Exists(exe)) key.SetValue("CodexHebdo.Windhawk",'"'+exe+"\" -tray-only");
                    }
                }
            } else { key.DeleteValue("CodexHebdo",false); key.DeleteValue("CodexHebdo.Windhawk",false); }
            PreferenceStore.Save(draft);
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
            MessageBox.Show(this,T("Impossible d’enregistrer les réglages : ")+T(e.Message),"Codex Hebdo",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
}
