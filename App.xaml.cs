using AtlasAI.Core;
using System;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using System.Windows.Interop;
using AtlasAI.Services;
using AtlasAI.UI;
using AtlasAI.Tools;
using AtlasAI.Personality;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using AtlasAI.Views.ViewModels;

namespace AtlasAI {
 public partial class App : Application
 {
 private static ServiceProvider? _serviceProvider;

 private static IAtlasDialogService? _dialogService;

 public static string PendingOpenUrl { get; private set; } = "";

 public static IServiceProvider Services
 {
 get
 {
 if (_serviceProvider == null)
 _serviceProvider = BuildServiceProvider();
 return _serviceProvider;
 }
 }

 public static T? GetService<T>() where T : class
 {
 try 
 { 
 return Services.GetService(typeof(T)) as T; 
 } 
 catch (Exception ex)
 {
 AppLogger.Log($"GetService failed for type {typeof(T).Name}: {ex.Message}");
 return null; 
 }
 }

 private static ServiceProvider BuildServiceProvider()
 {
 var services = new ServiceCollection();
 try 
 { 
 ConfigureServices(services);
 } 
 catch (Exception ex)
 {
 AppLogger.Log($"FATAL: Service configuration failed. {ex.ToString()}");
 throw; // Re-throw critical exception
 }
 return services.BuildServiceProvider();
 }

 private static void ConfigureServices(IServiceCollection services)
 {
 services.AddSingleton<IContextService, ContextService>();
 services.AddSingleton<IMemoryService, MemoryService>();
 services.AddSingleton<ILogWatcherService, LogWatcherService>();
 }

 public static IAtlasDialogService DialogService
 {
 get
 {
 if (_dialogService == null)
 {
 _dialogService = new AtlasDialogService();
 }
 return _dialogService;
 }
 }

 public static string ConsumePendingOpenUrl()
 {
 var value = PendingOpenUrl;
 PendingOpenUrl = "";
 return value ?? "";
 }

 public static string GetLottiePath(string fileName)
 {
 var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
 // Prefer runtime animations folder (bin\x64\Animations) when present.
 var runtimePath = Path.Combine(baseDir, "Animations", fileName);
 if (File.Exists(runtimePath))
 {
 Debug.WriteLine($"Lottie exists: {runtimePath} => True");
 return runtimePath;
 }

 var path = Path.Combine(baseDir, "Assets", "Animations", "Lottie", fileName);
 if (File.Exists(path))
 {
 Debug.WriteLine($"Lottie exists: {path} => True");
 return path;
 }

 try
 {
 var cwd = Directory.GetCurrentDirectory();
 var altRuntime = Path.Combine(cwd, "Animations", fileName);
 if (File.Exists(altRuntime))
 return altRuntime;

 var alt = Path.Combine(cwd, "Assets", "Animations", "Lottie", fileName);
 Debug.WriteLine($"Lottie exists: {path} => False; alt: {alt} => {File.Exists(alt)}");
 if (File.Exists(alt))
 return alt;
 }
 catch (Exception ex)
 {
 AppLogger.LogError($"Lottie path check failed for {fileName}", ex);
 }

 return path;
 }

 private static BitmapImage? _appIcon;
 private static Mutex? _singleInstanceMutex;
 private static bool _mutexOwned = false;
 private readonly AtlasAI.SmartHome.RingDoorbellMonitorService _ringDoorbellMonitorService = new();
 private static readonly string MutexName = "AtlasAI_SingleInstanceMutex";

 private const string ExternalPipeName = "AtlasAI_ExternalOpenPipe_v1";
 private static CancellationTokenSource? _externalPipeCts;
 private static readonly ConcurrentQueue<string[]> _pendingExternalArgs = new();

 protected override void OnStartup(StartupEventArgs e)
 {
 AppLogger.LogInfo($"=== App.OnStartup ===");
 try
 {
	 if (AtlasAI.Core.FactoryReset.TryRunPendingReset(out var resetStatus) && !string.IsNullOrWhiteSpace(resetStatus))
	 {
		 try
		 {
			 MessageBox.Show(
				 resetStatus,
				 "AtlasAI Factory Reset",
				 MessageBoxButton.OK,
				 resetStatus.StartsWith("Factory reset failed", StringComparison.OrdinalIgnoreCase)
					 ? MessageBoxImage.Error
					 : MessageBoxImage.Information);
		 }
		 catch { }
	 }
 }
 catch { }
 try
 {
 	// Ensure the transient voice UI log file exists even if ChatWindow is never opened.
 	// This helps diagnose "popup flashed then vanished" by letting the UI layer append events later.
 	var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Boot] App.OnStartup{Environment.NewLine}";
	bool TryWrite(string baseDir)
	{
		try
		{
			Directory.CreateDirectory(baseDir);
			File.AppendAllText(Path.Combine(baseDir, "latest_voice_ui.log"), line);
			return true;
		}
		catch
		{
			return false;
		}
	}

	var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI");
	if (!TryWrite(roaming))
	{
		var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasAI");
		if (!TryWrite(local))
		{
			var temp = Path.Combine(Path.GetTempPath(), "AtlasAI");
			TryWrite(temp);
		}
	}
 }
 catch { }
 try
 {
 	var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
 	AppLogger.LogInfo($"Process: {Process.GetCurrentProcess().ProcessName} (v{ver})");
 }
 catch { }

 var initialArgs = (e?.Args ?? Array.Empty<string>()).Select(a => (a ?? "").Trim()).Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
 var validateXaml = initialArgs.Any(a => string.Equals(a, "--validate-xaml", StringComparison.OrdinalIgnoreCase) ||
									   string.Equals(a, "/validate-xaml", StringComparison.OrdinalIgnoreCase) ||
									   string.Equals(a, "--xaml-validate", StringComparison.OrdinalIgnoreCase) ||
									   string.Equals(a, "/xaml-validate", StringComparison.OrdinalIgnoreCase));
 try { AppLogger.LogInfo($"Args: {(initialArgs.Length == 0 ? "<none>" : string.Join(" ", initialArgs))}"); } catch { }

 // Explicit user-invoked shell registration.
 if (initialArgs.Any(a => string.Equals(a, "--register-shell", StringComparison.OrdinalIgnoreCase) ||
						  string.Equals(a, "/register-shell", StringComparison.OrdinalIgnoreCase)))
 {
	 try
	 {
		 var exePath = GetExecutablePath();
		 WindowsShellIntegration.Register(exePath);
		 MessageBox.Show(
			 "Registered AtlasAI protocol (atlasai://) and media file associations for the current user.\n\nWindows may still require you to confirm/set defaults in Default Apps.",
			 "AtlasAI Shell Registration",
			 MessageBoxButton.OK,
			 MessageBoxImage.Information);
	 }
	 catch (Exception ex)
	 {
		 MessageBox.Show($"Shell registration failed: {ex.Message}", "AtlasAI Shell Registration", MessageBoxButton.OK, MessageBoxImage.Error);
	 }
	 Shutdown();
	 return;
 }

 CheckForSingleInstance(initialArgs);
 if (!_mutexOwned) return;

 base.OnStartup(e);
 InitializeServices();

 // Ensure the wake word event hub is initialized for the app lifetime.
 // Without this, WakeWordService events never get broadcast to the orchestrator/UI.
 try
 {
 	WakeWordCoordinator.Instance.Initialize();
 	AppLogger.LogInfo("WakeWordCoordinator initialized");
 }
 catch (Exception ex)
 {
 	AppLogger.LogWarning($"WakeWordCoordinator initialization failed: {ex.Message}");
 }

 // Global exception handlers (best-effort). These are diagnostic only.
 try { AppDomain.CurrentDomain.UnhandledException += OnUnhandledException; } catch { }
 try { DispatcherUnhandledException += OnDispatcherUnhandledException; } catch { }
 try { TaskScheduler.UnobservedTaskException += OnUnobservedTaskException; } catch { }

 try
 {
 var args = initialArgs;

 var downloadsStandalone = args.Any(a => string.Equals(a, "--downloads-only", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "--downloader-only", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "/downloads-only", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(a, "/downloader-only", StringComparison.OrdinalIgnoreCase));

 var startOnDownloads = args.Any(a => string.Equals(a, "--downloader", StringComparison.OrdinalIgnoreCase) ||
							   string.Equals(a, "--downloads", StringComparison.OrdinalIgnoreCase) ||
							   string.Equals(a, "/downloader", StringComparison.OrdinalIgnoreCase) ||
							   string.Equals(a, "/downloads", StringComparison.OrdinalIgnoreCase));
 ShowPrimaryWindow(downloadsStandalone, startOnDownloads, validateXaml, args);
 }
 catch (Exception ex)
 {
 try { AppLogger.LogError("Startup: failed to create/show main window", ex); } catch { }
 try
 {
 var chatWindow = new ChatWindow();
 MainWindow = chatWindow;
 chatWindow.Show();
	try { AppLogger.LogInfo("Fallback window shown: ChatWindow"); } catch { }

 chatWindow.Dispatcher.BeginInvoke(new Action(() =>
 {
 try
 {
 if (chatWindow.WindowState == WindowState.Minimized)
 {
 chatWindow.WindowState = WindowState.Normal;
 }
 chatWindow.Activate();
 chatWindow.Topmost = true;
 chatWindow.Topmost = false;
 chatWindow.Focus();
 }
 catch
 {
 }
 }));
 }
 catch (Exception ex2)
 {
 try { AppLogger.LogError("Startup: failed to create/show ChatWindow", ex2); } catch { }
 try
 {
 var fallback = new MainWindow();
 MainWindow = fallback;
 fallback.Show();
	try { AppLogger.LogInfo("Fallback window shown: MainWindow"); } catch { }

 fallback.Dispatcher.BeginInvoke(new Action(() =>
 {
 try
 {
 if (fallback.WindowState == WindowState.Minimized)
 {
 fallback.WindowState = WindowState.Normal;
 }
 fallback.Activate();
 fallback.Topmost = true;
 fallback.Topmost = false;
 fallback.Focus();
 }
 catch
 {
 }
 }));
 }
			catch (Exception ex3)
 {
				AppLogger.LogError("Startup: failed to create/show any window", ex3);
				MessageBox.Show($"Failed to start Atlas: {ex3.Message}", "AtlasAI Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
 Shutdown(-1);
 }
 }
	 }

 }

 private void ShowPrimaryWindow(bool downloadsStandalone, bool startOnDownloads, bool validateXaml, string[] args)
 {
	Window? primaryWindow = null;
	var primaryWindowShown = false;
	var startupActionsInitialized = false;

	Window EnsurePrimaryWindow()
	{
		if (primaryWindow != null)
			return primaryWindow;

		if (downloadsStandalone)
		{
			primaryWindow = new DownloaderWindow();
		}
		else
		{
			primaryWindow = new CommandCenterWindow();
		}

		try
		{
			primaryWindow.Closed += (_, __) =>
			{
				try { AppLogger.LogInfo($"Main window closed: {primaryWindow.GetType().Name}"); } catch { }
			};
		}
		catch { }

		return primaryWindow;
	}

	void InitializeStartupActions(Window window)
	{
		if (startupActionsInitialized)
			return;

		startupActionsInitialized = true;

		try { ProactiveBanterService.Start(); } catch { }

		if (validateXaml)
		{
			try
			{
				window.Dispatcher.BeginInvoke(new Action(() =>
				{
					try { AppLogger.LogInfo("XAML validation: starting template scan..."); } catch { }
					try { ValidateXamlTemplates(); } catch { }
					try { AppLogger.LogInfo("XAML validation: template scan complete"); } catch { }
				}), DispatcherPriority.ContextIdle);
			}
			catch { }
		}

		try { StartExternalPipeServer(); } catch { }
		try { EnqueueExternalArgs(args); } catch { }

		try
		{
			if (startOnDownloads && window is CommandCenterWindow ccw)
			{
				window.Dispatcher.BeginInvoke(new Action(() =>
				{
					try { ccw.NavigateToView("AI DOWNLOADS"); } catch { }
				}), DispatcherPriority.Background);
			}
		}
		catch
		{
		}
	}

	void ActivateWindow(Window window)
	{
		window.Dispatcher.BeginInvoke(new Action(() =>
		{
			try
			{
				if (window.WindowState == WindowState.Minimized)
				{
					window.WindowState = WindowState.Normal;
				}
				window.Visibility = Visibility.Visible;
				window.Opacity = 1;
				window.Activate();
				window.Topmost = true;
				window.Topmost = false;
				window.Focus();
			}
			catch
			{
			}
		}));
	}

	void ShowMainWindow()
	{
		var window = EnsurePrimaryWindow();

		try
		{
			if (window is CommandCenterWindow preloadedCommandCenter)
				preloadedCommandCenter.CompleteStartupActivation();
		}
		catch { }

		ShutdownMode = ShutdownMode.OnMainWindowClose;
		MainWindow = window;

		if (!window.IsVisible)
		{
			window.Show();
		}

		window.Visibility = Visibility.Visible;
		window.Opacity = 1;

		if (!primaryWindowShown)
		{
			primaryWindowShown = true;
			try { AppLogger.LogInfo($"Main window shown: {window.GetType().Name}"); } catch { }
		}

		InitializeStartupActions(window);
		ActivateWindow(window);
	}

	void PreloadMainWindowBehindIntro()
	{
		var window = EnsurePrimaryWindow();

		try
		{
			if (window is CommandCenterWindow ccw)
				ccw.SetStartupPreloadMode(true);

			try { AppLogger.LogInfo($"Main window preloaded: {window.GetType().Name}"); } catch { }
		}
		catch (Exception ex)
		{
			try { AppLogger.LogWarning($"Main window preload failed: {ex.Message}"); } catch { }
		}
	}

	ShowMainWindow();
 }

 private bool TryShowStartupVideo(Action onComplete)
 {
	try
	{
		var videoPath = GetStartupVideoPath();
		if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
			return false;

		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		var completionInvoked = 0;
		DispatcherTimer? startupWatchdog = null;

		void CompleteStartupVideo()
		{
			if (System.Threading.Interlocked.Exchange(ref completionInvoked, 1) != 0)
				return;

			try
			{
				startupWatchdog?.Stop();
			}
			catch
			{
			}

			try
			{
				Dispatcher.BeginInvoke(new Action(onComplete), DispatcherPriority.Normal);
			}
			catch
			{
				onComplete();
			}
		}

		var startupWindow = new StartupVideoWindow();
		startupWindow.Closed += (_, _) => CompleteStartupVideo();
		startupWatchdog = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(18)
		};
		startupWatchdog.Tick += (_, _) =>
		{
			try { AppLogger.LogWarning("Startup video watchdog forced main window activation."); } catch { }
			CompleteStartupVideo();
		};
		startupWatchdog.Start();
		startupWindow.Show();
		startupWindow.PlayVideo(videoPath, CompleteStartupVideo);
		try { AppLogger.LogInfo($"Startup video shown: {videoPath}"); } catch { }
		return true;
	}
	catch (Exception ex)
	{
		try { AppLogger.LogError("Startup: failed to show startup video", ex); } catch { }
		ShutdownMode = ShutdownMode.OnMainWindowClose;
		return false;
	}
 }

 private static string? GetStartupVideoPath()
 {
	static string? FindFirstVideo(string root)
	{
		try
		{
			if (!Directory.Exists(root))
				return null;

			return Directory.EnumerateFiles(root)
				.Where(path => path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
					|| path.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase))
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault();
		}
		catch
		{
			return null;
		}
	}

	static IEnumerable<string> EnumerateCandidateRoots()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var candidates = new List<string>();

		void Add(string? path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return;

			try
			{
				var full = Path.GetFullPath(path);
				if (seen.Add(full))
					candidates.Add(full);
			}
			catch
			{
			}
		}

		var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
		var cwd = Directory.GetCurrentDirectory();

		Add(Path.Combine(baseDir, "Assets", "Startup_Video"));
		Add(Path.Combine(cwd, "Assets", "Startup_Video"));

		try
		{
			var current = new DirectoryInfo(string.IsNullOrWhiteSpace(baseDir) ? cwd : baseDir);
			for (var i = 0; i < 4 && current != null; i++, current = current.Parent)
				Add(Path.Combine(current.FullName, "Assets", "Startup_Video"));
		}
		catch
		{
		}

		return candidates;
	}

	foreach (var root in EnumerateCandidateRoots())
	{
		var video = FindFirstVideo(root);
		if (!string.IsNullOrWhiteSpace(video))
			return video;
	}

	return null;
 }

 private static void LogCrash(string source, Exception ex)
 {
	try
	{
		try
		{
			if (ex is System.Windows.Markup.XamlParseException xpe)
			{
				var baseUri = "";
				try { baseUri = xpe.BaseUri?.ToString() ?? ""; } catch { }

				AppLogger.Log($" [CRASH] {source}: XamlParseException: {xpe.Message}\n   BaseUri: {baseUri}\n   Line: {xpe.LineNumber}, Pos: {xpe.LinePosition}\n   Inner: {xpe.InnerException?.GetType().Name}: {xpe.InnerException?.Message}\n   Stack: {xpe.StackTrace}");
				return;
			}
		}
		catch { }

		AppLogger.Log($" [CRASH] {source}: {ex}");
	}
	catch
	{
	}
 }

 private static void ValidateXamlTemplates()
 {
	try
	{
		var app = Application.Current;
		if (app == null) return;

		var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

		void Inspect(object? value)
		{
			if (value == null) return;
			if (!visited.Add(value)) return;

			switch (value)
			{
				case ResourceDictionary rd:
					WalkDictionary(rd);
					break;
				case Style style:
					InspectStyle(style);
					break;
				case FrameworkTemplate ft:
					TryLoadTemplate(ft);
					break;
			}
		}

		void WalkDictionary(ResourceDictionary rd)
		{
			try
			{
				foreach (System.Collections.DictionaryEntry entry in rd)
				{
					Inspect(entry.Value);
				}
			}
			catch
			{
			}

			try
			{
				foreach (var md in rd.MergedDictionaries)
				{
					Inspect(md);
				}
			}
			catch
			{
			}
		}

		void InspectStyle(Style style)
		{
			try
			{
				if (style.BasedOn != null) InspectStyle(style.BasedOn);
			}
			catch
			{
			}

			try
			{
				foreach (var sb in style.Setters)
				{
					if (sb is Setter s) Inspect(s.Value);
				}
			}
			catch
			{
			}

			try
			{
				foreach (var tb in style.Triggers)
				{
					switch (tb)
					{
						case Trigger t:
							foreach (var sb in t.Setters) if (sb is Setter s) Inspect(s.Value);
							break;
						case DataTrigger dt:
							foreach (var sb in dt.Setters) if (sb is Setter s) Inspect(s.Value);
							break;
						case MultiTrigger mt:
							foreach (var sb in mt.Setters) if (sb is Setter s) Inspect(s.Value);
							break;
						case MultiDataTrigger mdt:
							foreach (var sb in mdt.Setters) if (sb is Setter s) Inspect(s.Value);
							break;
					}
				}
			}
			catch
			{
			}
		}

		void TryLoadTemplate(FrameworkTemplate ft)
		{
			try
			{
				_ = ft.LoadContent();
			}
			catch (Exception ex)
			{
				LogCrash("ValidateXamlTemplates", ex);
			}
		}

		Inspect(app.Resources);
	}
	catch
	{
	}
 }

 private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
 {
	public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
	public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
	public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
 }

 private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
 {
	try
	{
		var ex = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
		LogCrash("AppDomain.UnhandledException", ex);
	}
	catch
	{
	}
 }

 private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
 {
	try
	{
		LogCrash("DispatcherUnhandledException", e.Exception);
	}
	catch
	{
	}
	// Prevent unhandled UI-thread exceptions from crashing the whole app.
	e.Handled = true;
 }

 private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
 {
	try
	{
		LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
	}
	catch
	{
	}
 }

 private void InitializeServices()
 {
 // Eagerly initialize services to catch errors early.
 try
 {
 var serviceProvider = Services as ServiceProvider;
 if (serviceProvider == null) throw new Exception("Service provider is not built.");

 // Example of getting a service to trigger its creation
 var context = GetService<IContextService>();
 if (context == null) throw new Exception("Failed to initialize ContextService.");

	try
	{
		CompanionTransportService.Instance.Start();
		AppLogger.LogInfo("[Companion] Auto-started during app initialization.");
	}
	catch (Exception ex)
	{
		AppLogger.LogWarning($"[Companion] Auto-start failed during app initialization: {ex.Message}");
	}

	try
	{
		_ringDoorbellMonitorService.Start();
		AppLogger.LogInfo("[RingDoorbellMonitor] Auto-started during app initialization.");
	}
	catch (Exception ex)
	{
		AppLogger.LogWarning($"[RingDoorbellMonitor] Auto-start failed during app initialization: {ex.Message}");
	}
 }
 catch (Exception ex)
 {
 MessageBox.Show($"A critical error occurred during application startup: {ex.Message}", "AtlasAI Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
 Shutdown(-1);
 }
 }

 private void CheckForSingleInstance(string[] args)
 {
 #if DEBUG
 // Dev quality-of-life: allow multiple instances so UI/startup changes can be validated
 // without hunting down a previously running window.
 _mutexOwned = true;
 return;
 #else
 _singleInstanceMutex = new Mutex(true, MutexName, out _mutexOwned);
 if (!_mutexOwned)
 {
 try
 {
	 if (TrySendArgsToRunningInstance(args))
	 {
		 Shutdown();
		 return;
	 }
 }
 catch
 {
 }

 MessageBox.Show("AtlasAI is already running.", "Application Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
 Shutdown();
 }
 #endif
 }

 private static string GetExecutablePath()
 {
	 try
	 {
		 var fromMainModule = Process.GetCurrentProcess()?.MainModule?.FileName;
		 if (!string.IsNullOrWhiteSpace(fromMainModule) && File.Exists(fromMainModule)) return fromMainModule;
	 }
	 catch
	 {
	 }

	 try
	 {
		 var fromEnv = Environment.ProcessPath;
		 if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;
	 }
	 catch
	 {
	 }

	 return System.Reflection.Assembly.GetExecutingAssembly().Location;
 }

 private static bool TrySendArgsToRunningInstance(string[] args)
 {
	 try
	 {
		 using var client = new NamedPipeClientStream(".", ExternalPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
		 client.Connect(250);
		 var payload = JsonSerializer.Serialize(args ?? Array.Empty<string>());
		 using var sw = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
		 sw.Write(payload);
		 return true;
	 }
	 catch
	 {
		 return false;
	 }
 }

 private static void StartExternalPipeServer()
 {
 #if DEBUG
	 // Multiple instances are allowed in Debug; a global pipe would conflict.
	 return;
 #else
	 if (_externalPipeCts != null) return;
	 _externalPipeCts = new CancellationTokenSource();
	 var token = _externalPipeCts.Token;

	 Task.Run(async () =>
	 {
		 while (!token.IsCancellationRequested)
		 {
			 try
			 {
				 using var server = new NamedPipeServerStream(
					 ExternalPipeName,
					 PipeDirection.In,
					 1,
					 PipeTransmissionMode.Byte,
					 PipeOptions.Asynchronous);

				 await server.WaitForConnectionAsync(token);
				 using var sr = new StreamReader(server, Encoding.UTF8);
				 var json = await sr.ReadToEndAsync();
				 if (string.IsNullOrWhiteSpace(json)) continue;

				 string[]? incoming = null;
				 try { incoming = JsonSerializer.Deserialize<string[]>(json); } catch { }
				 if (incoming == null || incoming.Length == 0) continue;

				 EnqueueExternalArgs(incoming);
			 }
			 catch (OperationCanceledException)
			 {
				 break;
			 }
			 catch
			 {
				 try { await Task.Delay(150, token); } catch { }
			 }
		 }
	 }, token);
 #endif
 }

 private static void EnqueueExternalArgs(string[] args)
 {
	 if (args == null || args.Length == 0) return;
	 _pendingExternalArgs.Enqueue(args);

	 try
	 {
		 Current?.Dispatcher?.BeginInvoke(new Action(HandlePendingExternalArgs), DispatcherPriority.Background);
	 }
	 catch
	 {
	 }
 }

 private static void HandlePendingExternalArgs()
 {
	 while (_pendingExternalArgs.TryDequeue(out var args))
	 {
		 try { HandleExternalOpen(args); } catch { }
	 }
 }

 private static void HandleExternalOpen(string[] args)
 {
	 try
	 {
		 var clean = (args ?? Array.Empty<string>())
			 .Select(a => (a ?? "").Trim())
			 .Where(a => !string.IsNullOrWhiteSpace(a))
			 .Where(a => !a.StartsWith("--", StringComparison.OrdinalIgnoreCase) && !a.StartsWith("/", StringComparison.OrdinalIgnoreCase))
			 .ToArray();

		 if (clean.Length == 0) return;

		 var urls = new List<string>();
		 var files = new List<string>();

		 foreach (var raw in clean)
		 {
			 if (TryUnwrapAtlasProtocol(raw, out var unwrapped) && !string.IsNullOrWhiteSpace(unwrapped))
			 {
				 if (IsHttpUrl(unwrapped)) urls.Add(unwrapped);
				 else if (TryGetExistingFilePath(unwrapped, out var fp)) files.Add(fp);
				 continue;
			 }

			 if (IsHttpUrl(raw)) { urls.Add(raw); continue; }
			 if (TryGetExistingFilePath(raw, out var filePath)) { files.Add(filePath); continue; }
		 }

		 // Media file launch: route to Media Centre and play.
		 var mediaFile = files.FirstOrDefault(IsMediaFilePath);
		 if (!string.IsNullOrWhiteSpace(mediaFile))
		 {
			 if (Current?.MainWindow is CommandCenterWindow ccw)
			 {
				 try { ccw.NavigateToView("AI MEDIA CENTRE"); } catch { }
				 TryPlayMediaWhenReady(mediaFile);
			 }
			 else
			 {
				 TryPlayMediaWhenReady(mediaFile);
			 }
			 return;
		 }

		 if (urls.Count == 0) return;

		 var distinctUrls = urls
			 .Select(u => (u ?? "").Trim())
			 .Where(u => !string.IsNullOrWhiteSpace(u))
			 .Distinct(StringComparer.OrdinalIgnoreCase)
			 .ToList();

		 try
		 {
			 if (distinctUrls.Count == 1) DownloadService.Instance.AddDownload(distinctUrls[0]);
			 else _ = DownloadService.Instance.AddDownloadsAsync(distinctUrls);
		 }
		 catch
		 {
		 }

		 try
		 {
			 if (Current?.MainWindow is CommandCenterWindow ccw)
				 ccw.NavigateToView("AI DOWNLOADS");
		 }
		 catch
		 {
		 }
	 }
	 catch
	 {
	 }
 }

 private static bool IsHttpUrl(string value)
 {
	 try
	 {
		 return Uri.TryCreate(value, UriKind.Absolute, out var u) &&
				(string.Equals(u.Scheme, "http", StringComparison.OrdinalIgnoreCase) || string.Equals(u.Scheme, "https", StringComparison.OrdinalIgnoreCase));
	 }
	 catch
	 {
		 return false;
	 }
 }

 private static bool TryGetExistingFilePath(string value, out string filePath)
 {
	 filePath = "";
	 try
	 {
		 if (string.IsNullOrWhiteSpace(value)) return false;

		 if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
		 {
			 var lp = uri.LocalPath;
			 if (!string.IsNullOrWhiteSpace(lp) && File.Exists(lp))
			 {
				 filePath = lp;
				 return true;
			 }
		 }

		 if (File.Exists(value))
		 {
			 filePath = value;
			 return true;
		 }
	 }
	 catch
	 {
	 }
	 return false;
 }

 private static bool IsMediaFilePath(string path)
 {
	 try
	 {
		 var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
		 return WindowsShellIntegration.MediaExtensions.Contains(ext);
	 }
	 catch
	 {
		 return false;
	 }
 }

 private static void TryPlayMediaWhenReady(string pathOrUrl)
 {
	 _ = Task.Run(async () =>
	 {
		 for (var i = 0; i < 40; i++)
		 {
			 try
			 {
				 var vm = MediaCentreViewModel.Instance;
				 if (vm != null)
				 {
					 try
					 {
						 Current?.Dispatcher?.BeginInvoke(new Action(() =>
						 {
							 // try { vm.PlayExternalUrlOrPath(pathOrUrl); } catch { }
						 }), DispatcherPriority.Background);
					 }
					 catch
					 {
					 }
					 return;
				 }
			 }
			 catch
			 {
			 }

			 try { await Task.Delay(100).ConfigureAwait(false); } catch { }
		 }
	 });
 }

 private static bool TryUnwrapAtlasProtocol(string raw, out string unwrapped)
 {
	 unwrapped = "";
	 try
	 {
		 if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return false;
		 if (!string.Equals(uri.Scheme, "atlasai", StringComparison.OrdinalIgnoreCase)) return false;

		 // Expected forms:
		 //  - atlasai://download?url=<encoded>
		 //  - atlasai://open?path=<encoded>
		 var url = GetQueryParam(uri, "url");
		 if (!string.IsNullOrWhiteSpace(url)) { unwrapped = url; return true; }
		 var path = GetQueryParam(uri, "path");
		 if (!string.IsNullOrWhiteSpace(path)) { unwrapped = path; return true; }

		 // If no query param, just return the original target string.
		 var maybe = uri.AbsoluteUri;
		 if (!string.IsNullOrWhiteSpace(maybe))
		 {
			 unwrapped = maybe;
			 return true;
		 }
	 }
	 catch
	 {
	 }
	 return false;
 }

 private static string GetQueryParam(Uri uri, string key)
 {
	 try
	 {
		 var query = (uri?.Query ?? "").TrimStart('?');
		 if (string.IsNullOrWhiteSpace(query)) return "";

		 foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
		 {
			 var kv = part.Split('=', 2);
			 if (kv.Length != 2) continue;
			 var k = Uri.UnescapeDataString(kv[0] ?? "");
			 if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
			 return Uri.UnescapeDataString(kv[1] ?? "");
		 }
	 }
	 catch
	 {
	 }
	 return "";
 }

 protected override void OnExit(ExitEventArgs e)
 {
 try
 {
	 _ringDoorbellMonitorService.Dispose();
 }
 catch
 {
 }

 try
 {
	 _externalPipeCts?.Cancel();
	 _externalPipeCts?.Dispose();
	 _externalPipeCts = null;
 }
 catch
 {
 }

 if (_mutexOwned)
 {
 _singleInstanceMutex?.ReleaseMutex();
 }
 _singleInstanceMutex?.Dispose();
 base.OnExit(e);
 }
 }
}
