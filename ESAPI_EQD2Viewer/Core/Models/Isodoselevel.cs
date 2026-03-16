using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ESAPI_EQD2Viewer.Core.Models
{
    /// <summary>
    /// Represents a single isodose level with percentage threshold, color, and visibility.
    /// Now supports runtime editing from the UI.
    /// </summary>
    public class IsodoseLevel : INotifyPropertyChanged
    {
        private double _fraction;
        private string _label;
        private uint _color;
        private byte _alpha = 0x4C;
        private bool _isVisible = true;

        /// <summary>
        /// Fraction of the reference dose (e.g. 1.07 = 107%).
        /// </summary>
        public double Fraction
        {
            get => _fraction;
            set { _fraction = value; OnPropertyChanged(); OnPropertyChanged(nameof(PercentLabel)); }
        }

        /// <summary>
        /// Display label shown in the UI legend.
        /// </summary>
        public string Label
        {
            get => _label;
            set { _label = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Auto-generated percent label from fraction.
        /// </summary>
        public string PercentLabel => $"{Fraction * 100:F0}%";

        /// <summary>
        /// BGRA color as uint (0xAARRGGBB format).
        /// </summary>
        public uint Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); OnPropertyChanged(nameof(MediaColor)); }
        }

        /// <summary>
        /// Alpha value 0–255 used during fill/colorwash rendering. ~30% default.
        /// Line mode always uses full opacity.
        /// </summary>
        public byte Alpha
        {
            get => _alpha;
            set { _alpha = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether this isodose level is drawn. Toggled per-level from the UI.
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public IsodoseLevel(double fraction, string label, uint color)
        {
            _fraction = fraction;
            _label = label;
            _color = color;
        }

        /// <summary>
        /// Default clinical set (4 levels, matching original).
        /// </summary>
        public static IsodoseLevel[] GetDefaults()
        {
            return new[]
            {
                new IsodoseLevel(1.07, "107%", 0xFFFF0000),
                new IsodoseLevel(0.95, "95%",  0xFF00FF00),
                new IsodoseLevel(0.80, "80%",  0xFF00FFFF),
                new IsodoseLevel(0.50, "50%",  0xFF0000FF),
            };
        }

        /// <summary>
        /// Extended Eclipse-style isodose set with 10 clinical levels.
        /// </summary>
        public static IsodoseLevel[] GetEclipseDefaults()
        {
            return new[]
            {
                new IsodoseLevel(1.10, "110%", 0xFFFF0000),   // Red – hot spot
                new IsodoseLevel(1.07, "107%", 0xFFFF4400),   // Orange-red
                new IsodoseLevel(1.05, "105%", 0xFFFF8800),   // Orange
                new IsodoseLevel(1.00, "100%", 0xFFFFFF00),   // Yellow – prescription
                new IsodoseLevel(0.95, "95%",  0xFF00FF00),   // Green – target coverage
                new IsodoseLevel(0.90, "90%",  0xFF00DD88),   // Teal
                new IsodoseLevel(0.80, "80%",  0xFF00FFFF),   // Cyan
                new IsodoseLevel(0.70, "70%",  0xFF0088FF),   // Light blue
                new IsodoseLevel(0.50, "50%",  0xFF0000FF),   // Blue
                new IsodoseLevel(0.30, "30%",  0xFF8800FF),   // Violet
            };
        }

        /// <summary>
        /// Minimal 3-level set for quick evaluation.
        /// </summary>
        public static IsodoseLevel[] GetMinimalSet()
        {
            return new[]
            {
                new IsodoseLevel(1.05, "105%", 0xFFFF0000),
                new IsodoseLevel(0.95, "95%",  0xFF00FF00),
                new IsodoseLevel(0.50, "50%",  0xFF0000FF),
            };
        }

        /// <summary>
        /// Returns System.Windows.Media.Color for WPF UI display.
        /// </summary>
        public System.Windows.Media.Color MediaColor
        {
            get
            {
                byte r = (byte)((Color >> 16) & 0xFF);
                byte g = (byte)((Color >> 8) & 0xFF);
                byte b = (byte)(Color & 0xFF);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
        }

        /// <summary>
        /// Legacy method kept for compatibility.
        /// </summary>
        public System.Windows.Media.Color GetMediaColor() => MediaColor;

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}