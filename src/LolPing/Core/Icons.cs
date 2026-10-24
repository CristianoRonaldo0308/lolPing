using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LolPing.Core;

/// <summary>
/// Bộ biểu tượng tự vẽ bằng vector (khung 100×100). Không dùng tài sản của trò chơi nào.
/// </summary>
public static class Icons
{
    private static readonly Color Outline = Color.FromRgb(0x0B, 0x0F, 0x1A);
    private static readonly Color Ink = Color.FromRgb(0x2A, 0x17, 0x08);

    public static ImageSource Logo { get; } = Img(
        Fill("M50,4 L96,50 L50,96 L4,50 Z", Color.FromRgb(0x2F, 0x7B, 0xFF), 0),
        Fill("M50,18 L82,50 L50,82 L18,50 Z", Color.FromRgb(0x5A, 0xD1, 0xFF), 0),
        Fill("M50,34 L66,50 L50,66 L34,50 Z", Color.FromRgb(0x10, 0x18, 0x2C), 0));

    // ───────────── Tín hiệu ─────────────

    public static ImageSource Generic() => Img(
        Fill("M50,6 L94,50 L50,94 L6,50 Z", C(0x3D8BFF)),
        Fill("M50,30 L70,50 L50,70 L30,50 Z", C(0xCFE6FF), 0));

    public static ImageSource Danger() => Img(
        Fill("M50,8 L95,88 L5,88 Z", C(0xE53950)),
        Fill("M44,32 H56 L54,64 H46 Z", Colors.White, 0),
        Fill("M44,75 A6,6 0 1 0 56,75 A6,6 0 1 0 44,75 Z", Colors.White, 0));

    public static ImageSource OnMyWay() => Img(
        Fill("M38,6 H62 V50 H84 L50,94 L16,50 H38 Z", C(0x3D8BFF)));

    public static ImageSource Push() => Img(
        Fill("M50,6 L88,44 L73,59 L50,36 L27,59 L12,44 Z", C(0x4CC36A)),
        Fill("M50,42 L88,80 L73,95 L50,72 L27,95 L12,80 Z", C(0x4CC36A)));

    public static ImageSource Assist() => Img(
        Fill("M20,6 H30 V95 H20 Z", C(0xB7F5B7)),
        Fill("M30,10 C50,2 62,22 86,12 V54 C62,64 50,42 30,52 Z", C(0x5DD35D)));

    public static ImageSource AllIn() => Img(
        Fill("M10,10 L25,12 L79,66 L66,79 L12,25 Z", C(0xF2C230)),
        Fill("M58,82 L82,58 L89,65 L65,89 Z", C(0xC8901A)),
        Fill("M90,10 L75,12 L21,66 L34,79 L88,25 Z", C(0xF2C230)),
        Fill("M42,82 L18,58 L11,65 L35,89 Z", C(0xC8901A)));

    public static ImageSource NeedVision() => Img(
        Fill("M4,54 C22,24 74,24 92,54 C74,84 22,84 4,54 Z", C(0x36C9A8)),
        Fill("M36,54 A12,12 0 1 0 60,54 A12,12 0 1 0 36,54 Z", C(0x0E2A24), 0),
        Fill("M76,2 H86 V12 H96 V22 H86 V32 H76 V22 H66 V12 H76 Z", Colors.White));

    public static ImageSource EnemyVision() => Img(
        Fill("M4,50 C22,20 78,20 96,50 C78,80 22,80 4,50 Z", C(0xE8559B)),
        Fill("M50,34 L62,50 L50,66 L38,50 Z", Colors.White, 0));

    public static ImageSource Missing() => Img(
        Fill("M28,34 C28,8 74,6 74,32 C74,48 57,52 57,64 V70 H43 V60 C43,44 60,40 60,32 C60,20 42,20 42,34 Z", C(0xF5C842)),
        Fill("M42,84 A8,8 0 1 0 58,84 A8,8 0 1 0 42,84 Z", C(0xF5C842)));

    public static ImageSource Bait() => Img(
        Stroke("M60,10 V62 C60,86 26,86 26,64 L18,72", Outline, 16),
        Stroke("M60,10 V62 C60,86 26,86 26,64 L18,72", C(0xF59E2A), 9),
        Fill("M52,8 A8,8 0 1 0 68,8 A8,8 0 1 0 52,8 Z", C(0xFFD08A)));

    public static ImageSource Cleared() => Img(
        Fill("M42,8 C46,34 50,38 76,42 C50,46 46,50 42,76 C38,50 34,46 8,42 C34,38 38,34 42,8 Z", C(0x5AA9FF)),
        Fill("M76,56 C78,69 80,70 92,72 C80,74 78,76 76,88 C74,76 72,74 60,72 C72,70 74,69 76,56 Z", C(0xBFE0FF)));

    public static ImageSource Hold() => Img(
        Fill("M50,6 L88,20 V48 C88,72 70,88 50,96 C30,88 12,72 12,48 V20 Z", C(0x8FA3C7)),
        Fill("M36,40 H64 V54 H36 Z", C(0x1A2233), 0));

    public static ImageSource Retreat() => Img(
        Fill("M74,92 V48 C74,30 62,22 48,22 H40 V6 L8,34 L40,62 V44 H48 C52,44 54,46 54,50 V92 Z", C(0xFF7A45)));

    public static ImageSource Target() => Img(
        Stroke("M18,50 A32,32 0 1 0 82,50 A32,32 0 1 0 18,50 Z", Outline, 14),
        Stroke("M50,4 V28 M50,72 V96 M4,50 H28 M72,50 H96", Outline, 14),
        Stroke("M18,50 A32,32 0 1 0 82,50 A32,32 0 1 0 18,50 Z", C(0xFF4D5E), 8),
        Stroke("M50,4 V28 M50,72 V96 M4,50 H28 M72,50 H96", C(0xFF4D5E), 8),
        Fill("M43,50 A7,7 0 1 0 57,50 A7,7 0 1 0 43,50 Z", C(0xFF4D5E)));

    // ───────────── Biểu cảm (mặt tròn) ─────────────

    private const string FaceGeo = "M6,50 A44,44 0 1 0 94,50 A44,44 0 1 0 6,50 Z";
    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xCC, 0x3D);

    private static Drawing Face(Color c) => Fill(FaceGeo, c, 4);
    private static Drawing Dot(double x, double y, double r = 6) =>
        new GeometryDrawing(Frozen(Ink), null, new EllipseGeometry(new Point(x, y), r, r));

    public static ImageSource Smile() => Img(Face(Yellow), Dot(36, 42), Dot(64, 42),
        Stroke("M30,60 Q50,80 70,60", Ink, 6));

    public static ImageSource Laugh() => Img(Face(Yellow),
        Stroke("M27,44 Q36,32 45,44", Ink, 6), Stroke("M55,44 Q64,32 73,44", Ink, 6),
        Fill("M28,56 H72 Q68,86 50,86 Q32,86 28,56 Z", Ink, 0),
        Fill("M38,74 Q50,66 62,74 Q58,84 50,84 Q42,84 38,74 Z", C(0xE8556B), 0));

    public static ImageSource Sad() => Img(Face(Yellow), Dot(36, 44), Dot(64, 44),
        Stroke("M32,74 Q50,58 68,74", Ink, 6),
        Fill("M68,52 C60,66 62,72 69,72 C76,72 78,66 68,52 Z", C(0x5AB4FF), 2));

    public static ImageSource Angry() => Img(Face(C(0xFF8A3D)),
        Stroke("M24,30 L44,40", Ink, 6), Stroke("M76,30 L56,40", Ink, 6),
        Dot(36, 48, 5), Dot(64, 48, 5), Stroke("M32,74 Q50,62 68,74", Ink, 6));

    public static ImageSource Wow() => Img(Face(Yellow), Dot(36, 40, 7), Dot(64, 40, 7),
        new GeometryDrawing(Frozen(Ink), null, new EllipseGeometry(new Point(50, 68), 9, 12)));

    public static ImageSource Sleepy() => Img(Face(C(0xC9B8FF)),
        Stroke("M28,48 H44", Ink, 6), Stroke("M56,48 H72", Ink, 6), Stroke("M44,68 H56", Ink, 6),
        Stroke("M70,6 H88 L70,24 H88", Colors.White, 5));

    public static ImageSource Cool() => Img(Face(Yellow),
        Fill("M18,34 H82 V42 C82,56 60,58 56,44 H44 C40,58 18,56 18,42 Z", C(0x141826), 0),
        Stroke("M36,68 Q54,76 68,62", Ink, 6));

    public static ImageSource Sweat() => Img(Face(Yellow), Dot(36, 44), Dot(64, 44),
        Stroke("M32,68 Q41,62 50,68 Q59,74 68,68", Ink, 6),
        Fill("M80,10 C71,26 73,32 80,32 C87,32 89,26 80,10 Z", C(0x8FD3FF), 2));

    public static ImageSource Cry() => Img(Face(Yellow),
        Stroke("M28,44 Q36,52 44,44", Ink, 6), Stroke("M56,44 Q64,52 72,44", Ink, 6),
        Stroke("M33,52 V88", C(0x5AB4FF), 7), Stroke("M67,52 V88", C(0x5AB4FF), 7),
        Fill("M38,76 Q50,60 62,76 Z", Ink, 0));

    private const string HeartGeo =
        "M50,90 C20,68 6,52 6,32 C6,16 18,8 30,8 C40,8 46,14 50,22 C54,14 60,8 70,8 C82,8 94,16 94,32 C94,52 80,68 50,90 Z";

    public static ImageSource Love() => Img(Fill(HeartGeo, C(0xFF5C8A)),
        Fill("M24,26 C24,20 30,16 36,18", C(0xFFC2D4), 0));

    public static ImageSource Gg()
    {
        var text = new FormattedText("GG", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Italic, FontWeights.Black, FontStretches.Normal),
            36, Brushes.White, 1.0);
        var geo = text.BuildGeometry(new Point(50 - text.Width / 2, 44 - text.Height / 2));
        return Img(Fill(HeartGeo, C(0xFF7BA3)),
            new GeometryDrawing(Frozen(Colors.White), new Pen(Frozen(C(0xB0234F)), 2), geo));
    }

    // ───────────── Thẻ âm thanh (khi âm trong kho chưa có ảnh) ─────────────

    private static readonly uint[] CardColors = { 0x3D8BFF, 0xE8559B, 0x4CC36A, 0xF59E2A, 0x9B6BFF, 0x36C9A8, 0xFF5C5C, 0xF2C230 };

    public static ImageSource ClipCard(string name)
    {
        int h = 0;
        foreach (var ch in name) h = h * 31 + ch;
        var color = C(CardColors[(h & 0x7FFFFFFF) % CardColors.Length]);
        return Img(
            Fill("M14,6 H86 Q94,6 94,14 V86 Q94,94 86,94 H14 Q6,94 6,86 V14 Q6,6 14,6 Z", color),
            Fill("M22,40 H36 L54,24 V76 L36,60 H22 Z", Colors.White, 0),
            Stroke("M64,38 Q72,50 64,62", Colors.White, 6),
            Stroke("M72,28 Q86,50 72,72", Colors.White, 6));
    }

    // ───────────── Hàm dựng ─────────────

    public static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush Gradient(Color c)
    {
        var light = Color.FromRgb(
            (byte)(c.R + (255 - c.R) * 0.35), (byte)(c.G + (255 - c.G) * 0.35), (byte)(c.B + (255 - c.B) * 0.35));
        var b = new LinearGradientBrush(light, c, 90);
        b.Freeze();
        return b;
    }

    private static GeometryDrawing Fill(string data, Color c, double outline = 4)
    {
        Pen? pen = outline > 0 ? new Pen(Frozen(Outline), outline) { LineJoin = PenLineJoin.Round } : null;
        return new GeometryDrawing(outline > 0 ? Gradient(c) : Frozen(c), pen, Geometry.Parse(data));
    }

    private static GeometryDrawing Stroke(string data, Color c, double width) =>
        new(null, new Pen(Frozen(c), width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        }, Geometry.Parse(data));

    private static ImageSource Img(params Drawing[] drawings)
    {
        var group = new DrawingGroup();
        // Khung trong suốt cố định để mọi biểu tượng có cùng kích thước khi co giãn.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(-6, -6, 112, 112))));
        foreach (var d in drawings) group.Children.Add(d);
        var img = new DrawingImage(group);
        img.Freeze();
        return img;
    }
}
