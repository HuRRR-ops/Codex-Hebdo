using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexHebdoSetup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex=new Mutex(true,"Local\\CodexHebdo-Setup",out bool owner);
        if(!owner) return 2;
        if(args.Contains("--verify")) { try { Clean(Extract());return 0; } catch { return 1; } }
        if(args.Contains("--install-unattended")) {
            try { return Run(Extract(),"en",true,_ => {}).GetAwaiter().GetResult(); } catch { return 1; }
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new SetupForm());
        return 0;
    }
    internal static string Extract()
    {
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexHebdo-Setup","1.2.3",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")!;
        using var archive=new ZipArchive(stream,ZipArchiveMode.Read);
        // ZipArchive rejects traversal paths; extraction occurs in a fresh, private staging directory.
        archive.ExtractToDirectory(root);
        var manifest=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"manifest.json")))!;
        foreach(var entry in manifest) {
            string path=Path.GetFullPath(Path.Combine(root,entry.Key));
            if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid payload path.");
            using var file=File.OpenRead(path);
            if(!Convert.ToHexString(SHA256.HashData(file)).Equals(entry.Value,StringComparison.OrdinalIgnoreCase)) throw new IOException("Payload integrity check failed.");
        }
        return root;
    }
    internal static async Task<int> Run(string root,string language,bool unattended,Action<string> report,CancellationToken cancellation=default)
    {
        var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8
        };
        foreach(string argument in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(root,"Bootstrap.ps1"),"-Language",language}) start.ArgumentList.Add(argument);
        if(unattended) start.ArgumentList.Add("-Unattended");
        using var process=new Process { StartInfo=start };
        string log=Path.Combine(Path.GetDirectoryName(root)!,"setup.log");
        object logLock=new();
        bool committing=false;
        void Record(string line) {
            lock(logLock) {
                File.AppendAllText(log,line+Environment.NewLine);
                if(line.StartsWith("Installation du widget") || line.StartsWith("Installing the widget")) {
                    if(!cancellation.IsCancellationRequested) { committing=true;File.WriteAllText(Path.Combine(root,"commit.allowed"),"ok"); }
                }
            }
            report(line);
        }
        Record("Codex Hebdo 1.2.3 - "+DateTimeOffset.Now.ToString("O"));
        process.OutputDataReceived+=(_,e) => { if(e.Data is { } line) Record(line); };
        process.ErrorDataReceived+=(_,e) => { if(e.Data is { } line) Record(line); };
        process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
        try { await process.WaitForExitAsync(cancellation); }
        catch(OperationCanceledException) {
            bool cancelled;
            lock(logLock) { cancelled=!committing;if(cancelled && !process.HasExited) process.Kill(entireProcessTree:true); }
            await process.WaitForExitAsync();
            if(cancelled) throw;
        }
        if(process.ExitCode==0) Clean(root);
        return process.ExitCode;
    }
    private static void Clean(string root)
    {
        string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexHebdo-Setup","1.2.3");
        var folder=new DirectoryInfo(Path.GetFullPath(root));
        if(!string.Equals(folder.Parent?.FullName,expected,StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(folder.Name,"N",out _) || folder.LinkTarget is not null) return;
        try { folder.Delete(true); } catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}

internal sealed class SetupForm : Form
{
    private readonly Label description=new() { AutoSize=true,Dock=DockStyle.Fill,ForeColor=Color.FromArgb(89,108,119),Margin=new Padding(0,0,0,22) };
    private readonly TextBox log=new() { Multiline=true,ReadOnly=true,TabStop=false,HideSelection=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Color.FromArgb(53,73,84) };
    private readonly Label activity=new() { AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,0,0,10) };
    private readonly Button install=new() { AutoSize=true,Text="Installer",BackColor=Color.FromArgb(9,155,139),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Padding=new Padding(14,5,14,5) };
    private readonly ComboBox language=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=110 };
    private readonly ProgressBar progress=new() { Dock=DockStyle.Bottom,Height=5 };
    private readonly Button cancel=new() { AutoSize=true,Padding=new Padding(9,5,9,5) };
    private CancellationTokenSource? cancellation;
    private bool busy,complete,committing;
    private bool French=>language.SelectedIndex==0;
    internal SetupForm()
    {
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        using var iconStream=Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.ico")!;
        Icon=new Icon(iconStream);
        Text="Codex Hebdo - Installation";Font=new Font("Segoe UI",10);ClientSize=new Size(580,480);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(244,250,250);Padding=new Padding(24);ForeColor=Color.FromArgb(28,48,61);
        SuspendLayout();
        var header=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true,WrapContents=false,Margin=new Padding(0,0,0,14) };
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")!;
        using var logo=Image.FromStream(resource);
        var picture=new PictureBox { Image=new Bitmap(logo),SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(45,48),Margin=new Padding(0,0,15,0) };
        header.Controls.Add(picture);header.Controls.Add(new Label { Text="Codex Hebdo",AutoSize=true,Font=new Font("Segoe UI Semibold",19),Margin=new Padding(0,4,0,0) });
        var footer=new TableLayoutPanel { Dock=DockStyle.Bottom,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=4,RowCount=1,Padding=new Padding(0,12,0,0),Margin=Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach(Control control in new Control[]{language,cancel,install}) { control.Anchor=AnchorStyles.None;control.Margin=new Padding(8,0,0,0); }
        install.AutoSizeMode=cancel.AutoSizeMode=AutoSizeMode.GrowAndShrink;
        language.Items.AddRange(["Français","English"]);language.SelectedIndex=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr"?0:1;
        language.Anchor=AnchorStyles.Left;language.Margin=Padding.Empty;
        footer.Controls.Add(language,0,0);footer.Controls.Add(cancel,2,0);footer.Controls.Add(install,3,0);
        footer.Padding=new Padding(0,18,0,0);
        install.FlatAppearance.BorderSize=0;
        install.FlatAppearance.MouseOverBackColor=Color.FromArgb(7,135,122);
        cancel.FlatStyle=FlatStyle.Flat;cancel.BackColor=Color.White;
        cancel.FlatAppearance.BorderColor=Color.FromArgb(215,229,229);
        install.MinimumSize=cancel.MinimumSize=new Size(104,40);
        activity.Font=new Font("Segoe UI Semibold",10);
        var journal=new Panel { Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(14),Margin=Padding.Empty };
        journal.Controls.Add(log);
        var content=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Margin=Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        for(int i=0;i<3;i++) content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute,5));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        progress.Dock=DockStyle.Fill;progress.Margin=Padding.Empty;
        footer.Dock=DockStyle.Fill;
        content.Controls.Add(header,0,0);content.Controls.Add(description,0,1);
        content.Controls.Add(activity,0,2);content.Controls.Add(journal,0,3);
        content.Controls.Add(progress,0,4);content.Controls.Add(footer,0,5);
        Controls.Add(content);
        ResumeLayout(true);
        language.SelectedIndexChanged+=(_,_) => Translate();Translate();
        install.Click+=async (_,_) => {
            if(complete) {Close();return;}
            busy=true;committing=false;cancellation=new();install.Enabled=false;language.Enabled=false;progress.Style=ProgressBarStyle.Marquee;log.Clear();Translate();
            try {
                Add(French?"Préparation de l’installation…":"Preparing installation…");
                string root=await Task.Run(Program.Extract);
                int code=await Program.Run(root,French?"fr":"en",false,Add,cancellation.Token);
                complete=code==0;
                Add(complete?(French?"Installation terminée. Double-cliquez sur le widget pour les options.":"Installation complete. Double-click the widget for settings."):(French?"Installation interrompue. Corrigez le problème indiqué puis réessayez.":"Installation stopped. Resolve the problem above, then retry."));
            } catch(OperationCanceledException) { Add(French?"Installation annulée. Vous pouvez la relancer.":"Installation cancelled. You can retry."); }
            catch(Exception ex) { Add(ex.Message); }
            finally {busy=false;cancel.Enabled=true;install.Enabled=true;language.Enabled=!complete;progress.Style=ProgressBarStyle.Blocks;progress.Value=complete?100:0;cancellation.Dispose();cancellation=null;Translate();}
        };
        cancel.Click+=(_,_) => { if(busy) { if(!committing)cancellation?.Cancel(); } else Close(); };
        FormClosing+=(_,e) => { if(busy) { e.Cancel=true;if(!committing)cancellation?.Cancel(); } };
        FormClosed+=(_,_) => picture.Image?.Dispose();
    }
    private void Translate()
    {
        activity.Text=complete?(French?"Installation terminée":"Installation complete"):(busy?(French?"Installation en cours":"Installing"):(French?"Prêt à installer":"Ready to install"));
        description.Text=French?"Installation automatique du widget et des composants manquants. Internet est requis. Si nécessaire, connectez votre compte Codex dans le navigateur qui s’ouvrira.":"Automatically install the widget and missing components. Internet is required. If needed, sign in to Codex in the browser that opens.";
        install.Text=complete?(French?"Fermer":"Close"):(French?"Installer":"Install");
        cancel.Text=French?"Annuler":"Cancel";cancel.Visible=!complete;
    }
    private void Add(string value) {
        if(InvokeRequired) { BeginInvoke(() => Add(value));return; }
        if(value.StartsWith("Installation du widget") || value.StartsWith("Installing the widget")) { committing=true;cancel.Enabled=false; }
        log.AppendText(value+Environment.NewLine);
    }
}
