using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Color Tools - Color conversion, picking, and palette generation.
    /// </summary>
    public static class ColorTools
    {
        /// <summary>
        /// Handle color commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            // Convert color
            if (lower.Contains("convert") && (lower.Contains("color") || lower.Contains("colour")))
            {
                return ConvertColorFromClipboard();
            }
            
            // Parse hex color
            var hexMatch = Regex.Match(input, @"#?([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})\b");
            if (hexMatch.Success && (lower.Contains("color") || lower.Contains("rgb") || lower.Contains("hsl")))
            {
                var hex = hexMatch.Groups[1].Value;
                if (hex.Length == 3)
                    hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                return GetColorInfo("#" + hex);
            }
            
            // Parse RGB
            var rgbMatch = Regex.Match(input, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
            if (rgbMatch.Success)
            {
                var r = byte.Parse(rgbMatch.Groups[1].Value);
                var g = byte.Parse(rgbMatch.Groups[2].Value);
                var b = byte.Parse(rgbMatch.Groups[3].Value);
                return GetColorInfo(r, g, b);
            }
            
            // Random color
            if (lower.Contains("random color") || lower.Contains("random colour"))
            {
                var random = new Random();
                var r = (byte)random.Next(256);
                var g = (byte)random.Next(256);
                var b = (byte)random.Next(256);
                var hex = $"#{r:X2}{g:X2}{b:X2}";
                
                Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(hex));
                
                return $"🎨 **Random Color:**\n" +
                       $"Hex: `{hex}`\n" +
                       $"RGB: `rgb({r}, {g}, {b})`\n" +
                       $"✓ Copied hex to clipboard!";
            }
            
            // Color palette
            if (lower.Contains("palette") || lower.Contains("color scheme"))
            {
                return GeneratePalette();
            }
            
            // Complementary color
            if (lower.Contains("complementary") || lower.Contains("opposite color"))
            {
                return GetComplementaryFromClipboard();
            }
            
            // Lighten/darken
            if (lower.Contains("lighten") || lower.Contains("darken"))
            {
                var amount = 20;
                var match = Regex.Match(lower, @"(\d+)\s*%?");
                if (match.Success)
                    amount = int.Parse(match.Groups[1].Value);
                
                return AdjustBrightnessFromClipboard(lower.Contains("lighten") ? amount : -amount);
            }
            
            return null;
        }
        
        private static string GetColorInfo(string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return GetColorInfo(color.R, color.G, color.B);
            }
            catch
            {
                return $"❌ Invalid color: {hex}";
            }
        }
        
        private static string GetColorInfo(byte r, byte g, byte b)
        {
            var hex = $"#{r:X2}{g:X2}{b:X2}";
            var (h, s, l) = RgbToHsl(r, g, b);
            var name = GetColorName(r, g, b);
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(hex));
            
            return $"🎨 **Color Info:**\n\n" +
                   $"Name: {name}\n" +
                   $"Hex: `{hex}`\n" +
                   $"RGB: `rgb({r}, {g}, {b})`\n" +
                   $"HSL: `hsl({h:F0}, {s:F0}%, {l:F0}%)`\n" +
                   $"✓ Copied hex to clipboard!";
        }
        
        private static string ConvertColorFromClipboard()
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText()?.Trim();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy a color value to clipboard first!";
            
            // Try hex
            var hexMatch = Regex.Match(text, @"#?([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})\b");
            if (hexMatch.Success)
            {
                var hex = hexMatch.Groups[1].Value;
                if (hex.Length == 3)
                    hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
                return GetColorInfo("#" + hex);
            }
            
            // Try RGB
            var rgbMatch = Regex.Match(text, @"(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
            if (rgbMatch.Success)
            {
                var r = byte.Parse(rgbMatch.Groups[1].Value);
                var g = byte.Parse(rgbMatch.Groups[2].Value);
                var b = byte.Parse(rgbMatch.Groups[3].Value);
                return GetColorInfo(r, g, b);
            }
            
            return $"❌ Couldn't parse color: {text}";
        }
        
        private static string GeneratePalette()
        {
            var random = new Random();
            var baseHue = random.Next(360);
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("🎨 **Color Palette:**\n");
            
            // Analogous colors
            var colors = new[]
            {
                ("Primary", baseHue, 70, 50),
                ("Secondary", (baseHue + 30) % 360, 60, 55),
                ("Accent", (baseHue + 180) % 360, 80, 45),
                ("Light", baseHue, 30, 90),
                ("Dark", baseHue, 50, 20)
            };
            
            foreach (var (name, h, s, l) in colors)
            {
                var (r, g, b) = HslToRgb(h, s, l);
                var hex = $"#{r:X2}{g:X2}{b:X2}";
                sb.AppendLine($"**{name}:** `{hex}`");
            }
            
            return sb.ToString();
        }
        
        private static string GetComplementaryFromClipboard()
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText()?.Trim();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy a color to clipboard first!";
            
            var hexMatch = Regex.Match(text, @"#?([0-9A-Fa-f]{6})");
            if (!hexMatch.Success)
                return "❌ Invalid hex color in clipboard";
            
            var hex = hexMatch.Groups[1].Value;
            var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            
            // Complementary = invert
            var compR = (byte)(255 - r);
            var compG = (byte)(255 - g);
            var compB = (byte)(255 - b);
            var compHex = $"#{compR:X2}{compG:X2}{compB:X2}";
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(compHex));
            
            return $"🎨 **Complementary Color:**\n" +
                   $"Original: `#{hex}`\n" +
                   $"Complementary: `{compHex}`\n" +
                   $"✓ Copied to clipboard!";
        }
        
        private static string AdjustBrightnessFromClipboard(int amount)
        {
            string? text = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText()?.Trim();
            });
            
            if (string.IsNullOrEmpty(text))
                return "📋 Copy a color to clipboard first!";
            
            var hexMatch = Regex.Match(text, @"#?([0-9A-Fa-f]{6})");
            if (!hexMatch.Success)
                return "❌ Invalid hex color in clipboard";
            
            var hex = hexMatch.Groups[1].Value;
            var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
            
            var (h, s, l) = RgbToHsl(r, g, b);
            l = Math.Clamp(l + amount, 0, 100);
            var (newR, newG, newB) = HslToRgb((int)h, (int)s, (int)l);
            var newHex = $"#{newR:X2}{newG:X2}{newB:X2}";
            
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(newHex));
            
            var action = amount > 0 ? "Lightened" : "Darkened";
            return $"🎨 **{action} Color:**\n" +
                   $"Original: `#{hex}`\n" +
                   $"Result: `{newHex}`\n" +
                   $"✓ Copied to clipboard!";
        }
        
        private static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            var max = Math.Max(rd, Math.Max(gd, bd));
            var min = Math.Min(rd, Math.Min(gd, bd));
            double h = 0, s, l = (max + min) / 2;
            
            if (max == min)
            {
                h = s = 0;
            }
            else
            {
                var d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                
                if (max == rd) h = ((gd - bd) / d + (gd < bd ? 6 : 0)) / 6;
                else if (max == gd) h = ((bd - rd) / d + 2) / 6;
                else h = ((rd - gd) / d + 4) / 6;
            }
            
            return (h * 360, s * 100, l * 100);
        }
        
        private static (byte R, byte G, byte B) HslToRgb(int h, int s, int l)
        {
            double hd = h / 360.0, sd = s / 100.0, ld = l / 100.0;
            
            if (sd == 0)
            {
                var gray = (byte)(ld * 255);
                return (gray, gray, gray);
            }
            
            var q = ld < 0.5 ? ld * (1 + sd) : ld + sd - ld * sd;
            var p = 2 * ld - q;
            
            double HueToRgb(double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 1.0 / 2) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            }
            
            return (
                (byte)(HueToRgb(hd + 1.0 / 3) * 255),
                (byte)(HueToRgb(hd) * 255),
                (byte)(HueToRgb(hd - 1.0 / 3) * 255)
            );
        }
        
        private static string GetColorName(byte r, byte g, byte b)
        {
            // Simple color naming
            var (h, s, l) = RgbToHsl(r, g, b);
            
            if (s < 10) return l < 20 ? "Black" : l > 80 ? "White" : "Gray";
            
            var hue = h switch
            {
                < 15 => "Red",
                < 45 => "Orange",
                < 75 => "Yellow",
                < 150 => "Green",
                < 210 => "Cyan",
                < 270 => "Blue",
                < 330 => "Purple",
                _ => "Red"
            };
            
            var lightness = l switch
            {
                < 30 => "Dark ",
                > 70 => "Light ",
                _ => ""
            };
            
            return lightness + hue;
        }
    }
}
