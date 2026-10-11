using System.Buffers.Binary;
using System.Text.Json;
using CodexHebdo;

internal static class Tests
{
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
    static Quota? Parse(string s) { using var d = JsonDocument.Parse(s); return Quota.Parse(d.RootElement, 1000000); }
    static string W(int minutes, string used = "62", long reset = 1500000) => $$"""{"usedPercent":{{used}},"windowDurationMins":{{minutes}},"resetsAt":{{reset}}}""";
    static string B(string primary, string secondary = "null") => "{\"rateLimits\":{\"primary\":" + primary + ",\"secondary\":" + secondary + "}}";
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("app-server")) { FakeServer(); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Check(Parse(B(W(10080)))?.Remaining == 38, "weekly primary, remaining");
        Check(Parse(B(W(300), W(10080)))?.Remaining == 38, "weekly secondary");
        Check(Parse(B(W(300))) is null, "no five-hour fallback");
        Check(Parse(B(W(10079))) is null, "duration exact");
        Check(Parse(B(W(10080, "0")))?.Remaining == 100, "unused");
        Check(Parse(B(W(10080, "100")))?.Remaining == 0, "exhausted");
        Check(Parse(B(W(10080, "12.5")))?.Remaining == 87.5, "fractional");
        foreach (var value in new[] { "null", "-1", "101", "\"62\"" }) Check(Parse(B(W(10080, value))) is null, "invalid percent " + value);
        Check(Parse(B(W(10080, reset: 1000000))) is null, "reset now");
        Check(Parse(B(W(10080, reset: 999999))) is null, "expired");
        Check(Parse(B(W(10080, reset: long.MaxValue))) is null, "invalid timestamp");
        Check(Parse(B(W(10080), W(10080, "20"))) is null, "ambiguous windows");
        Check(Parse("{\"rateLimits\":null}") is null, "null bucket");
        Check(Parse("{}") is null, "missing bucket");
        Check(Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":20}}}") is null, "missing duration");
        Check(Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":" + W(10080, "20") + "}},\"rateLimits\":{\"primary\":" + W(10080) + "}}")?.Remaining == 80, "prefer codex map");
        Check(Parse("{\"rateLimits\":{\"limitId\":\"codex_other\",\"primary\":" + W(10080) + "}}") is null, "reject other model");
        Check(Quota.Countdown(6 * 86400 + 86399) == "6j", "days");
        Check(Quota.Countdown(86399) == "23h", "hours");
        Check(Quota.Countdown(45 * 60) == "45min", "minutes");
        Check(Quota.Countdown(1) == "1min", "sub-minute");
        Check(Quota.Countdown(0) == "--", "reset countdown");
        Check(Quota.Color(50.01) == "green" && Quota.Color(50) == "yellow" && Quota.Color(21) == "yellow" && Quota.Color(20) == "red", "color boundaries");
        var q = new Quota(38, 1500000, 1000000);
        Check(q.IsFresh(1000075) && !q.IsFresh(1000076) && !q.IsFresh(999999), "TTL and clock rollback");
        Check(q.Label(1000076) == "Hebdo --", "stale label");
        var bytes = Program.Encode(q);
        Check(bytes.Length == 56 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0x31444843
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) == 2
            && BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(16)) == 38
            && BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(24)) == 1500000, "wire layout");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(Program.Encode(null).AsSpan(8)) == 0, "wire unavailable");
        Check(Enumerable.Range(1, 7).Select(Program.Backoff).SequenceEqual(new[] { 30, 60, 120, 240, 300, 300, 300 }), "backoff");
        CreditTests(q);
        RpcTests().GetAwaiter().GetResult();
        PreferenceTests(args);
        OptionsPipeTest().GetAwaiter().GetResult();
        Console.WriteLine($"{count} tests passed.");
    }

    static void CreditTests(Quota q)
    {
        ResetCredits? Credits(string json) { using var d = JsonDocument.Parse(json); return ResetCredits.Parse(d.RootElement, 1000000); }
        Check(Credits("{\"rateLimitResetCredits\":{\"availableCount\":0}}")?.Available == 0, "zero resets known");
        Check(Credits("{\"rateLimitResetCredits\":{\"availableCount\":2,\"credits\":[]}}")?.Available == 2, "reset count authoritative over capped list");
        foreach (var value in new[] { "-1", "null", "\"1\"", "1.5", "9223372036854775808" })
            Check(Credits("{\"rateLimitResetCredits\":{\"availableCount\":" + value + "}}") is null, "invalid reset count " + value);
        Check(Credits("{}") is null && Credits("{\"rateLimitResetCredits\":null}") is null, "missing resets unknown, not zero");
        var credits = new ResetCredits(1, 1000000);
        Check(credits.IsFresh(1000075) && !credits.IsFresh(1000076) && !credits.IsFresh(999999), "independent reset freshness");
        using var update = JsonDocument.Parse(B(W(10080)));
        var previous = new Reading(q, credits);
        var pushed = Reading.Parse(update.RootElement, 1000050, previous, notification: true);
        Check(pushed.Credits == credits && pushed.Credits.ObservedAt == 1000000, "notification preserves original reset timestamp");
        Check(Reading.Parse(update.RootElement, 1000050, previous).Credits is null, "full response clears missing resets");
        using var unavailable = JsonDocument.Parse("{\"rateLimitResetCredits\":null}");
        Check(Reading.Parse(unavailable.RootElement, 1000050, previous, notification: true).Credits is null, "explicit null notification clears resets");
        var encoded = Program.Encode(q, credits);
        Check(BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(40)) == 1
            && BinaryPrimitives.ReadInt64LittleEndian(encoded.AsSpan(48)) == 1000000, "reset IPC count and timestamp");
        Check(BinaryPrimitives.ReadInt64LittleEndian(Program.Encode(q).AsSpan(40)) == -1, "reset IPC unknown sentinel");
        Check(BinaryPrimitives.ReadInt64LittleEndian(Program.Encode(q, new(0,1000000)).AsSpan(40)) == 0, "reset IPC real zero");
    }

    static void FakeServer()
    {
        bool initialized = false;
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            using var d = JsonDocument.Parse(line);
            var root = d.RootElement;
            var method = root.GetProperty("method").GetString();
            if (method == "initialized") { initialized = true; continue; }
            int id = root.GetProperty("id").GetInt32();
            if (method == "initialize") Console.WriteLine(JsonSerializer.Serialize(new { id, result = new { } }));
            else if (!initialized) Console.WriteLine(JsonSerializer.Serialize(new { id, error = new { code = -1 } }));
            else if (method == "account/rateLimits/read")
            {
                var bucket = new { rateLimits = new { primary = new { usedPercent = 62, windowDurationMins = 10080, resetsAt = 1500000 } } };
                Console.WriteLine(JsonSerializer.Serialize(new { method = "account/rateLimits/updated", @params = bucket }));
                Console.WriteLine(JsonSerializer.Serialize(new { id, result = bucket }));
            }
            else if (method == "test/disconnect") return;
            else Console.WriteLine(JsonSerializer.Serialize(new { id, error = new { code = -32601, message = "test error" } }));
        }
    }

    static IEnumerable<Control> Children(Control parent)
    {
        foreach(Control c in parent.Controls) { yield return c; foreach(var descendant in Children(c)) yield return descendant; }
    }
    static void PreferenceTests(string[] args)
    {
        var p = new Preferences { Label = new(300), CounterGap = -9, Language = "invalid" }.Normalize();
        Check(p.Label.Size == 20 && p.CounterGap == 0 && p.Language == "fr", "settings normalized");
        var originalPath = PreferenceStore.FileName;
        string temp = Path.Combine(Path.GetTempPath(),"CodexHebdo-test-"+Guid.NewGuid()+".json");
        PreferenceStore.FileName = temp;
        try {
            PreferenceStore.Save(new() { Language="en", Percent=new(14,Bold:true,Italic:true), CounterGap=2 });
            PreferenceStore.Preview(new() { Language="fr" });
            Check(PreferenceStore.Saved.Language == "en", "preview does not save");
            PreferenceStore.Load();
            Check(PreferenceStore.Saved.Percent.Bold && PreferenceStore.Saved.Percent.Italic && PreferenceStore.Saved.CounterGap == 2, "preferences roundtrip");
            Check(BinaryPrimitives.ReadInt32LittleEndian(PreferenceStore.Packet.AsSpan(26*4)) == 1, "language IPC");
            string svg = Path.ChangeExtension(temp,"svg");
            try {
                File.WriteAllText(svg,"<svg xmlns='http://www.w3.org/2000/svg' width='20' height='10'><path fill='red' d='M0 0H20V10H0Z'/></svg>");
                string logo=LogoImport.Load(svg);
                var packet=PreferenceStore.Encode(new() { LogoPng=logo, TintLogo=true, LogoColor=0x123456 });
                Check(packet.Length>128 && packet.Length == 128+BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(100)), "SVG import and bounded logo wire");
                using var bitmap=SkiaSharp.SKBitmap.Decode(packet.AsSpan(128).ToArray());
                var pixel=bitmap.GetPixel(64,64);
                Check(pixel.Red==0x12 && pixel.Green==0x34 && pixel.Blue==0x56,"logo tint");
                Check(bitmap.GetPixel(0,0).Alpha==0,"logo aspect ratio and transparency");
            } finally { File.Delete(svg); }
            foreach (string bad in new[] { "<!DOCTYPE svg SYSTEM 'https://example.com/a'><svg/>", "<svg><image href='https://example.com/a'/></svg>", "<svg><path fill='url(https://example.com/a)'/></svg>" }) {
                bool rejected=false; try { LogoImport.ValidateSvg(bad); } catch { rejected=true; } Check(rejected,"external SVG rejected");
            }
            File.WriteAllText(temp,"invalid json"); PreferenceStore.Load();
            Check(PreferenceStore.Saved.Language=="fr","corrupt settings fallback");
            Check(Strings.Get("Appliquer","en")=="Apply" && Strings.Get("Appliquer","fr")=="Appliquer","FR EN strings");
            Check(Program.Details("en").Contains("unavailable"),"English unavailable details");
            {
                Application.EnableVisualStyles();
                foreach (string lang in new[] { "fr","en" }) {
                    PreferenceStore.Save(new() { Language=lang });
                    var before=PreferenceStore.Packet.ToArray();
                    using var form=new OptionsForm(offscreen:true); form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-3000,-3000); form.Show(); form.PerformLayout(); Application.DoEvents();
                    Check(before.SequenceEqual(PreferenceStore.Packet),"opening options leaves text sizes unchanged");
                    Check(form.FormBorderStyle==FormBorderStyle.FixedDialog && !form.MaximizeBox,"fixed non-resizable options");
                    Check(Children(form).OfType<GroupBox>().All(g => Children(g).OfType<Button>().All(b => g.ClientRectangle.Contains(g.RectangleToClient(b.RectangleToScreen(b.ClientRectangle))))),"style buttons fully inside groups at current DPI");
                    var sizes=Children(form).OfType<NumericUpDown>().Take(3).ToArray();
                    sizes[1].Text="1";
                    Check(BinaryPrimitives.ReadInt32LittleEndian(PreferenceStore.Packet.AsSpan(24))==12,"partial size input does not jump");
                    sizes[1].Text="11";
                    Check(BinaryPrimitives.ReadInt32LittleEndian(PreferenceStore.Packet.AsSpan(24))==11,"typed size preview matches field immediately");
                    sizes[1].Value=12;
                    using var image=new Bitmap(form.Width,form.Height);
                    form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));
                    if(args.Contains("--render-options")) image.Save(Path.Combine(Path.GetTempPath(),"CodexHebdo-options-"+lang+".png"));
                    var tabs=form.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabControl>().Single();
                    int fullHeight=form.Height;
                    for(int tab=1;tab<tabs.TabCount;tab++) {
                        tabs.SelectedIndex=tab; form.PerformLayout(); Application.DoEvents();
                        Check(form.Height<fullHeight,"options fit shorter tabs without user resizing");
                        using var tabImage=new Bitmap(form.Width,form.Height);
                        form.DrawToBitmap(tabImage,new Rectangle(Point.Empty,form.Size));
                        if(args.Contains("--render-options")) tabImage.Save(Path.Combine(Path.GetTempPath(),$"CodexHebdo-options-{lang}-{tab}.png"));
                    }
                }
            }
        } finally { PreferenceStore.FileName=originalPath; File.Delete(temp); File.Delete(temp+".tmp"); }
    }

    static async Task OptionsPipeTest()
    {
        string name="CodexHebdo-test-"+Guid.NewGuid();
        using var stop=new CancellationTokenSource();
        var opened=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server=Program.Serve(stop.Token,name,() => opened.TrySetResult());
        async Task<byte[]> Read(char command) {
            using var deadline=new CancellationTokenSource(3000);
            using var pipe=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);
            await pipe.ConnectAsync(deadline.Token);
            await pipe.WriteAsync(new byte[] {(byte)command},deadline.Token);
            var bytes=new byte[184]; await pipe.ReadExactlyAsync(bytes,deadline.Token); return bytes;
        }
        try {
            var before=await Read('V');
            var opening=await Read('U');
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var after=await Read('V');
            Check(before.SequenceEqual(opening) && opening.SequenceEqual(after),"opening options returns the same stats and text settings without an empty frame");
        } finally { stop.Cancel(); try { await server; } catch(OperationCanceledException) {} }
    }

    static async Task RpcTests()
    {
        await using var rpc = new RpcClient(Environment.ProcessPath!);
        var notification = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        rpc.Updated += x => notification.TrySetResult(x);
        await rpc.Initialize(CancellationToken.None);
        var result = await rpc.Call("account/rateLimits/read", new { }, CancellationToken.None);
        Check(Quota.Parse(result, 1000000)?.Remaining == 38, "RPC handshake and response correlation");
        Check(Quota.Parse(await notification.Task.WaitAsync(TimeSpan.FromSeconds(2)), 1000000)?.Remaining == 38, "RPC push notification");
        bool failed = false;
        try { await rpc.Call("test/error", new { }, CancellationToken.None); } catch (IOException) { failed = true; }
        Check(failed, "RPC error propagation");
        failed = false;
        try { await rpc.Call("test/disconnect", new { }, CancellationToken.None); } catch (IOException) { failed = true; }
        Check(failed && !rpc.IsAlive, "RPC disconnect propagation");
    }
}
