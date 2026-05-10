using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AtlasAI.Views.Converters
{
    public sealed class SidebarIconSourceConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var key = (values.Length > 0 ? values[0]?.ToString() : "") ?? "";
                key = key.Trim();
                if (string.IsNullOrWhiteSpace(key)) return null!;

                var isChecked = values.Length > 1 && values[1] is bool b1 && b1;
                var isMouseOver = values.Length > 2 && values[2] is bool b2 && b2;

                var state = isChecked ? "active" : isMouseOver ? "hover" : "default";
                var slug = Slug(key);

                var assetSlug = slug switch
                {
                    "speech" => "custom_greeting",
                    "smarthome" => "smarthome",
                    "smart_home" => "smarthome",
                    "aichef" => "ai_chef",
                    "fileexplorer" => "explorer",
                    "file_explorer" => "explorer",
                    _ => slug
                };

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var root2 = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", ".."));
                var root3 = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", ".."));
                var roots = new[] { baseDir, root2, root3 };
                
                foreach (var root in roots)
                {
                    var dir = Path.Combine(root, "Assets", "Icons");
                    if (!Directory.Exists(dir)) continue;

                    var files = Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileName)
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .Where(f =>
                            f!.StartsWith($"sidebar_{assetSlug}", StringComparison.OrdinalIgnoreCase) ||
                            f.StartsWith($"{assetSlug}_", StringComparison.OrdinalIgnoreCase) ||
                            f.Equals($"{assetSlug}.png", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (files.Length == 0) continue;

                    string? pick = null;
                    bool HasToken(string fileName, string token) =>
                        fileName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (state == "active")
                    {
                        pick = files.FirstOrDefault(f => HasToken(f, "active")) ??
                               files.FirstOrDefault(f => f.Equals($"sidebar_{assetSlug}_active.png", StringComparison.OrdinalIgnoreCase)) ??
                               files.FirstOrDefault(f => f.Equals($"sidebar_{assetSlug}_active 1.png", StringComparison.OrdinalIgnoreCase)) ??
                               files.FirstOrDefault(f => f.Equals($"{assetSlug}_active.png", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (state == "hover")
                    {
                        pick = files.FirstOrDefault(f => HasToken(f, "hover")) ??
                               files.FirstOrDefault(f => HasToken(f, "active"));
                    }
                    else
                    {
                        pick = files.FirstOrDefault(f => HasToken(f, "default")) ??
                               files.FirstOrDefault(f => f.Equals($"sidebar_{assetSlug}_default.png", StringComparison.OrdinalIgnoreCase)) ??
                               files.FirstOrDefault(f => f.Equals($"{assetSlug}_default.png", StringComparison.OrdinalIgnoreCase));
                    }

                    pick ??= files.FirstOrDefault(f => f.Equals($"sidebar_{assetSlug}.png", StringComparison.OrdinalIgnoreCase)) ??
                             files.FirstOrDefault(f => f.Equals($"{assetSlug}.png", StringComparison.OrdinalIgnoreCase)) ??
                             files.FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(pick)) continue;
                    var full = Path.Combine(dir, pick);
                    if (!File.Exists(full)) continue;

                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(full, UriKind.Absolute);
                    img.EndInit();
                    img.Freeze();

                    var processed = TryMakeCornerColorTransparent(img);
                    return processed ?? img;
                }
            }
            catch
            {
            }

            return null!;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static string Slug(string value)
        {
            var s = (value ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s)) return "";
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) continue;
                chars[i] = '_';
            }
            return new string(chars).Trim('_');
        }

        private static BitmapSource? TryMakeCornerColorTransparent(BitmapSource src)
        {
            try
            {
                if (src == null) return null;
                var w = src.PixelWidth;
                var h = src.PixelHeight;
                if (w <= 2 || h <= 2) return null;

                var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                var stride = w * 4;
                var pixels = new byte[stride * h];
                bgra.CopyPixels(pixels, stride, 0);

                (byte b, byte g, byte r, byte a) Pixel(int x, int y)
                {
                    var idx = (y * stride) + (x * 4);
                    return (pixels[idx], pixels[idx + 1], pixels[idx + 2], pixels[idx + 3]);
                }

                (byte b, byte g, byte r, byte a) SampleNearCorner(int cornerX, int cornerY)
                {
                    var max = 10;
                    for (var d = 0; d <= max; d++)
                    {
                        var x = Math.Clamp(cornerX == 0 ? d : (w - 1 - d), 0, w - 1);
                        var y = Math.Clamp(cornerY == 0 ? d : (h - 1 - d), 0, h - 1);
                        var p = Pixel(x, y);
                        if (p.a != 0) return p;
                    }

                    for (var dy = 0; dy <= max; dy++)
                    {
                        for (var dx = 0; dx <= max; dx++)
                        {
                            var x = Math.Clamp(cornerX == 0 ? dx : (w - 1 - dx), 0, w - 1);
                            var y = Math.Clamp(cornerY == 0 ? dy : (h - 1 - dy), 0, h - 1);
                            var p = Pixel(x, y);
                            if (p.a != 0) return p;
                        }
                    }

                    return (0, 0, 0, 0);
                }

                var c1 = SampleNearCorner(0, 0);
                var c2 = SampleNearCorner(w - 1, 0);
                var c3 = SampleNearCorner(0, h - 1);
                var c4 = SampleNearCorner(w - 1, h - 1);
                var corners = new[] { c1, c2, c3, c4 }.Where(c => c.a != 0).ToArray();
                if (corners.Length == 0) return null;

                bool CloseToAnyCorner(byte b, byte g, byte r, byte a, int tol)
                {
                    if (a == 0) return false;
                    foreach (var c in corners)
                    {
                        if (Math.Abs(b - c.b) <= tol &&
                            Math.Abs(g - c.g) <= tol &&
                            Math.Abs(r - c.r) <= tol)
                            return true;
                    }
                    return false;
                }

                var tolEdge = 85;
                var visited = new bool[w * h];
                var q = new Queue<(int x, int y)>();

                void EnqueueIfBg(int x, int y)
                {
                    var vi = y * w + x;
                    if (visited[vi]) return;
                    var idx = (y * stride) + (x * 4);
                    var b = pixels[idx];
                    var g = pixels[idx + 1];
                    var r = pixels[idx + 2];
                    var a = pixels[idx + 3];
                    if (!CloseToAnyCorner(b, g, r, a, tolEdge)) return;
                    visited[vi] = true;
                    q.Enqueue((x, y));
                }

                for (var x = 0; x < w; x++)
                {
                    EnqueueIfBg(x, 0);
                    EnqueueIfBg(x, h - 1);
                }
                for (var y = 0; y < h; y++)
                {
                    EnqueueIfBg(0, y);
                    EnqueueIfBg(w - 1, y);
                }

                while (q.Count > 0)
                {
                    var (x, y) = q.Dequeue();
                    var idx = (y * stride) + (x * 4);
                    pixels[idx + 3] = 0;

                    if (x > 0) EnqueueIfBg(x - 1, y);
                    if (x < w - 1) EnqueueIfBg(x + 1, y);
                    if (y > 0) EnqueueIfBg(x, y - 1);
                    if (y < h - 1) EnqueueIfBg(x, y + 1);
                }

                var wb = new WriteableBitmap(w, h, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                wb.Freeze();
                return wb;
            }
            catch
            {
                return null;
            }
        }

        private static BitmapSource? TryMakeDominantColorTransparent(BitmapSource src)
        {
            try
            {
                if (src == null) return null;
                var w = src.PixelWidth;
                var h = src.PixelHeight;
                if (w <= 2 || h <= 2) return null;

                var bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                var stride = w * 4;
                var pixels = new byte[stride * h];
                bgra.CopyPixels(pixels, stride, 0);

                var counts = new Dictionary<int, int>();
                var total = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var a = pixels[i + 3];
                    if (a == 0) continue;
                    total++;
                    var b = pixels[i];
                    var g = pixels[i + 1];
                    var r = pixels[i + 2];
                    var key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                    if (counts.TryGetValue(key, out var c)) counts[key] = c + 1;
                    else counts[key] = 1;
                }

                if (total < 64) return null;

                var bestKey = 0;
                var bestCount = 0;
                foreach (var kv in counts)
                {
                    if (kv.Value > bestCount)
                    {
                        bestCount = kv.Value;
                        bestKey = kv.Key;
                    }
                }

                var ratio = (double)bestCount / total;
                if (ratio < 0.35) return null;

                long sumR = 0, sumG = 0, sumB = 0;
                var sumN = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var a = pixels[i + 3];
                    if (a == 0) continue;
                    var b = pixels[i];
                    var g = pixels[i + 1];
                    var r = pixels[i + 2];
                    var key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                    if (key != bestKey) continue;
                    sumR += r;
                    sumG += g;
                    sumB += b;
                    sumN++;
                }

                if (sumN <= 0) return null;
                var bgR = (byte)Math.Clamp(sumR / sumN, 0, 255);
                var bgG = (byte)Math.Clamp(sumG / sumN, 0, 255);
                var bgB = (byte)Math.Clamp(sumB / sumN, 0, 255);

                var tol = 70;
                var changed = 0;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var a = pixels[i + 3];
                    if (a == 0) continue;
                    var b = pixels[i];
                    var g = pixels[i + 1];
                    var r = pixels[i + 2];
                    if (Math.Abs(r - bgR) <= tol &&
                        Math.Abs(g - bgG) <= tol &&
                        Math.Abs(b - bgB) <= tol)
                    {
                        pixels[i + 3] = 0;
                        changed++;
                    }
                }

                if (changed < 16) return null;

                var wb = new WriteableBitmap(w, h, bgra.DpiX, bgra.DpiY, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                wb.Freeze();
                return wb;
            }
            catch
            {
                return null;
            }
        }
    }
}
