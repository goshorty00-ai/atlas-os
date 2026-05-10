using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AtlasAI.ScreenCapture
{
    public class ScreenCaptureEngine
    {
        // Use local Pictures folder (not OneDrive) - C:\Users\{user}\Pictures\Screenshots
        private static readonly string CapturesPath = GetLocalPicturesPath();
        
        private static string GetLocalPicturesPath()
        {
            // Get the actual local Pictures folder, bypassing OneDrive redirection
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localPictures = Path.Combine(userProfile, "Pictures", "Screenshots");
            return localPictures;
        }

        public event Action<CaptureResult>? CaptureCompleted;
        public event Action<string>? CaptureError;

        static ScreenCaptureEngine()
        {
            // Ensure captures directory exists
            if (!Directory.Exists(CapturesPath))
                Directory.CreateDirectory(CapturesPath);
        }

        public async Task<CaptureResult> CaptureScreenAsync(int monitorId = -1)
        {
            try
            {
                var monitors = GetAvailableMonitors();
                if (monitors.Count == 0)
                    throw new InvalidOperationException("No monitors detected");

                // Use primary monitor if no specific monitor requested
                var targetMonitor = monitorId >= 0 && monitorId < monitors.Count 
                    ? monitors[monitorId] 
                    : monitors[0];

                var bitmap = await Task.Run(() => CaptureMonitor(targetMonitor));
                var result = await SaveCaptureAsync(bitmap, targetMonitor);

                CaptureCompleted?.Invoke(result);
                return result;
            }
            catch (Exception ex)
            {
                var error = $"Screen capture failed: {ex.Message}";
                CaptureError?.Invoke(error);
                throw new InvalidOperationException(error, ex);
            }
        }

        public List<Monitor> GetAvailableMonitors()
        {
            var monitors = new List<Monitor>();
            
            for (int i = 0; i < Screen.AllScreens.Length; i++)
            {
                var screen = Screen.AllScreens[i];
                monitors.Add(new Monitor
                {
                    Id = i,
                    Name = screen.DeviceName,
                    Bounds = new Rectangle(
                        screen.Bounds.X, 
                        screen.Bounds.Y, 
                        screen.Bounds.Width, 
                        screen.Bounds.Height),
                    IsPrimary = screen.Primary,
                    WorkingArea = new Rectangle(
                        screen.WorkingArea.X,
                        screen.WorkingArea.Y,
                        screen.WorkingArea.Width,
                        screen.WorkingArea.Height)
                });
            }

            return monitors;
        }

        private Bitmap CaptureMonitor(Monitor monitor)
        {
            var bitmap = new Bitmap(monitor.Bounds.Width, monitor.Bounds.Height, PixelFormat.Format32bppArgb);
            
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    monitor.Bounds.X, 
                    monitor.Bounds.Y, 
                    0, 0, 
                    monitor.Bounds.Size, 
                    CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }

        private async Task<CaptureResult> SaveCaptureAsync(Bitmap bitmap, Monitor monitor)
        {
            // Ensure captures directory exists
            if (!Directory.Exists(CapturesPath))
                Directory.CreateDirectory(CapturesPath);
                
            var timestamp = DateTime.Now;
            var filename = $"capture_{timestamp:yyyyMMdd_HHmmss}_{monitor.Id}.png";
            var filepath = Path.Combine(CapturesPath, filename);

            // Save bitmap to file
            await Task.Run(() => 
            {
                try
                {
                    bitmap.Save(filepath, ImageFormat.Png);
                    System.Diagnostics.Debug.WriteLine($"[ScreenCapture] Saved to: {filepath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScreenCapture] Save error: {ex}");
                    throw;
                }
            });

            // Verify file was saved
            if (!File.Exists(filepath))
            {
                throw new InvalidOperationException($"Screenshot file was not created at: {filepath}");
            }

            // Convert to byte array for processing
            byte[] imageData;
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                imageData = stream.ToArray();
            }

            var metadata = new CaptureMetadata
            {
                Timestamp = timestamp,
                MonitorId = monitor.Id,
                MonitorName = monitor.Name,
                Resolution = new Size(bitmap.Width, bitmap.Height),
                FilePath = filepath,
                FileSize = new FileInfo(filepath).Length
            };

            return new CaptureResult
            {
                Id = Guid.NewGuid().ToString(),
                ImageData = imageData,
                Metadata = metadata,
                Success = true
            };
        }

        public void ShowCapturePreview(CaptureResult capture)
        {
            try
            {
                // Show a brief visual feedback
                var form = new Form
                {
                    Text = "📸 Screenshot Captured!",
                    Size = new Size(300, 100),
                    StartPosition = FormStartPosition.CenterScreen,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    TopMost = true
                };

                var label = new Label
                {
                    Text = $"✅ Captured from {capture.Metadata.MonitorName}\n📁 Saved to: {Path.GetFileName(capture.Metadata.FilePath)}",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter
                };

                form.Controls.Add(label);
                form.Show();

                // Auto-close after 2 seconds
                var timer = new Timer { Interval = 2000 };
                timer.Tick += (s, e) => { form.Close(); timer.Dispose(); };
                timer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preview error: {ex.Message}");
            }
        }
    }

    public class Monitor
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Rectangle Bounds { get; set; }
        public Rectangle WorkingArea { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class CaptureResult
    {
        public string Id { get; set; } = "";
        public byte[] ImageData { get; set; } = Array.Empty<byte>();
        public CaptureMetadata Metadata { get; set; } = new();
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public class CaptureMetadata
    {
        public DateTime Timestamp { get; set; }
        public int MonitorId { get; set; }
        public string MonitorName { get; set; } = "";
        public Size Resolution { get; set; }
        public string FilePath { get; set; } = "";
        public long FileSize { get; set; }
    }
}