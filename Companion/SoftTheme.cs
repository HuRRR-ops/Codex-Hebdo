using System.Drawing.Drawing2D;

namespace CodexHebdo;

internal static class SoftTheme
{
    internal static Color Background { get; private set; }
    internal static Color Surface { get; private set; }
    internal static Color Ink { get; private set; }
    internal static Color Muted { get; private set; }
    internal static Color Accent { get; private set; }
    internal static Color AccentInk { get; private set; }
    internal static Color Tint { get; private set; }
    internal static Color Border { get; private set; }
    internal static Color Sidebar { get; private set; }
    internal static bool Dark { get; private set; }
    static SoftTheme() => Set(new());
    internal static Color Mix(Color a, Color b, double weight) => Color.FromArgb(
        (int)(a.R*(1-weight)+b.R*weight),(int)(a.G*(1-weight)+b.G*weight),(int)(a.B*(1-weight)+b.B*weight));
    internal static void Set(Preferences p)
    {
        Dark=p.UiTheme=="midnight";
        int value=p.UiAccent>=0?p.UiAccent:p.UiTheme switch {
            "reference"=>0x099B8B,"lavender"=>0x7861B5,"rose"=>0xB44A79,"sand"=>0x9C652E,"midnight"=>0x8DA8EF,_=>0x2F70B5
        };
        Accent=Color.FromArgb(unchecked((int)0xff000000)|value);
        AccentInk=ContrastInk(Accent);
        Ink=Dark?Color.FromArgb(232,238,251):Color.FromArgb(25,43,67);
        Muted=Dark?Color.FromArgb(164,181,211):Color.FromArgb(103,122,149);
        Background=Dark?Color.FromArgb(24,32,49):Mix(Color.White,Accent,.035);
        Surface=Dark?Color.FromArgb(33,44,63):Color.White;
        Tint=Mix(Surface,Accent,Dark?.2:.15);
        Sidebar=Mix(Background,Accent,Dark?.08:.04);
        Border=Mix(Surface,Accent,Dark?.25:.14);
    }
    internal static Color ContrastInk(Color c)
    {
        static double Linear(byte v) { double s=v/255d; return s<=.04045?s/12.92:Math.Pow((s+.055)/1.055,2.4); }
        double luminance=.2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);
        return luminance>.179?Color.FromArgb(18,29,47):Color.White;
    }
    internal static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var p = new GraphicsPath(); float d = Math.Min(radius*2, Math.Min(bounds.Width,bounds.Height));
        p.AddArc(bounds.X,bounds.Y,d,d,180,90); p.AddArc(bounds.Right-d,bounds.Y,d,d,270,90);
        p.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90); p.AddArc(bounds.X,bounds.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
    }
}

internal class SoftButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; init; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Quiet { get; init; }
    private bool selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get => selected; set { selected=value; Invalidate(); } }
    private Color? swatch;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Color? Swatch { get => swatch; set { swatch=value; Invalidate(); } }
    private bool hover, pressed;
    internal SoftButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        BackColor = SoftTheme.Surface; ForeColor = SoftTheme.Ink; Padding = new Padding(9,4,9,4);
    }
    protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=false; pressed=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics; float scale=DeviceDpi/96f;
        g.Clear(Parent?.BackColor ?? SoftTheme.Background); g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=SoftTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),9*scale);
        var fill=Primary ? (pressed ? SoftTheme.Mix(SoftTheme.Accent,Color.Black,.13) : hover ? SoftTheme.Mix(SoftTheme.Accent,Color.White,.12) : SoftTheme.Accent)
            : (hover || Selected) ? SoftTheme.Tint : Quiet ? (Parent?.BackColor ?? SoftTheme.Background) : SoftTheme.Surface;
        using var brush=new SolidBrush(fill); g.FillPath(brush,path);
        using var pen=new Pen(Primary || Quiet ? fill : SoftTheme.Border); g.DrawPath(pen,path);
        var textBounds=ClientRectangle;
        if (Swatch is { } color) {
            float d=18*scale; using var dot=new SolidBrush(color);
            g.FillEllipse(dot,14*scale,(Height-d)/2,d,d);
            using var edge=new Pen(Color.FromArgb(50,SoftTheme.Ink)); g.DrawEllipse(edge,14*scale,(Height-d)/2,d,d);
            textBounds.X=(int)(40*scale); textBounds.Width-=textBounds.X+(int)(25*scale);
        }
        if(Swatch.HasValue) { using var chevron=new Pen(SoftTheme.Ink,1.2f*scale); float cx=Width-23*scale,cy=Height/2f;g.DrawLines(chevron,[new PointF(cx-4*scale,cy-2*scale),new PointF(cx,cy+2*scale),new PointF(cx+4*scale,cy-2*scale)]); }
        TextRenderer.DrawText(g,Text,Font,textBounds,Enabled?(Primary?SoftTheme.AccentInk:Selected?SoftTheme.Accent:SoftTheme.Ink):SoftTheme.Muted,
            TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.VerticalCenter|(Swatch.HasValue?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter)|TextFormatFlags.EndEllipsis);
        if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(ClientRectangle,-5,-5),Primary?SoftTheme.AccentInk:SoftTheme.Accent,fill);
    }
}

internal sealed class SoftCard : GroupBox
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Description { get; set; } = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string IconText { get; set; } = "";
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string Example { get; set; } = "";
    internal SoftCard()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=SoftTheme.Surface; ForeColor=SoftTheme.Ink;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics; float scale=DeviceDpi/96f;
        g.Clear(Parent?.BackColor ?? SoftTheme.Background); g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=SoftTheme.Round(new RectangleF(.5f,.5f,Width-1,Height-1),13*scale);
        using var fill=new SolidBrush(SoftTheme.Surface); using var edge=new Pen(SoftTheme.Border);
        g.FillPath(fill,path); g.DrawPath(edge,path);
        using var title=new Font(Font.FontFamily,9,FontStyle.Bold);
        float left=IconText.Length>0?46:12;
        if(IconText.Length>0) {
            using var bubble=new SolidBrush(SoftTheme.Tint);
            g.FillEllipse(bubble,12*scale,8*scale,28*scale,28*scale);
            using var iconFont=new Font("Segoe UI Symbol",19,FontStyle.Regular);
            LineIcons.Draw(g,IconText,new RectangleF(18*scale,14*scale,16*scale,16*scale),SoftTheme.Ink);
        }
        TextRenderer.DrawText(g,Text,title,new Rectangle((int)(left*scale),(int)(10*scale),Width-(int)((left+20)*scale),(int)(24*scale)),SoftTheme.Ink,TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.Left|TextFormatFlags.NoPadding);
        using var helper=new Font(Font.FontFamily,8.5f);
        TextRenderer.DrawText(g,Example,helper,new Rectangle((int)(Width-240*scale),(int)(22*scale),(int)(220*scale),(int)(20*scale)),SoftTheme.Muted,TextFormatFlags.Right|TextFormatFlags.NoPadding);
        if(Description.Length>0) {
            TextRenderer.DrawText(g,Description,Font,new Rectangle((int)(left*scale),(int)(42*scale),Width-(int)((left+20)*scale),(int)(24*scale)),SoftTheme.Muted,TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.Left|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis);
            using var divider=new Pen(SoftTheme.Border); g.DrawLine(divider,left*scale,70*scale,Width-20*scale,70*scale);
        }
    }
}

internal sealed class SoftTabs : TabControl
{
    internal SoftTabs()
    {
        SizeMode=TabSizeMode.Fixed; Appearance=TabAppearance.FlatButtons;
        ItemSize=new Size(1,1); TabStop=false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer,true);
    }
    // Navigation is provided by ordinary accessible buttons above the pages.
    public override Rectangle DisplayRectangle => ClientRectangle;
}
