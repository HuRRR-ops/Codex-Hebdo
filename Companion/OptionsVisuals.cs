using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SkiaSharp;
using Svg.Skia;

namespace CodexHebdo;

internal sealed class SidebarPanel : Panel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Language { get; set; } = "fr";
    private readonly Bitmap brand;
    internal SidebarPanel() {
        DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true);
        using var stream=typeof(SidebarPanel).Assembly.GetManifestResourceStream("CodexHebdo.calendar.png")!;
        using var image=Image.FromStream(stream); brand=new Bitmap(image);
        AccessibleName="Codex Hebdo";
    }
    protected override void Dispose(bool disposing) { if(disposing) brand.Dispose(); base.Dispose(disposing); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g=e.Graphics; float d=DeviceDpi/96f;
        g.Clear(SoftTheme.Sidebar); g.SmoothingMode=SmoothingMode.AntiAlias;
        using var wave=new SolidBrush(SoftTheme.Mix(SoftTheme.Sidebar,SoftTheme.Accent,.07));
        using var wave2=new SolidBrush(SoftTheme.Mix(SoftTheme.Sidebar,SoftTheme.Accent,.045));
        g.FillEllipse(wave,-Width*.6f,Height-140*d,Width*1.7f,250*d);
        g.FillEllipse(wave2,Width*.25f,Height-180*d,Width*1.5f,270*d);
        float scale=Math.Min(52*d/brand.Width,52*d/brand.Height);
        float width=brand.Width*scale,height=brand.Height*scale;
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.DrawImage(brand,(Width-width)/2,8*d,width,height);

    }
}

internal sealed class NavigationButton : SoftButton
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Description { get; init; } = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Glyph { get; init; } = "";
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics; float d=DeviceDpi/96f;
        g.Clear(SoftTheme.Sidebar); g.SmoothingMode=SmoothingMode.AntiAlias;
        if(Selected) {
            using var path=SoftTheme.Round(new RectangleF(0,0,Width-1,Height-1),12*d);
            using var fill=new SolidBrush(SoftTheme.Tint); g.FillPath(fill,path);
        }
        using var bubble=new SolidBrush(Selected?SoftTheme.Mix(SoftTheme.Tint,SoftTheme.Accent,.12):SoftTheme.Mix(SoftTheme.Sidebar,SoftTheme.Accent,.04));
        g.FillEllipse(bubble,8*d,9*d,23*d,23*d);
        using var icon=new Font("Segoe UI Symbol",16);
        LineIcons.Draw(g,Glyph,new RectangleF(11*d,12*d,17*d,17*d),SoftTheme.Ink);
        using var title=new Font(Font,Selected?FontStyle.Bold:FontStyle.Regular);
        TextRenderer.DrawText(g,Text,title,new Rectangle((int)(37*d),(int)(11*d),Width-(int)(40*d),(int)(22*d)),SoftTheme.Ink,TextFormatFlags.NoPadding);
        using var small=new Font(Font.FontFamily,8.5f);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-4,-4));
    }
}

internal sealed class SoftCheck : CheckBox
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Toggle { get; init; }
    internal SoftCheck()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Cursor=Cursors.Hand;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        float d=DeviceDpi/96f;
        var text=TextRenderer.MeasureText(Text,Font);
        return new Size(text.Width+(int)((Toggle?43:25)*DeviceDpi/96f),Math.Max(text.Height,(int)Math.Ceiling(19*d)));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics; float d=DeviceDpi/96f; g.Clear(Parent?.BackColor??SoftTheme.Surface); g.SmoothingMode=SmoothingMode.AntiAlias;
        float w=(Toggle?30:14)*d,h=(Toggle?17:14)*d,y=(Height-h)/2;
        using var shape=SoftTheme.Round(new RectangleF(1,y,w,h),(Toggle?11:4)*d);
        using var fill=new SolidBrush(Checked?SoftTheme.Accent:SoftTheme.Surface);
        using var edge=new Pen(Checked?SoftTheme.Accent:SoftTheme.Muted,d);
        g.FillPath(fill,shape);g.DrawPath(edge,shape);
        if(Toggle) {
            using var knob=new SolidBrush(Checked?Color.White:SoftTheme.Muted);
            float diameter=h-5*d; g.FillEllipse(knob,Checked?w-diameter-2*d:3*d,y+2.5f*d,diameter,diameter);
        } else if(Checked) {
            using var check=new Pen(SoftTheme.AccentInk,1.8f*d);
            g.DrawLines(check,[new PointF(4*d,y+8*d),new PointF(7*d,y+11*d),new PointF(13*d,y+5*d)]);
        }
        TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(w+11*d),0,Width-(int)(w+11*d),Height),SoftTheme.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.PreserveGraphicsClipping);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-2,-2));
    }
}

internal sealed class WidgetPreview : Control
{
    private Preferences preferences=new();
    private Bitmap? logo;
    private string? logoKey;
    internal WidgetPreview() { DoubleBuffered=true; }
    internal void RefreshPreview(Preferences p)
    {
        preferences=p;
        string key=p.LogoPng??"default";
        if(key!=logoKey) {
            logo?.Dispose();
            if(p.LogoPng is { } png) {
                using var stream=new MemoryStream(Convert.FromBase64String(png)); using var image=Image.FromStream(stream); logo=new Bitmap(image);
            } else {
                using var resource=typeof(WidgetPreview).Assembly.GetManifestResourceStream("CodexHebdo.logo.svg")!;
                using var reader=new StreamReader(resource); using var svg=new SKSvg(); svg.FromSvg(reader.ReadToEnd());
                using var surface=SKSurface.Create(new SKImageInfo(128,128)); surface.Canvas.Clear(SKColors.Transparent);
                var bounds=svg.Picture!.CullRect; float scale=Math.Min(128/bounds.Width,128/bounds.Height);
                surface.Canvas.Translate((128-bounds.Width*scale)/2,(128-bounds.Height*scale)/2); surface.Canvas.Scale(scale);surface.Canvas.Translate(-bounds.Left,-bounds.Top);surface.Canvas.DrawPicture(svg.Picture);
                using var image=surface.Snapshot();using var data=image.Encode(SKEncodedImageFormat.Png,100);using var stream=new MemoryStream(data.ToArray());using var bitmap=Image.FromStream(stream);logo=new Bitmap(bitmap);
            }
            logoKey=key;
        }
        Invalidate();
    }
    protected override void Dispose(bool disposing) { if(disposing)logo?.Dispose();base.Dispose(disposing); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var p=preferences;var g=e.Graphics;float d=DeviceDpi/96f; g.Clear(Parent?.BackColor??SoftTheme.Background);g.SmoothingMode=SmoothingMode.AntiAlias;
        using var panel=SoftTheme.Round(new RectangleF(0,0,Width-1,Height-1),14*d);
        using var background=new SolidBrush(SoftTheme.Surface);using var border=new Pen(SoftTheme.Border);g.FillPath(background,panel);g.DrawPath(border,panel);
        using var small=new Font("Segoe UI",8.5f,FontStyle.Bold);
        TextRenderer.DrawText(g,p.Language=="en"?"◉  Live preview":"◉  Aperçu en direct",small,new Point((int)(16*d),(int)(12*d)),SoftTheme.Ink);
        using var helper=new Font("Segoe UI",8);
        var reading=Program.CurrentReading;long now=Program.Now;
        string percent=reading?.Weekly is { } q && q.IsFresh(now)?$"{Math.Floor(q.Remaining)}%":"--";
        var credits=reading?.Credits;
        string resets=credits is not null && credits.IsFresh(now)?"↻"+(credits.Available>99?"99+":credits.Available.ToString()):"↻--";
        string title=p.Language=="en"?"Weekly":"Hebdo";
        const float magnify=1.25f;
        Font Make(TextStyle s)=>new("Segoe UI",s.Size*d*magnify,(s.Bold?FontStyle.Bold:0)|(s.Italic?FontStyle.Italic:0),GraphicsUnit.Pixel);
        using var lf=Make(p.Label);using var pf=Make(p.Percent);using var cf=Make(p.Credits);
        var flags=TextFormatFlags.NoPadding|TextFormatFlags.SingleLine;
        float Measure(string text,Font font,bool visible)=>visible?TextRenderer.MeasureText(g,text,font,Size.Empty,flags).Width+2*d:0;
        float lw=Measure(title,lf,p.Label.Visible),pw=Measure(percent,pf,p.Percent.Visible),cw=Measure(resets,cf,p.Credits.Visible);
        float gap=pw>0&&cw>0?p.CounterGap*d*magnify:0,row=pw+gap+cw,width=Math.Max(lw,row);
        float labelLeft=(width-lw)/2,numbersLeft=(width-row)/2;
        if(p.AlignPercent && pw>0 && lw>0) {numbersLeft=Math.Max(0,(lw-pw)/2);labelLeft=Math.Max(0,(pw-lw)/2);width=Math.Max(labelLeft+lw,numbersLeft+row);}
        float logoSize=p.LogoVisible?p.LogoSize*d*magnify:0,logoWidth=logoSize>0?logoSize+p.LogoGap*d*magnify:0;
        float lh=p.Label.Visible?lf.Height:0,rh=Math.Max(p.Percent.Visible?pf.Height:0,p.Credits.Visible?cf.Height:0),lines=lh>0?p.LineGap*d*magnify:0;
        float totalW=width+logoWidth,totalH=Math.Max(logoSize,lh+rh+lines),x=Width-totalW-28*d,y=(Height-totalH)/2;
        Color Rgb(int c)=>Color.FromArgb(unchecked((int)0xff000000)|c);
        if(logoSize>0 && logo is not null) {
            using var attributes=new ImageAttributes();
            if(p.LogoPng is null || p.TintLogo) {
                var c=p.LogoColor>=0?Rgb(p.LogoColor):p.LogoPng is null?SoftTheme.Ink:Color.FromArgb(40,40,40);
                attributes.SetColorMatrix(new ColorMatrix(new float[][]{[0,0,0,0,0],[0,0,0,0,0],[0,0,0,0,0],[0,0,0,1,0],[c.R/255f,c.G/255f,c.B/255f,0,1]}));
            }
            g.DrawImage(logo,new Rectangle((int)x,(int)(y+(totalH-logoSize)/2),(int)logoSize,(int)logoSize),0,0,logo.Width,logo.Height,GraphicsUnit.Pixel,attributes);
        }
        float tx=x+logoWidth,ty=y+(totalH-lh-rh-lines)/2;
        void Draw(string text,Font font,float dx,float dy,Color color)=>TextRenderer.DrawText(g,text,font,new Point((int)dx,(int)dy),color,flags);
        if(p.Label.Visible) Draw(title,lf,tx+labelLeft,ty,p.Label.AutoColor?SoftTheme.Ink:Rgb(p.Label.Color));
        if(p.Percent.Visible) Draw(percent,pf,tx+numbersLeft,ty+lh+lines,percent=="--"?SoftTheme.Muted:Rgb(p.Percent.AutoColor?(reading!.Weekly!.Remaining>50?p.Good:reading.Weekly.Remaining>20?p.Warning:p.Bad):p.Percent.Color));
        if(p.Credits.Visible) Draw(resets,cf,tx+numbersLeft+pw+gap,ty+lh+lines,resets=="↻--"?SoftTheme.Muted:Rgb(p.Credits.AutoColor?(credits!.Available==0?p.Bad:p.Good):p.Credits.Color));
        AccessibleName=(p.Language=="en"?"Preview: ":"Aperçu : ")+title+" "+percent+" "+resets;
    }
}

internal static class RoundedDrawing
{
    internal static void FillRoundedRectangleCompat(this Graphics g,Brush brush,RectangleF rectangle,float radius)
    {
        using var path=SoftTheme.Round(rectangle,radius);g.FillPath(brush,path);
    }
}

internal static class LineIcons
{
    internal static void Draw(Graphics g,string icon,RectangleF bounds,Color color)
    {
        var saved=g.Save();g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/24,bounds.Height/24);
        using var p=new Pen(color,1.6f) { StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round };
        if(icon=="▧") {
            using var box=SoftTheme.Round(new RectangleF(1,1,22,22),3);g.DrawPath(p,box);g.DrawEllipse(p,6,5,4,4);
            g.DrawLines(p,[new PointF(2,19),new PointF(9,12),new PointF(13,16),new PointF(18,10),new PointF(23,16)]);
        } else if(icon=="▦") {
            foreach(int x in new[]{1,14}) foreach(int y in new[]{1,14}) {using var box=SoftTheme.Round(new RectangleF(x,y,9,9),2);g.DrawPath(p,box);}
        } else if(icon=="◉") {
            using var shape=new GraphicsPath();shape.AddBezier(22,13,26,-3,0,-4,1,14);shape.AddBezier(1,14,3,25,17,26,15,19);shape.AddBezier(15,19,11,14,22,18,22,13);g.DrawPath(p,shape);
            foreach(var a in new[]{new PointF(6,9),new PointF(11,5),new PointF(17,7)})g.DrawEllipse(p,a.X,a.Y,1.5f,1.5f);
        } else if(icon=="▤") {
            using var box=SoftTheme.Round(new RectangleF(4,1,16,22),2);g.DrawPath(p,box);g.DrawLine(p,8,7,16,7);g.DrawLine(p,8,11,16,11);g.DrawLine(p,8,15,12,15);
        } else {
            using var font=new Font("Segoe UI Symbol",21,FontStyle.Regular,GraphicsUnit.Pixel);
            using var ink=new SolidBrush(color);using var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center };
            g.DrawString(icon,font,ink,new RectangleF(0,0,24,24),format);
        }
        g.Restore(saved);
    }
}
