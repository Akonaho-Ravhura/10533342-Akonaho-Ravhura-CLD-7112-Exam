using System.Globalization;
using System.Text;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Data
{
    /// <summary>
    /// Generates test "drone" images for seeding: an aerial view of crop rows coloured like an
    /// NDVI map (green = healthy, yellow/brown = stressed), with optional stress patches.
    /// Output is SVG so no image library is needed.
    /// </summary>
    public static class SampleDroneImages
    {
        public sealed record Patch(double X, double Y, double Rx, double Ry, double Severity);

        public static byte[] Generate(string title, double baseNdvi, int seed, params Patch[] patches)
        {
            const int w = 1200, h = 750;
            var rng = new Random(seed);
            var sb = new StringBuilder();
            string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">");
            sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#3b2a1a\"/>");

            // Slightly rotated crop rows, each split into segments whose colour reflects local NDVI.
            sb.Append($"<g transform=\"rotate(-8 {w / 2} {h / 2})\">");
            for (var y = -120; y < h + 120; y += 14)
            {
                for (var x = -150; x < w + 150; x += 40)
                {
                    var ndvi = baseNdvi + (rng.NextDouble() - 0.5) * 0.12;
                    foreach (var p in patches)
                    {
                        var dx = (x - p.X * w) / (p.Rx * w);
                        var dy = (y - p.Y * h) / (p.Ry * h);
                        var d = dx * dx + dy * dy;
                        if (d < 1.6) ndvi -= p.Severity * Math.Max(0, 1 - d / 1.6);
                    }
                    sb.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"38\" height=\"9\" rx=\"3\" fill=\"{NdviColour(ndvi)}\"/>");
                }
            }
            sb.Append("</g>");

            // Farm track and a centre-pivot style circle for realism.
            sb.Append($"<path d=\"M0 {h - 90} C {w * 0.3} {h - 140}, {w * 0.6} {h - 40}, {w} {h - 110}\" stroke=\"#a08a6a\" stroke-width=\"14\" fill=\"none\" opacity=\"0.8\"/>");

            // Overlay: title, timestamp-ish caption and NDVI legend.
            sb.Append("<rect x=\"0\" y=\"0\" width=\"100%\" height=\"64\" fill=\"#000\" opacity=\"0.55\"/>");
            sb.Append($"<text x=\"24\" y=\"42\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"28\" font-weight=\"600\" fill=\"#ffffff\">{Escape(title)}</text>");
            sb.Append($"<text x=\"{w - 24}\" y=\"42\" text-anchor=\"end\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"22\" fill=\"#ff8a1f\">AgriStream drone · NDVI {F(baseNdvi)}</text>");

            sb.Append($"<g transform=\"translate(24 {h - 52})\"><rect width=\"330\" height=\"36\" rx=\"6\" fill=\"#000\" opacity=\"0.6\"/>");
            for (var i = 0; i < 10; i++)
                sb.Append($"<rect x=\"{12 + i * 22}\" y=\"10\" width=\"22\" height=\"16\" fill=\"{NdviColour(i / 10.0)}\"/>");
            sb.Append("<text x=\"250\" y=\"24\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"14\" fill=\"#fff\">low → high</text></g>");

            sb.Append("</svg>");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>Brown (bare / stressed) → yellow → green (healthy).</summary>
        private static string NdviColour(double ndvi)
        {
            var t = Math.Clamp(ndvi, 0, 1);
            (int r, int g, int b) c = t < 0.35
                ? Lerp((110, 72, 40), (196, 160, 50), t / 0.35)
                : t < 0.6
                    ? Lerp((196, 160, 50), (120, 170, 60), (t - 0.35) / 0.25)
                    : Lerp((120, 170, 60), (34, 120, 40), (t - 0.6) / 0.4);
            return $"#{c.r:x2}{c.g:x2}{c.b:x2}";
        }

        private static (int, int, int) Lerp((int r, int g, int b) a, (int r, int g, int b) b, double t) =>
            ((int)(a.r + (b.r - a.r) * t), (int)(a.g + (b.g - a.g) * t), (int)(a.b + (b.b - a.b) * t));

        private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
