using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using AtlasAI.Core;
using AtlasAI.FloatingHud;
using AtlasAI.UI;
using AtlasAI.Voice;
using Application = System.Windows.Application;

namespace AtlasAI
{
 public partial class MainWindow : Window
 {
 private bool isDragging = false;
 private System.Windows.Point dragStartScreen;
 private double dragStartLeft;
 private double dragStartTop;
 private ChatWindow? chatWindow;
 private NotifyIcon? trayIcon;
 private readonly PresenceController _presence;
 private readonly VoiceSystemOrchestrator _voiceOrchestrator;
 private int _currentAnimationStyle = 0;

 private const int HOTKEY_ID = 9000;
 private const uint MOD_CTRL = 0x0002;
 private const uint MOD_ALT = 0x0001;
 private const uint VK_A = 0x41;

 [DllImport("user32.dll", SetLastError = true)]
 private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

 [DllImport("user32.dll", SetLastError = true)]
 private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

 private HwndSource? _source;

 public MainWindow()
 {
 InitializeComponent();
 this.Closing += MainWindow_Closing;
 // ... (rest of constructor)
 }

 protected override void OnSourceInitialized(EventArgs e)
 {
 base.OnSourceInitialized(e);
 var helper = new WindowInteropHelper(this);
 _source = HwndSource.FromHwnd(helper.Handle);
 _source?.AddHook(HwndHook);
 RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CTRL | MOD_ALT, VK_A);
 }

 private void MainWindow_Closing(object? sender, CancelEventArgs e)
 {
 // Clean up resources
 UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_ID);
 _voiceOrchestrator?.Dispose();
 trayIcon?.Dispose();
 trayIcon = null;

 // Ensure the application shuts down completely
 Application.Current.Shutdown();
 }

 private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
 {
 const int WM_HOTKEY = 0x0312;
 if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
 {
 ToggleChatWindow();
 handled = true;
 }
 return IntPtr.Zero;
 }

 private void ToggleChatWindow()
 {
 if (chatWindow == null || !chatWindow.IsVisible)
 {
 chatWindow = new ChatWindow(); // Simplified for example
 chatWindow.Show();
 }
 else
 { 
 chatWindow.Close();
 chatWindow = null;
 }
 }

 // ... (rest of the file remains the same)
 }
}
