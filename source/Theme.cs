using System.Drawing;
using System.Windows.Forms;

internal enum ColorMode { Red, Green, Orange, Blue, Purple, Cyan, Multicolor }

internal static class F1Theme
{
    internal static readonly Color Background=Color.FromArgb(32,32,32);
    internal static readonly Color Surface=Color.FromArgb(38,38,38);
    internal static readonly Color Edge=Color.FromArgb(96,96,96);
    internal static readonly Color Muted=Color.FromArgb(176,176,176);
    internal static readonly Color Green=Color.FromArgb(48,209,88);
    internal static readonly Color Red=Color.FromArgb(255,69,58);
    internal static readonly Color Orange=Color.FromArgb(255,159,10);
    internal static readonly Color Blue=Color.FromArgb(10,132,255);
    internal static readonly Color Purple=Color.FromArgb(191,90,242);
    internal static readonly Color Cyan=Color.FromArgb(100,210,255);
    internal static readonly Color Accent=Green;
    internal static readonly Color Error=Red;
    internal static readonly Color Input=Color.FromArgb(47,47,47);

    internal static Color Tint(Color color,float amount)
    {return Color.FromArgb((int)(Background.R+(color.R-Background.R)*amount),(int)(Background.G+(color.G-Background.G)*amount),(int)(Background.B+(color.B-Background.B)*amount));}

    internal static void StyleButton(ButtonBase button,Color border)
    {
        button.FlatStyle=FlatStyle.Flat;button.UseVisualStyleBackColor=false;button.Cursor=Cursors.Hand;
        button.BackColor=Surface;button.ForeColor=Color.White;button.FlatAppearance.BorderColor=border;
        button.FlatAppearance.MouseOverBackColor=Tint(border,0.16f);button.FlatAppearance.MouseDownBackColor=Tint(border,0.25f);
    }

    internal static Color SpeedColor(long rate)
    {
        switch(rate){case 1:return Green;case 2:return Cyan;case 3:return Blue;case 5:return Orange;case 10:return Red;default:return Muted;}
    }

    internal static Color ModeColor(ColorMode mode)
    {
        switch(mode){case ColorMode.Red:return Red;case ColorMode.Green:return Green;case ColorMode.Orange:return Orange;case ColorMode.Blue:return Blue;case ColorMode.Purple:return Purple;case ColorMode.Cyan:return Cyan;case ColorMode.Multicolor:return Green;default:return Red;}
    }

    internal static Color SpeedColor(ColorMode mode,long rate)
    {return mode==ColorMode.Multicolor?SpeedColor(rate):ModeColor(mode);}

    internal static Color ActionColor(int index)
    {
        switch(index){case 0:return Green;case 1:return Cyan;case 2:return Blue;case 3:return Orange;case 4:return Red;case 5:return Purple;case 6:return Purple;case 7:return Cyan;case 8:return Blue;default:return Muted;}
    }

    internal static Color ActionColor(ColorMode mode,int index)
    {return mode==ColorMode.Multicolor?ActionColor(index):ModeColor(mode);}
}
