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
    private readonly TabControl tabs = new SoftTabs() { Dock = DockStyle.Fill };
    private readonly Label details = new() { AutoSize = true, MaximumSize = new Size(560,0), Padding = new Padding(12), Font = new Font("Segoe UI",9) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private bool rebuilding;
    private readonly List<Action> bindings = [];
    private bool fitting;
    private bool sizeInitialized;
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2 };
    private readonly SidebarPanel sidebar = new() { Dock=DockStyle.Fill, Margin=Padding.Empty };
    private readonly WidgetPreview preview = new();
    private readonly FlowLayoutPanel navigation = new() { AutoSize=false, WrapContents=false, FlowDirection=FlowDirection.TopDown };
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
        Text = "Codex Hebdo - Options"; Font = new Font("Segoe UI",9);
        SoftTheme.Set(draft);
        BackColor=SoftTheme.Background; ForeColor=SoftTheme.Ink; DoubleBuffered=true;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        ClientSize = new Size(D(640),D(560));
        StartPosition = FormStartPosition.CenterScreen; ShowIcon = false;
        layout.Padding = Padding.Empty;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,D(152)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.Language=draft.Language;
        navigation.Location=new Point(D(8),D(68)); navigation.Size=new Size(D(136),D(235));
        sidebar.Controls.Add(navigation); layout.Controls.Add(sidebar,0,0);layout.SetRowSpan(sidebar,3);
        var header = new Panel { Dock=DockStyle.Fill, Height=D(72), Margin=new Padding(D(12),D(10),D(12),D(4)) };
        preview.Dock=DockStyle.Fill; preview.Margin=Padding.Empty; header.Controls.Add(preview);
        layout.Controls.Add(header,1,0);
        tabs.Margin=new Padding(D(12),D(4),D(12),0);
        layout.Controls.Add(tabs,1,1);
        buttons.Padding = new Padding(D(4),D(8),D(12),D(10));
        buttons.Margin=Padding.Empty;
        buttons.Controls.Add(Button("Fermer",() => Close()));
        buttons.Controls.Add(Button("Appliquer",Apply));
        buttons.Controls.Add(Button("Annuler",() => { draft = PreferenceStore.Saved; PreferenceStore.Preview(draft); Close(); }));
        buttons.Controls.Add(Button("Par défaut",RestoreDefaults));
        var footer=new TableLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        footer.Controls.Add(buttons,1,0);
        layout.Controls.Add(footer,1,2); Controls.Add(layout);
        FormClosed += (_,_) => { timer.Dispose(); PreferenceStore.Preview(PreferenceStore.Saved); };
        tabs.SelectedIndexChanged += (_,_) => { if (!rebuilding) { UpdateNavigation(); FitContent(); } };
        Shown += (_,_) => FitContent();
        DpiChanged += (_,_) => { sizeInitialized=false; Rebuild(); FitContent(); };
        Rebuild(); timer.Tick += (_,_) => UpdateDetails(); timer.Start(); UpdateDetails();
    }
    private Button Button(string text, Action action)
    {
        var b = new SoftButton { Text = text, Primary = text == "Appliquer", AutoSize = true, MinimumSize = new Size(D(76),D(28)), Margin = new Padding(D(4),0,D(4),0) }; b.Click += (_,_) => action(); return b;
    }
    private FlowLayoutPanel Page(TabControl tabs, string title)
    {
        var tab = new TabPage(title) { Padding = Padding.Empty, BackColor=SoftTheme.Background };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        tab.Controls.Add(flow); tabs.TabPages.Add(tab); return flow;
    }
    private void Preview() { if (!rebuilding) { PreferenceStore.Preview(draft); ApplyTheme(); preview.RefreshPreview(draft); } }
    private CheckBox Check(string title, Func<bool> get, Action<bool> change)
    {
        var b = new SoftCheck { Toggle=title is "Afficher" or "Couleur automatique" or "Afficher le logo", ForeColor=SoftTheme.Accent, Cursor=Cursors.Hand, Text = title, Checked = get(), AutoSize = true, Margin = new Padding(D(4),D(5),D(4),D(5)) };
        bindings.Add(() => b.Checked = get());
        b.CheckedChanged += (_,_) => { if (!rebuilding) { change(b.Checked); Preview(); } }; return b;
    }
    private Control Number(string title, Func<int> get, int min, int max, Action<int> change, int labelWidth = 245)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(D(4),D(4),D(4),D(4)) };
        row.Controls.Add(new Label { Text = title, Width = D(labelWidth), AutoSize = false, Height = D(28), TextAlign = ContentAlignment.MiddleLeft });
        var n = new SizeInput { BorderStyle=BorderStyle.FixedSingle, TextAlign=HorizontalAlignment.Center, BackColor=SoftTheme.Surface, ForeColor=SoftTheme.Ink, Minimum = min, Maximum = max, Value = get(), Width = D(78), Margin=new Padding(0,D(3),0,0), AccessibleName=T(title) };
        bindings.Add(() => n.Value = get());
        n.ValueChanged += (_,_) => { if (!rebuilding) { change((int)n.Value); Preview(); } };
        // Commit a complete valid typed value immediately, not only after focus leaves the field.
        n.TextChanged += (_,_) => {
            if (!rebuilding && int.TryParse(n.Text,out int entered) && entered >= min && entered <= max && n.Value != entered)
                n.Value = entered;
        };
        row.Controls.Add(n); return row;
    }
    private Control ColorButton(string title, Func<int> get, Action<int> change)
    {
        var b = (SoftButton)Button(title,() => { }); b.AutoSize=false; b.Size = new Size(D(194),D(28));
        b.Swatch=Color.FromArgb(unchecked((int)0xff000000) | get());
        bindings.Add(() => b.Swatch=Color.FromArgb(unchecked((int)0xff000000) | get()));
        b.Click += (_,_) => {
            using var dialog = new ColorDialog { Color = b.Swatch ?? Color.Black, FullOpen = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            b.Swatch = dialog.Color;
            change(dialog.Color.ToArgb() & 0xffffff); Preview(); RefreshControls();
        };
        return b;
    }
    private void Style(FlowLayoutPanel page, string title, Func<TextStyle> get, Action<TextStyle> set)
    {
        var box = new SoftCard { Text = title, MinimumSize = new Size(D(510),0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Pad(6), Margin = new Padding(0,0,0,D(8)) };
        box.IconText=title=="Hebdo"?"▤":title=="Pourcentage restant"?"%":"↻";
        var flow = new FlowLayoutPanel { Location = new Point(D(10),D(40)), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        box.Controls.Add(flow);
        var checks = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        checks.Controls.Add(Check("Afficher",() => get().Visible,v => set(get() with { Visible=v })));
        checks.Controls.Add(Check("Gras",() => get().Bold,v => set(get() with { Bold=v })));
        checks.Controls.Add(Check("Italique",() => get().Italic,v => set(get() with { Italic=v })));
        checks.Controls.Add(Check("Couleur automatique",() => get().AutoColor,v => set(get() with { AutoColor=v })));
        foreach(Control check in checks.Controls) check.Margin=new Padding(D(2),D(3),D(8),D(3));
        flow.Controls.Add(checks);
        var appearance = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        appearance.Controls.Add(Number("Taille (DIP)",() => get().Size,8,20,v => set(get() with { Size=v }),120));
        appearance.Controls.Add(ColorButton("Couleur",() => get().Color,v => set(get() with { Color=v, AutoColor=false })));
        flow.Controls.Add(appearance);
        page.Controls.Add(box);
    }
    private void Rebuild()
    {
        rebuilding = true; bindings.Clear(); int selected = Math.Max(0,tabs.SelectedIndex);
        // Preserve the details label while disposing the old pages and their controls.
        details.Parent?.Controls.Remove(details);
        while (tabs.TabPages.Count > 0) { var old = tabs.TabPages[0]; tabs.TabPages.Remove(old); old.Dispose(); }
        var text = Page(tabs,"Textes");
        Style(text,"Hebdo",() => draft.Label,v => draft=draft with { Label=v });
        Style(text,"Pourcentage restant",() => draft.Percent,v => draft=draft with { Percent=v });
        Style(text,"Réinitialisations ↻",() => draft.Credits,v => draft=draft with { Credits=v });
        var logo = Page(tabs,"Logo");
        logo.Controls.Add(Check("Afficher le logo",() => draft.LogoVisible,v => draft=draft with { LogoVisible=v }));
        logo.Controls.Add(Number("Taille du logo (DIP)",() => draft.LogoSize,12,32,v => draft=draft with { LogoSize=v }));
        var logoInfo = new Label { AutoSize = true };
        Action updateLogoInfo = () => logoInfo.Text = T(draft.LogoPng is null ? "Logo d’origine" : "Logo personnalisé enregistré dans l’application");
        bindings.Add(updateLogoInfo); updateLogoInfo(); logo.Controls.Add(logoInfo);
        logo.Controls.Add(Button("Importer un PNG / SVG…",() => {
            using var dialog = new OpenFileDialog { Filter = T("Logo PNG ou SVG|*.png;*.svg"), Title = T("Choisir un logo") };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { draft=draft with { LogoPng=LogoImport.Load(dialog.FileName) }; RefreshControls(); Preview(); }
            catch (Exception e) when (e is not OutOfMemoryException) { MessageBox.Show(this,T(e.Message),T("Import du logo"),MessageBoxButtons.OK,MessageBoxIcon.Information); }
        }));
        logo.Controls.Add(Check("Teinter le logo personnalisé (sinon : couleurs d’origine)",() => draft.TintLogo,v => draft=draft with { TintLogo=v }));
        logo.Controls.Add(ColorButton("Couleur du logo",() => draft.LogoColor<0?0x282828:draft.LogoColor,v => draft=draft with { LogoColor=v, TintLogo=true }));
        logo.Controls.Add(Button("Logo d’origine / thème Windows",() => { draft=draft with { LogoPng=null, LogoColor=-1, TintLogo=false }; RefreshControls(); Preview(); }));
        var spacing = Page(tabs,"Disposition");
        spacing.Controls.Add(Number("Espace entre le logo et le texte",() => draft.LogoGap,0,24,v => draft=draft with { LogoGap=v }));
        spacing.Controls.Add(Number("Espace entre le % et ↻",() => draft.CounterGap,0,24,v => draft=draft with { CounterGap=v }));
        spacing.Controls.Add(Number("Espace entre les deux lignes",() => draft.LineGap,0,8,v => draft=draft with { LineGap=v }));
        spacing.Controls.Add(Check("Centrer Hebdo sur le pourcentage seulement",() => draft.AlignPercent,v => draft=draft with { AlignPercent=v }));
        spacing.Controls.Add(new Label { Text = "Par défaut, Hebdo est centré au-dessus de l’ensemble « %  ↻ ».", AutoSize = true });
        var colors = Page(tabs,"Couleurs auto");
        colors.Controls.Add(new Label { Text = "Pourcentage : vert > 50 %, jaune > 20 %, rouge ≤ 20 %.\nRéinitialisations : rouge à 0, vert à partir de 1.\nUne information indisponible reste neutre.", AutoSize = true, Margin = new Padding(D(4),D(8),D(4),D(18)) });
        colors.Controls.Add(ColorButton("Vert",() => draft.Good,v => draft=draft with { Good=v }));
        colors.Controls.Add(ColorButton("Jaune",() => draft.Warning,v => draft=draft with { Warning=v }));
        colors.Controls.Add(ColorButton("Rouge",() => draft.Bad,v => draft=draft with { Bad=v }));
        var general = Page(tabs,"Général");
        var appearancePage = general;
        appearancePage.Controls.Add(new Label { Text="Couleurs de l’interface", AutoSize=true, Font=new Font("Segoe UI",12,FontStyle.Bold), Margin=new Padding(D(4),D(10),0,D(10)) });
        appearancePage.Controls.Add(new Label { Text="Choisissez une ambiance, puis ajustez sa couleur d’accent.", AutoSize=true, Margin=new Padding(D(4),0,0,D(18)) });
        string[] themeIds=["reference","glacier","lavender","rose","sand","midnight"];
        string[] themeNames=["Référence","Glacier","Lavande","Rosé","Sable","Minuit"];
        int[] accents=[0x099B8B,0x2F70B5,0x7861B5,0xB44A79,0x9C652E,0x8DA8EF];
        var themes=new FlowLayoutPanel { AutoSize=true, WrapContents=true, MaximumSize=new Size(D(490),0), Margin=new Padding(0,0,0,D(16)) };
        for(int i=0;i<themeIds.Length;i++) {
            string id=themeIds[i];
            var choice=new SoftButton { Text=T(themeNames[i]), Name="Theme-"+id, Swatch=Color.FromArgb(unchecked((int)0xff000000)|accents[i]), Selected=draft.UiTheme==id, AutoSize=false, Size=new Size(D(150),D(34)), Margin=new Padding(D(4),D(4),D(4),D(4)) };
            bindings.Add(() => choice.Selected=draft.UiTheme==id);
            choice.Click+=(_,_) => { draft=draft with { UiTheme=id, UiAccent=-1 }; RefreshControls(); Preview(); };
            themes.Controls.Add(choice);
        }
        appearancePage.Controls.Add(themes);
        appearancePage.Controls.Add(ColorButton("Couleur d’accent",() => draft.UiAccent>=0?draft.UiAccent:SoftTheme.Accent.ToArgb()&0xffffff,v => draft=draft with { UiAccent=v }));
        appearancePage.Controls.Add(Button("Couleur du thème",() => { draft=draft with { UiAccent=-1 }; RefreshControls(); Preview(); }));
        appearancePage.Controls.Add(new Label { Text="Ces couleurs modifient les options, pas les indicateurs du widget.", AutoSize=true, Margin=new Padding(D(4),D(18),0,0) });
        general.Controls.Add(new Label { Text = "Langue / Language", AutoSize = true });
        var language = new ComboBox { FlatStyle=FlatStyle.Flat, BackColor=Color.White, ForeColor=SoftTheme.Ink, DropDownStyle = ComboBoxStyle.DropDownList, Width = D(152) };
        language.Items.AddRange(["Français", "English"]); language.SelectedIndex = draft.Language == "en" ? 1 : 0;
        language.SelectedIndexChanged += (_,_) => { if (!rebuilding) { draft=draft with { Language=language.SelectedIndex == 1 ? "en" : "fr" }; Rebuild(); Preview(); } };
        general.Controls.Add(language);
        general.Controls.Add(details);
        general.Controls.Add(Button("Actualiser maintenant",() => { Program.RequestRefresh(); UpdateDetails(); }));
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) startup = new SoftCheck { Toggle=true, ForeColor=SoftTheme.Accent, Text = "Démarrer avec Windows", Checked = startupDraft ?? (key?.GetValue("CodexHebdo") is string), AutoSize = true, Margin = Pad(12) };
        startup.CheckedChanged += (_,_) => startupDraft=startup.Checked;
        general.Controls.Add(startup);
        general.Controls.Add(new Label { Text = "Ouverture des options : double-clic uniquement.\nUn clic simple ou un clic droit n’ouvre aucun menu.\nLes modifications non appliquées sont annulées à la fermeture.", AutoSize = true, Margin = Pad(12) });

        while(navigation.Controls.Count>0) { var old=navigation.Controls[0]; navigation.Controls.Remove(old); old.Dispose(); }
        string[] hints=["Personnalisez les textes","Icône et identité","Position et alignements","Thème adaptatif","Comportement et divers"];
        string[] glyphs=["T","▧","▦","◉","⚙"];
        for(int i=0;i<tabs.TabPages.Count;i++) {
            int index=i;
            var item=new NavigationButton { Text=tabs.TabPages[i].Text, Description=T(hints[i]), Glyph=glyphs[i], Quiet=true, AutoSize=false, Size=new Size(D(136),D(40)), Margin=new Padding(0,0,0,D(5)) };
            item.Click+=(_,_) => tabs.SelectedIndex=index;
            navigation.Controls.Add(item);
        }
        tabs.SelectedIndex = Math.Min(selected,tabs.TabPages.Count-1); UpdateNavigation(); Strings.Translate(this,draft.Language); UpdateDetails(); rebuilding = false; ApplyTheme(); preview.RefreshPreview(draft); FitContent();
    }
    private void UpdateNavigation()
    {
        for(int i=0;i<navigation.Controls.Count;i++) ((SoftButton)navigation.Controls[i]).Selected=i==tabs.SelectedIndex;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd,IntPtr rect,IntPtr region,uint flags);
    private void RestoreDefaults()
    {
        // Present reset as one paint transaction, including child HWNDs and theme changes.
        bool redraw=IsHandleCreated && Visible;
        if(redraw) SendMessage(Handle,0x000B,IntPtr.Zero,IntPtr.Zero);
        try {
            draft=new() { Language=draft.Language };
            RefreshControls(); Preview();
        } finally {
            if(redraw) {
                SendMessage(Handle,0x000B,new IntPtr(1),IntPtr.Zero);
                RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x0185);
            }
        }
    }
    private void RefreshControls()
    {
        // Keep the same controls, focus, tab and window bounds during reset.
        // Only values change; no empty tab tree or intermediate layout is shown.
        rebuilding = true;
        SoftTheme.Set(draft);
        SuspendLayout(); layout.SuspendLayout(); tabs.SuspendLayout();
        try { foreach (var update in bindings) update(); }
        finally {
            tabs.ResumeLayout(false); layout.ResumeLayout(false); ResumeLayout(false);
            rebuilding = false;
        }
    }
    private void UpdateDetails()
    {
        preview.RefreshPreview(draft);
        string value = Program.Details(draft.Language);
        if (details.Text == value) return;
        details.Text = value;
        // Live statistics must never change the window geometry.
    }
    private void FitContent()
    {
        if (sizeInitialized || fitting || rebuilding || tabs.TabPages.Count==0 || tabs.TabPages[0].Controls[0] is not FlowLayoutPanel page) return;
        fitting = true;
        try {
            layout.ColumnStyles[0].Width=D(152); details.MaximumSize=new Size(D(490),0);
            navigation.Location=new Point(D(8),D(68));navigation.Size=new Size(D(136),D(235));
            page.PerformLayout();
            var wanted=page.GetPreferredSize(Size.Empty);
            int contentWidth=Math.Max(D(548),wanted.Width+tabs.Margin.Horizontal+SystemInformation.VerticalScrollBarWidth+D(12));
            var header=layout.GetControlFromPosition(1,0)!;
            int headerHeight=Math.Max(D(72),header.GetPreferredSize(new Size(contentWidth,0)).Height)+header.Margin.Vertical;
            int footerHeight=buttons.GetPreferredSize(new Size(contentWidth,0)).Height;
            int height=Math.Max(D(530),wanted.Height+tabs.Margin.Vertical+headerHeight+footerHeight+D(12));
            var screen=Screen.FromControl(this).WorkingArea;
            ClientSize=new Size(Math.Min(D(152)+contentWidth,screen.Width-D(40)),Math.Min(height,screen.Height-D(70)));
            sizeInitialized=true;
            if (Visible && !offscreen) Location = new Point(Math.Clamp(Left,screen.Left,Math.Max(screen.Left,screen.Right-Width)),Math.Clamp(Top,screen.Top,Math.Max(screen.Top,screen.Bottom-Height)));
        } finally { fitting = false; }
    }
    private void ApplyTheme()
    {
        SoftTheme.Set(draft); sidebar.Language=draft.Language;
        void Visit(Control c,bool card=false) {
            bool surface=card || c is SoftCard;
            c.BackColor=c is SidebarPanel || c==navigation || c is NavigationButton?SoftTheme.Sidebar:surface?SoftTheme.Surface:SoftTheme.Background;
            c.ForeColor=SoftTheme.Ink;
            if(c is TextBoxBase or UpDownBase or ComboBox) { c.BackColor=SoftTheme.Surface; return; }
            if(c is Label && c.Font.Size<12) c.ForeColor=SoftTheme.Muted;
            foreach(Control child in c.Controls) Visit(child,surface);
            c.Invalidate();
        }
        Visit(this);
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
