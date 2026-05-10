using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace AtlasAI.Agent
{
    /// <summary>
    /// Screen Capture - Take screenshots, capture windows, record screen region.
    /// </summary>
    public static class ScreenCapture
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }
        
        /// <summary>
        /// Handle screenshot commands
        /// </summary>
        public static async Task<string?> TryHandleAsync(string input)
        {
            var lower = input.ToLowerInvariant();
            
            if (!lower.Contains("screenshot") && !lower.Contains("screen capture") && 
                !lower.Contains("capture screen") && !lower.Contains("snip"))
                return null;
            
            // Capture active window
            if (lower.Contains("window") || lower.Contains("active"))
            {
                return await CaptureActiveWindowAsync();
            }
            
            // Capture full screen
            if (lower.Contains("full") || lower.Contains("entire") || lower.Contains("all"))
            {
                return await CaptureFullScreenAsync();
            }
            
            // Default: full screen
            return await CaptureFullScreenAsync();
        }
        
        private static async Task<string> CaptureFullScreenAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Get total screen bounds (all monitors)
                    var bounds = SystemInformation.VirtualScreen;
                    
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using var graphics = Graphics.FromImage(bitmap);
                    
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                    
                    return SaveScreenshot(bitmap, "fullscreen");
                }
                catch (Exception ex)
                {
                    return $"❌ Screenshot failed: {ex.Message}";
                }
            });
        }
        
        private static async Task<string> CaptureActiveWindowAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var hwnd = GetForegroundWindow();
                    if (hwnd == IntPtr.Zero)
                        return "❌ No active window found";
                    
                    if (!GetWindowRect(hwnd, out var rect))
                        return "❌ Couldn't get window bounds";
                    
                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    
                    if (width <= 0 || height <= 0)
                        return "❌ Invalid window size";
                    
                    using var bitmap = new Bitmap(width, height);
                    using var graphics = Graphics.FromImage(bitmap);
                    
                    graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height));
                    
                    return SaveScreenshot(bitmap, "window");
                }
                catch (Exception ex)
                {
                    return $"❌ Screenshot failed: {ex.Message}";
                }
            });
        }
        
        private static string SaveScreenshot(Bitmap bitmap, string prefix)
        {
            var screenshotsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "Atlas Screenshots"
            );
            
            Directory.CreateDirectory(screenshotsDir);
            
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var filename = $"{prefix}_{timestamp}.png";
            var filepath = Path.Combine(screenshotsDir, filename);
            
            bitmap.Save(filepath, ImageFormat.Png);
            
            // Also copy to clipboard
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap.GetHbitmap(),
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()
                );
                System.Windows.Clipboard.SetImage(bitmapSource);
            });
            
            return $"📸 **Screenshot Saved!**\n\n" +
                   $"File: `{filename}`\n" +
                   $"Location: `{screenshotsDir}`\n" +
                   $"Size: {bitmap.Width}x{bitmap.Height}\n\n" +
                   $"✓ Also copied to clipboard!";
        }
    }
}
