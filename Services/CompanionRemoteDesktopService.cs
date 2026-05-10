using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace AtlasAI.Services
{
    internal sealed class CompanionRemoteDesktopService
    {
        public const string PreviewFramePath = "/api/remote/desktop/frame";
        public const string LiveStreamPath = "/ws/remote/desktop/live";

        private const uint MouseEventMove = 0x0001;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventRightDown = 0x0008;
        private const uint MouseEventRightUp = 0x0010;
        private const uint MouseEventWheel = 0x0800;
        private const uint KeyEventKeyUp = 0x0002;
        private const byte VkShift = 0x10;
        private const byte VkControl = 0x11;
        private const byte VkAlt = 0x12;
        private const byte VkLeftWindows = 0x5B;
        private const byte VkVolumeMute = 0xAD;
        private const byte VkVolumeDown = 0xAE;
        private const byte VkVolumeUp = 0xAF;
        private const byte VkMediaPlayPause = 0xB3;
        private readonly object _inputLock = new();
        private bool _leftPointerHeld;

        public CompanionRemoteDesktopState GetState(string previewPath, string liveStreamPath)
        {
            var desktop = GetDesktopContext();

            return new CompanionRemoteDesktopState(
                IsAvailable: desktop.Width > 0 && desktop.Height > 0,
                SessionName: $"Atlas Desktop on {Environment.MachineName}",
                ActiveApp: desktop.ActiveApp,
                WindowTitle: desktop.WindowTitle,
                Width: desktop.Width,
                Height: desktop.Height,
                PreviewPath: previewPath,
                LiveStreamPath: liveStreamPath,
                SupportsPointer: true,
                SupportsKeyboard: true,
                SupportsClipboard: false,
                SupportsSystemShortcuts: true);
        }

            public CompanionRemoteDesktopFrame CaptureFrame(int maxWidth = 1440, int maxHeight = 900, long quality = 60L)
        {
            var desktop = GetDesktopContext();
            if (desktop.Width <= 0 || desktop.Height <= 0)
            {
                throw new InvalidOperationException("No desktop surface is available for capture.");
            }

            using var bitmap = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(desktop.Left, desktop.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            }

            using var preview = Downscale(bitmap, maxWidth: maxWidth, maxHeight: maxHeight);
            var bytes = EncodeJpeg(preview, quality: quality);

            return new CompanionRemoteDesktopFrame(bytes, "image/jpeg", preview.Width, preview.Height, DateTime.UtcNow);
        }

        public CompanionRemoteActionResult ExecuteAction(RemoteDesktopActionRequest request)
        {
            if (request == null)
            {
                throw new InvalidOperationException("A remote action payload is required.");
            }

            var action = (request.Action ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(action))
            {
                throw new InvalidOperationException("A non-empty 'action' field is required.");
            }

            return NormalizeToken(action) switch
            {
                "pointer" => ExecutePointerAction(request),
                "scroll" => ExecuteScrollAction(request),
                "shortcut" => ExecuteShortcutAction(request),
                "key" => ExecuteKeyAction(request),
                "text" => ExecuteTextAction(request),
                "mute" => ExecuteVirtualKeyAction(VkVolumeMute, "Mute toggled"),
                "volumeup" => ExecuteVirtualKeyAction(VkVolumeUp, "Volume increased"),
                "volumedown" => ExecuteVirtualKeyAction(VkVolumeDown, "Volume decreased"),
                "playpause" => ExecuteVirtualKeyAction(VkMediaPlayPause, "Play/pause toggled"),
                _ => throw new InvalidOperationException($"Unsupported remote action '{action}'."),
            };
        }

        private CompanionRemoteActionResult ExecutePointerAction(RemoteDesktopActionRequest request)
        {
            var desktop = GetDesktopContext();
            var x = ToScreenCoordinate(request.X, desktop.Left, desktop.Width);
            var y = ToScreenCoordinate(request.Y, desktop.Top, desktop.Height);
            var button = NormalizeToken(request.Button ?? "left");
            var gesture = NormalizeToken(request.Gesture ?? "tap");

            if (gesture == "move")
            {
                return ExecutePointerMoveAction(request, desktop);
            }

            if (gesture == "dragstart")
            {
                return ExecutePointerDragStartAction(x, y);
            }

            if (gesture == "dragmove")
            {
                return ExecutePointerDragMoveAction(request, desktop, x, y);
            }

            if (gesture == "dragend")
            {
                return ExecutePointerDragEndAction(x, y);
            }

            ReleaseHeldPointer();

            SetCursorPos(x, y);

            if (button == "right")
            {
                Click(MouseEventRightDown, MouseEventRightUp, x, y, clickCount: 1);
                return new CompanionRemoteActionResult(true, $"Right click at ({x}, {y})");
            }

            if (gesture == "doubletap")
            {
                Click(MouseEventLeftDown, MouseEventLeftUp, x, y, clickCount: 2);
                return new CompanionRemoteActionResult(true, $"Double click at ({x}, {y})");
            }

            if (gesture == "longpress")
            {
                mouse_event(MouseEventLeftDown, x, y, 0, 0);
                System.Threading.Thread.Sleep(180);
                mouse_event(MouseEventLeftUp, x, y, 0, 0);
                return new CompanionRemoteActionResult(true, $"Long press at ({x}, {y})");
            }

            Click(MouseEventLeftDown, MouseEventLeftUp, x, y, clickCount: 1);
            return new CompanionRemoteActionResult(true, $"Click at ({x}, {y})");
        }

        private CompanionRemoteActionResult ExecuteScrollAction(RemoteDesktopActionRequest request)
        {
            ReleaseHeldPointer();
            var delta = request.DeltaY;
            if (Math.Abs(delta) < 1)
            {
                delta = 120;
            }

            var wheelDelta = (int)Math.Round(delta);
            if (Math.Abs(wheelDelta) < 120)
            {
                wheelDelta = wheelDelta < 0 ? -120 : 120;
            }

            mouse_event(MouseEventWheel, 0, 0, wheelDelta, 0);
            return new CompanionRemoteActionResult(true, wheelDelta < 0 ? "Scrolled up" : "Scrolled down");
        }

        private CompanionRemoteActionResult ExecuteShortcutAction(RemoteDesktopActionRequest request)
        {
            ReleaseHeldPointer();
            var shortcut = (request.Shortcut ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                throw new InvalidOperationException("A non-empty 'shortcut' field is required.");
            }

            PressKeyChord(shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return new CompanionRemoteActionResult(true, $"Shortcut sent: {shortcut}");
        }

        private CompanionRemoteActionResult ExecuteKeyAction(RemoteDesktopActionRequest request)
        {
            ReleaseHeldPointer();
            var key = (request.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("A non-empty 'key' field is required.");
            }

            if (key.Length == 1)
            {
                TypeCharacter(key[0]);
            }
            else
            {
                TapVirtualKey(MapKeyToken(key));
            }

            return new CompanionRemoteActionResult(true, $"Key sent: {key}");
        }

        private CompanionRemoteActionResult ExecuteTextAction(RemoteDesktopActionRequest request)
        {
            ReleaseHeldPointer();
            var text = request.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("A non-empty 'text' field is required.");
            }

            foreach (var character in text)
            {
                TypeCharacter(character);
                System.Threading.Thread.Sleep(8);
            }

            return new CompanionRemoteActionResult(true, "Text sent");
        }

        private CompanionRemoteActionResult ExecuteVirtualKeyAction(byte virtualKey, string message)
        {
            ReleaseHeldPointer();
            TapVirtualKey(virtualKey);
            return new CompanionRemoteActionResult(true, message);
        }

        private CompanionRemoteActionResult ExecutePointerMoveAction(RemoteDesktopActionRequest request, CompanionDesktopContext desktop)
        {
            var deltaX = ToRelativePixels(request.DeltaX, desktop.Width);
            var deltaY = ToRelativePixels(request.DeltaY, desktop.Height);
            if (deltaX == 0 && deltaY == 0)
            {
                return new CompanionRemoteActionResult(true, "Pointer held");
            }

            mouse_event(MouseEventMove, deltaX, deltaY, 0, 0);
            return new CompanionRemoteActionResult(true, $"Pointer moved by ({deltaX}, {deltaY})");
        }

        private CompanionRemoteActionResult ExecutePointerDragStartAction(int x, int y)
        {
            lock (_inputLock)
            {
                SetCursorPos(x, y);
                if (!_leftPointerHeld)
                {
                    mouse_event(MouseEventLeftDown, x, y, 0, 0);
                    _leftPointerHeld = true;
                }
            }

            return new CompanionRemoteActionResult(true, $"Drag started at ({x}, {y})");
        }

        private CompanionRemoteActionResult ExecutePointerDragMoveAction(RemoteDesktopActionRequest request, CompanionDesktopContext desktop, int x, int y)
        {
            lock (_inputLock)
            {
                if (!_leftPointerHeld)
                {
                    SetCursorPos(x, y);
                    mouse_event(MouseEventLeftDown, x, y, 0, 0);
                    _leftPointerHeld = true;
                }

                var deltaX = ToRelativePixels(request.DeltaX, desktop.Width);
                var deltaY = ToRelativePixels(request.DeltaY, desktop.Height);
                if (deltaX != 0 || deltaY != 0)
                {
                    mouse_event(MouseEventMove, deltaX, deltaY, 0, 0);
                }
                else
                {
                    SetCursorPos(x, y);
                }
            }

            return new CompanionRemoteActionResult(true, $"Drag moved to ({x}, {y})");
        }

        private CompanionRemoteActionResult ExecutePointerDragEndAction(int x, int y)
        {
            lock (_inputLock)
            {
                SetCursorPos(x, y);
                if (_leftPointerHeld)
                {
                    mouse_event(MouseEventLeftUp, x, y, 0, 0);
                    _leftPointerHeld = false;
                }
            }

            return new CompanionRemoteActionResult(true, $"Drag ended at ({x}, {y})");
        }

        private void ReleaseHeldPointer()
        {
            lock (_inputLock)
            {
                if (!_leftPointerHeld)
                {
                    return;
                }

                mouse_event(MouseEventLeftUp, 0, 0, 0, 0);
                _leftPointerHeld = false;
            }
        }

        private static void Click(uint downEvent, uint upEvent, int x, int y, int clickCount)
        {
            for (var i = 0; i < clickCount; i++)
            {
                mouse_event(downEvent, x, y, 0, 0);
                mouse_event(upEvent, x, y, 0, 0);
                if (i + 1 < clickCount)
                {
                    System.Threading.Thread.Sleep(80);
                }
            }
        }

        private static void PressKeyChord(string[] tokens)
        {
            if (tokens.Length == 0)
            {
                return;
            }

            var modifiers = tokens.Take(tokens.Length - 1).Select(MapModifierToken).ToArray();
            var primaryKey = MapKeyToken(tokens[^1]);

            foreach (var modifier in modifiers)
            {
                keybd_event(modifier, 0, 0, UIntPtr.Zero);
            }

            TapVirtualKey(primaryKey);

            for (var i = modifiers.Length - 1; i >= 0; i--)
            {
                keybd_event(modifiers[i], 0, KeyEventKeyUp, UIntPtr.Zero);
            }
        }

        private static void TapVirtualKey(byte virtualKey)
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
            keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        private static void TypeCharacter(char character)
        {
            var vk = VkKeyScan(character);
            if (vk == -1)
            {
                return;
            }

            var virtualKey = (byte)(vk & 0xFF);
            var modifiers = (vk >> 8) & 0xFF;

            if ((modifiers & 1) != 0)
            {
                keybd_event(VkShift, 0, 0, UIntPtr.Zero);
            }

            if ((modifiers & 2) != 0)
            {
                keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            }

            if ((modifiers & 4) != 0)
            {
                keybd_event(VkAlt, 0, 0, UIntPtr.Zero);
            }

            TapVirtualKey(virtualKey);

            if ((modifiers & 4) != 0)
            {
                keybd_event(VkAlt, 0, KeyEventKeyUp, UIntPtr.Zero);
            }

            if ((modifiers & 2) != 0)
            {
                keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
            }

            if ((modifiers & 1) != 0)
            {
                keybd_event(VkShift, 0, KeyEventKeyUp, UIntPtr.Zero);
            }
        }

        private static byte MapModifierToken(string token)
        {
            return NormalizeToken(token) switch
            {
                "ctrl" or "control" => VkControl,
                "shift" => VkShift,
                "alt" or "menu" => VkAlt,
                "meta" or "win" or "windows" or "super" => VkLeftWindows,
                _ => throw new InvalidOperationException($"Unsupported modifier '{token}'."),
            };
        }

        private static byte MapKeyToken(string token)
        {
            var normalized = NormalizeToken(token);
            return normalized switch
            {
                "tab" => 0x09,
                "enter" or "return" => 0x0D,
                "escape" or "esc" => 0x1B,
                "backspace" => 0x08,
                "space" => 0x20,
                "left" => 0x25,
                "up" => 0x26,
                "right" => 0x27,
                "down" => 0x28,
                "delete" or "del" => 0x2E,
                "home" => 0x24,
                "end" => 0x23,
                _ when normalized.Length == 1 => (byte)char.ToUpperInvariant(normalized[0]),
                _ => throw new InvalidOperationException($"Unsupported key '{token}'."),
            };
        }

        private static int ToScreenCoordinate(double normalized, int origin, int size)
        {
            if (size <= 1)
            {
                return origin;
            }

            var clamped = Math.Max(0d, Math.Min(1d, normalized));
            return origin + (int)Math.Round(clamped * (size - 1));
        }

        private static int ToRelativePixels(double normalizedDelta, int size)
        {
            if (size <= 0)
            {
                return 0;
            }

            var clamped = Math.Max(-1d, Math.Min(1d, normalizedDelta));
            var pixels = (int)Math.Round(clamped * size);
            if (pixels == 0 && Math.Abs(clamped) >= 0.0025d)
            {
                pixels = clamped < 0 ? -1 : 1;
            }

            return pixels;
        }

        private static Bitmap Downscale(Bitmap source, int maxWidth, int maxHeight)
        {
            if (source.Width <= maxWidth && source.Height <= maxHeight)
            {
                return new Bitmap(source);
            }

            var ratio = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * ratio));
            var height = Math.Max(1, (int)Math.Round(source.Height * ratio));
            var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            using var graphics = Graphics.FromImage(resized);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(source, 0, 0, width, height);
            return resized;
        }

        private static byte[] EncodeJpeg(Image image, long quality)
        {
            using var stream = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, new[] { quality });
            image.Save(stream, encoder, parameters);
            return stream.ToArray();
        }

        private static CompanionDesktopContext GetDesktopContext()
        {
            var virtualScreen = Forms.SystemInformation.VirtualScreen;
            var activeApp = "Atlas Desktop";
            var windowTitle = "Desktop session ready";

            try
            {
                var handle = GetForegroundWindow();
                if (handle != IntPtr.Zero)
                {
                    var titleBuilder = new StringBuilder(512);
                    _ = GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
                    if (!string.IsNullOrWhiteSpace(titleBuilder.ToString()))
                    {
                        windowTitle = titleBuilder.ToString().Trim();
                    }

                    GetWindowThreadProcessId(handle, out var processId);
                    if (processId > 0)
                    {
                        using var process = Process.GetProcessById((int)processId);
                        if (!string.IsNullOrWhiteSpace(process.ProcessName))
                        {
                            activeApp = process.ProcessName;
                        }
                    }
                }
            }
            catch
            {
            }

            return new CompanionDesktopContext(
                virtualScreen.Left,
                virtualScreen.Top,
                virtualScreen.Width,
                virtualScreen.Height,
                activeApp,
                windowTitle);
        }

        private static string NormalizeToken(string value)
        {
            return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private sealed record CompanionDesktopContext(int Left, int Top, int Width, int Height, string ActiveApp, string WindowTitle);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, int data, int extraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char character);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    }

    internal sealed record CompanionRemoteDesktopState(
        bool IsAvailable,
        string SessionName,
        string ActiveApp,
        string WindowTitle,
        int Width,
        int Height,
        string PreviewPath,
        string LiveStreamPath,
        bool SupportsPointer,
        bool SupportsKeyboard,
        bool SupportsClipboard,
        bool SupportsSystemShortcuts);

    internal sealed record CompanionRemoteDesktopFrame(
        byte[] Bytes,
        string ContentType,
        int Width,
        int Height,
        DateTime CapturedAtUtc);

    internal sealed record CompanionRemoteActionResult(bool Ok, string Message);

    internal sealed class RemoteDesktopActionRequest
    {
        public string? Action { get; init; }

        public string? Button { get; init; }

        public string? Gesture { get; init; }

        public double X { get; init; }

        public double Y { get; init; }

        public double DeltaX { get; init; }

        public double DeltaY { get; init; }

        public string? Shortcut { get; init; }

        public string? Key { get; init; }

        public string? Text { get; init; }
    }
}