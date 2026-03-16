namespace ESAPI_IsodoseViewer.Core.Models
{
    /// <summary>
    /// Represents a single isodose level with a percentage threshold and display color.
    /// </summary>
    public class IsodoseLevel
    {
        /// <summary>
        /// Fraction of the reference dose (e.g. 1.07 = 107%).
        /// </summary>
        public double Fraction { get; set; }

        /// <summary>
        /// Display label shown in the UI legend (e.g. "107%").
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// BGRA color as a uint (e.g. 0xFFFF0000 = opaque red).
        /// Alpha will be overridden during rendering.
        /// </summary>
        public uint Color { get; set; }

        /// <summary>
        /// Alpha value 0-255 used during rendering. Default ~30% opacity.
        /// </summary>
        public byte Alpha { get; set; } = 0x4C;

        public IsodoseLevel(double fraction, string label, uint color)
        {
            Fraction = fraction;
            Label = label;
            Color = color;
        }

        /// <summary>
        /// Returns the default clinical isodose level set.
        /// </summary>
        public static IsodoseLevel[] GetDefaults()
        {
            return new[]
            {
                new IsodoseLevel(1.07, "107%", 0xFFFF0000),  // Red
                new IsodoseLevel(0.95, "95%",  0xFF00FF00),  // Green
                new IsodoseLevel(0.80, "80%",  0xFF00FFFF),  // Cyan
                new IsodoseLevel(0.50, "50%",  0xFF0000FF),  // Blue
            };
        }

        /// <summary>
        /// Returns the System.Windows.Media.Color for UI display (legend).
        /// </summary>
        public System.Windows.Media.Color GetMediaColor()
        {
            byte r = (byte)((Color >> 16) & 0xFF);
            byte g = (byte)((Color >> 8) & 0xFF);
            byte b = (byte)(Color & 0xFF);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }
    }
}