using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Ironmon.Tracker.AchievementOverlay;

/// <summary>
/// Draws the notification at physical resolution using Windows GDI+, without a desktop UI framework.
/// </summary>
internal static class OverlayCanvas
{
    private const string _fontFamily = "Arial";
    private const string _ellipsis = "\u2026";
    private const float _titleWidth = 249;
    private const float _lineHeight = 22;
    private static readonly Color _panelLeft = Color.FromArgb(191, 26, 40, 53);
    private static readonly Color _panelRight = Color.FromArgb(191, 19, 26, 36);
    private static readonly Color _border = Color.FromArgb(191, 67, 87, 106);
    private static readonly Color _divider = Color.FromArgb(191, 69, 103, 121);
    private static readonly Color _text = Color.FromArgb(255, 245, 248, 252);

    /// <summary>
    /// Draws one slide phase into the native window's premultiplied-alpha surface.
    /// </summary>
    /// <param name="target">The physical pixel buffer.</param>
    /// <param name="artwork">The approved title and images.</param>
    /// <param name="placement">The physical bounds and viewport scale.</param>
    /// <param name="original">Whether to compare the small source at larger sizes.</param>
    /// <param name="offset">The slide offset in physical pixels.</param>
    internal static void Draw(Bitmap target, OverlayArtwork artwork, OverlayPlacement placement, bool original, int offset)
    {
        using Graphics graphics = Graphics.FromImage(target);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TranslateTransform(offset, 0);
        graphics.ScaleTransform((float)placement.Scale, (float)placement.Scale);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using GraphicsPath outline = RoundedRectangle(new(0, 0, 340, 64), 5);
        using LinearGradientBrush background = new(new RectangleF(0, 0, 340, 64), _panelLeft, _panelRight, 0f);
        background.InterpolationColors = new ColorBlend { Colors = [_panelLeft, _panelRight, _panelRight], Positions = [0, 0.225f, 1] };
        graphics.FillPath(background, outline);
        using Pen border = new(_border, 1);
        using GraphicsPath edge = RoundedRectangle(new(0.5f, 0.5f, 339, 63), 4.5f);
        graphics.DrawPath(border, edge);
        using SolidBrush divider = new(_divider);
        graphics.FillRectangle(divider, 63, 12, 1, 40);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = original ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        using ImageAttributes sampling = new();
        sampling.SetWrapMode(WrapMode.TileFlipXY);
        Bitmap source = artwork.Select(placement.IconPixels, original);
        graphics.DrawImage(source, new Rectangle(7, 7, 50, 50), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, sampling);
        DrawTitle(graphics, artwork.Title);
    }

    /// <summary>
    /// Measures all title lines using the production font before applying the two-line limit.
    /// </summary>
    /// <param name="title">The complete achievement title.</param>
    /// <returns>The required untruncated lines.</returns>
    internal static string[] MeasureTitle(string title)
    {
        using Bitmap bitmap = new(1, 1);
        using Graphics graphics = Graphics.FromImage(bitmap);
        using Font font = new(_fontFamily, 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using StringFormat format = TitleFormat();
        return WrapTitle(graphics, font, format, title);
    }

    /// <summary>
    /// Draws two vertically centered lines and ellipsizes future oversized titles.
    /// </summary>
    /// <param name="graphics">The scaled canvas.</param>
    /// <param name="title">The approved title.</param>
    private static void DrawTitle(Graphics graphics, string title)
    {
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using Font font = new(_fontFamily, 20, FontStyle.Bold, GraphicsUnit.Pixel);
        using StringFormat format = TitleFormat();
        using SolidBrush brush = new(_text);
        string[] lines = WrapTitle(graphics, font, format, title);
        int count = Math.Min(2, lines.Length);
        float top = (64 - count * _lineHeight) / 2 - (font.GetHeight(graphics) - _lineHeight) / 2;
        for (int index = 0; index < count; index++)
        {
            string line = lines[index];
            if (index == 1 && lines.Length > 2)
            {
                while (line.Length > 0 && graphics.MeasureString(line + _ellipsis, font, int.MaxValue, format).Width > _titleWidth)
                    line = line[..^1];

                line = line.TrimEnd() + _ellipsis;
            }

            graphics.DrawString(line, font, brush, new PointF(77, top + index * _lineHeight), format);
        }
    }

    /// <summary>
    /// Wraps words and oversized text elements without discarding title characters.
    /// </summary>
    /// <param name="graphics">The measurement canvas.</param>
    /// <param name="font">The approved font.</param>
    /// <param name="format">The shared measurement options.</param>
    /// <param name="title">The complete title.</param>
    /// <returns>The untruncated lines.</returns>
    private static string[] WrapTitle(Graphics graphics, Font font, StringFormat format, string title)
    {
        List<string> lines = [];
        string line = string.Empty;
        foreach (string word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && graphics.MeasureString(line + word, font, int.MaxValue, format).Width > _titleWidth)
            {
                lines.Add(line.TrimEnd());
                line = string.Empty;
            }

            var characters = System.Globalization.StringInfo.GetTextElementEnumerator(word);
            while (characters.MoveNext())
            {
                string character = characters.GetTextElement();
                if (line.Length > 0 && graphics.MeasureString(line + character, font, int.MaxValue, format).Width > _titleWidth)
                {
                    lines.Add(line.TrimEnd());
                    line = string.Empty;
                }

                line += character;
            }

            line += ' ';
        }

        if (line.Length > 0)
            lines.Add(line.TrimEnd());

        return [.. lines];
    }

    /// <summary>
    /// Creates matching rendering and measurement options without hidden layout padding.
    /// </summary>
    /// <returns>The owned typographic format.</returns>
    private static StringFormat TitleFormat() => new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };

    /// <summary>
    /// Creates the notification's rounded panel path.
    /// </summary>
    /// <param name="bounds">The unscaled panel rectangle.</param>
    /// <param name="radius">The corner radius in game pixels.</param>
    /// <returns>The caller-owned closed path.</returns>
    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        GraphicsPath path = new();
        float diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
