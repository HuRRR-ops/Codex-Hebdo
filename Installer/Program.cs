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
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexHebdo-Setup","1.2.0",Guid.NewGuid().ToString("N"));
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
        Record("Codex Hebdo 1.2.0 - "+DateTimeOffset.Now.ToString("O"));
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
        string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexHebdo-Setup","1.2.0");
        var folder=new DirectoryInfo(Path.GetFullPath(root));
        if(!string.Equals(folder.Parent?.FullName,expected,StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(folder.Name,"N",out _) || folder.LinkTarget is not null) return;
        try { folder.Delete(true); } catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}

internal sealed class SetupForm : Form
{
    private readonly Label description=new() { AutoSize=false,Height=65,Dock=DockStyle.Top };
    private readonly TextBox log=new() { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,BorderStyle=BorderStyle.FixedSingle };
    private readonly Button install=new() { AutoSize=true,Text="Installer",BackColor=Color.FromArgb(9,155,139),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Padding=new Padding(14,5,14,5) };
    private readonly ComboBox language=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=110 };
    private readonly ProgressBar progress=new() { Dock=DockStyle.Bottom,Height=5 };
    private readonly Button cancel=new() { AutoSize=true,Padding=new Padding(9,5,9,5) };
    private CancellationTokenSource? cancellation;
    private bool busy,complete,committing;
    private bool French=>language.SelectedIndex==0;
    internal SetupForm()
    {
        Text="Codex Hebdo - Installation";Font=new Font("Segoe UI",10);ClientSize=new Size(570,400);
        FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.FromArgb(244,250,250);Padding=new Padding(22);
        var header=new FlowLayoutPanel { Dock=DockStyle.Top,Height=62,WrapContents=false };
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")!;
        using var logo=Image.FromStream(resource);
        var picture=new PictureBox { Image=new Bitmap(logo),SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(45,48),Margin=new Padding(0,0,15,0) };
        header.Controls.Add(picture);header.Controls.Add(new Label { Text="Codex Hebdo",AutoSize=true,Font=new Font("Segoe UI",20,FontStyle.Bold) });
        var footer=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=53,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,10,0,0) };
        language.Items.AddRange(["Français","English"]);language.SelectedIndex=CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr"?0:1;
        footer.Controls.Add(install);footer.Controls.Add(cancel);footer.Controls.Add(language);
        Controls.Add(log);Controls.Add(description);Controls.Add(header);Controls.Add(progress);Controls.Add(footer);
        language.SelectedIndexChanged+=(_,_) => Translate();Translate();
        install.Click+=async (_,_) => {
            if(complete) {Close();return;}
            busy=true;committing=false;cancellation=new();install.Enabled=false;language.Enabled=false;progress.Style=ProgressBarStyle.Marquee;log.Clear();
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
