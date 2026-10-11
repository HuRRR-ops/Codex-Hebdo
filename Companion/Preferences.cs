using System.Buffers.Binary;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace CodexHebdo;

internal sealed record TextStyle(int Size = 12, bool Visible = true, bool Bold = false, bool Italic = false, bool AutoColor = true, int Color = 0x282828)
{
    public TextStyle Normalize() => this with { Size = Math.Clamp(Size, 8, 20), Color = Color & 0xffffff };
}
internal sealed record Preferences
{
    public string Language { get; init; } = "fr";
    public string UiTheme { get; init; } = "reference";
    public int UiAccent { get; init; } = -1;
    public TextStyle Label { get; init; } = new(10);
    public TextStyle Percent { get; init; } = new();
    public TextStyle Credits { get; init; } = new();
    public bool LogoVisible { get; init; } = true;
    public int LogoSize { get; init; } = 21;
    public int LogoColor { get; init; } = -1;
    public bool TintLogo { get; init; }
    public string? LogoPng { get; init; }
    public int LogoGap { get; init; } = 7;
    public int CounterGap { get; init; } = 4;
    public int LineGap { get; init; }
    public bool AlignPercent { get; init; }
    public int Good { get; init; } = 0x0f7334;
    public int Warning { get; init; } = 0x8c6400;
    public int Bad { get; init; } = 0xc3232a;
    public Preferences Normalize() => this with {
        Language = Language == "en" ? "en" : "fr",
        UiTheme = UiTheme is "reference" or "glacier" or "lavender" or "rose" or "sand" or "midnight" ? UiTheme : "reference",
        UiAccent = UiAccent < 0 ? -1 : UiAccent & 0xffffff,
        Label = (Label ?? new(10)).Normalize(), Percent = (Percent ?? new()).Normalize(), Credits = (Credits ?? new()).Normalize(),
        LogoSize = Math.Clamp(LogoSize, 12, 32), LogoGap = Math.Clamp(LogoGap, 0, 24),
        CounterGap = Math.Clamp(CounterGap, 0, 24), LineGap = Math.Clamp(LineGap, 0, 8),
        LogoColor = LogoColor < 0 ? -1 : LogoColor & 0xffffff,
        Good = Good & 0xffffff, Warning = Warning & 0xffffff, Bad = Bad & 0xffffff,
        LogoPng = LogoPng is { Length: <= 180000 } ? LogoPng : null
    };
}

internal static class PreferenceStore
{
    internal static string FileName = Path.Combine(AppContext.BaseDirectory, "preferences.json");
    private static int revision;
    private static byte[] packet = Encode(new());
    internal static Preferences Saved { get; private set; } = new();
    internal static byte[] Packet => Volatile.Read(ref packet);
    internal static void Load()
    {
        try {
            if (File.Exists(FileName) && new FileInfo(FileName).Length <= 200000)
                Saved = (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FileName)) ?? new()).Normalize();
            Preview(Saved);
        } catch { Saved = new(); Preview(Saved); }
    }
    internal static void Preview(Preferences value) => Volatile.Write(ref packet, Encode(value.Normalize()));
    internal static void Save(Preferences value)
    {
        value = value.Normalize();
        var encoded = Encode(value);
        File.WriteAllText(FileName + ".tmp", JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(FileName + ".tmp", FileName, true);
        Saved = value;
        Volatile.Write(ref packet, encoded);
    }
    internal static byte[] Encode(Preferences value)
    {
        var p = value.Normalize();
        byte[] png = [];
        if (p.LogoPng is not null) {
            png = Convert.FromBase64String(p.LogoPng);
            using var bitmap = SKBitmap.Decode(png);
            if (bitmap is null || bitmap.Width > 256 || bitmap.Height > 256 || png.Length > 131072)
                throw new InvalidDataException("Logo invalide.");
            if (p.TintLogo) {
                using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
                int c = p.LogoColor < 0 ? 0x282828 : p.LogoColor;
                using var filter = SKColorFilter.CreateBlendMode(new SKColor((byte)(c >> 16), (byte)(c >> 8), (byte)c), SKBlendMode.SrcIn);
                using var paint = new SKPaint { ColorFilter = filter };
                surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
                using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); png = data.ToArray();
            }
        }
        var b = new byte[128 + png.Length];
        void I(int slot, int val) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(slot * 4), val);
        I(0, 1); I(1, Interlocked.Increment(ref revision));
        void Style(int offset, TextStyle s) { I(offset,s.Size); I(offset+1,(s.Visible?1:0)|(s.Bold?2:0)|(s.Italic?4:0)|(s.AutoColor?8:0)); I(offset+2,s.Color); }
        Style(2,p.Label); Style(6,p.Percent); Style(10,p.Credits);
        I(14,p.LogoSize); I(15,(p.LogoVisible?1:0)|(png.Length>0?2:0)); I(16,p.LogoColor);
        I(17,p.LogoGap); I(18,p.CounterGap); I(19,p.LineGap); I(20,p.AlignPercent?1:0);
        I(21,p.Good); I(22,p.Warning); I(23,p.Bad); I(25,png.Length); I(26,p.Language == "en" ? 1 : 0);
        png.CopyTo(b,128); return b;
    }
}

internal static class LogoImport
{
    internal static string Load(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Logo trop volumineux (maximum 2 Mo).");
        using var surface = SKSurface.Create(new SKImageInfo(128, 128));
        var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)) {
            string xml = File.ReadAllText(path);
            ValidateSvg(xml);
            using var svg = new SKSvg();
            svg.FromSvg(xml);
            var picture = svg.Picture ?? throw new InvalidDataException("SVG non reconnu.");
            var bounds = picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidDataException("Dimensions du SVG invalides.");
            float scale = Math.Min(128 / bounds.Width, 128 / bounds.Height);
            canvas.Translate((128-bounds.Width*scale)/2,(128-bounds.Height*scale)/2);
            canvas.Scale(scale); canvas.Translate(-bounds.Left,-bounds.Top); canvas.DrawPicture(picture);
        } else {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("PNG non reconnu.");
            if (codec.EncodedFormat != SKEncodedImageFormat.Png || codec.Info.Width > 4096 || codec.Info.Height > 4096)
                throw new InvalidDataException("Choisissez un PNG de 4096 × 4096 pixels maximum.");
            using var bitmap = SKBitmap.Decode(codec);
            float scale = Math.Min(128f/bitmap.Width,128f/bitmap.Height);
            var dest = SKRect.Create((128-bitmap.Width*scale)/2,(128-bitmap.Height*scale)/2,bitmap.Width*scale,bitmap.Height*scale);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(bitmap,dest,new SKSamplingOptions(SKFilterMode.Linear),paint);
        }
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png,100);
        return Convert.ToBase64String(data.ToArray());
    }
    internal static void ValidateSvg(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 });
        var doc = XDocument.Load(reader);
        if (doc.Root?.Name.LocalName != "svg") throw new InvalidDataException("SVG non reconnu.");
        // SVG is treated as a local drawing, with no scripts, external images, fonts or CSS imports.
        foreach (var e in doc.Descendants()) {
            if (e.Name.LocalName is "script" or "foreignObject" or "image" or "style" or "a" or "animate" or "set")
                throw new InvalidDataException("Utilisez un SVG statique, sans images ni styles externes.");
            foreach (var a in e.Attributes()) {
                string v = a.Value.Trim();
                if (a.Name.LocalName.StartsWith("on",StringComparison.OrdinalIgnoreCase)
                    || (a.Name.LocalName == "href" && !v.StartsWith('#'))
                    || v.Contains("@import",StringComparison.OrdinalIgnoreCase)
                    || (v.Contains("url(",StringComparison.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(v,@"^url\(\s*#[\w-]+\s*\)$")))
                    throw new InvalidDataException("Le SVG contient une référence externe non prise en charge.");
            }
        }
    }
}
