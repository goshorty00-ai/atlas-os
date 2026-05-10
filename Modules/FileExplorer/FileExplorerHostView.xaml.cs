using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Web.WebView2.Core;

namespace AtlasAI.Modules.FileExplorer
{
    public partial class FileExplorerHostView : UserControl
    {
        private const string VirtualHost = "atlas-file-explorer.local";
        private const int MaxDirectoryItems = 500;
        private const long MaxTextPreviewBytes = 512 * 1024;
        private const long MaxImagePreviewBytes = 8 * 1024 * 1024;
        private const int MaxCopyFolderItemCount = 1000;
        private const long MaxCopyTotalBytes = 500L * 1024 * 1024;
        private const long MaxCopyFileBytes = 500L * 1024 * 1024;
        private const int MaxAtlasBrainScanDepth = 2;
        private const int MaxAtlasBrainScanEntries = 500;
        private const int TransferBufferBytes = 128 * 1024;
        private const int MaxExtractFileCount = 5000;
        private const long MaxExtractTotalBytes = 10L * 1024 * 1024 * 1024;

        private static readonly HashSet<string> AllowedExtractExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z"
        };

        private static readonly string[] SevenZipPaths =
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
        };

        private static readonly HashSet<string> AllowedTextPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".json", ".xml", ".cs", ".xaml", ".js", ".ts", ".tsx", ".html", ".css", ".log", ".csv"
        };

        private static readonly HashSet<string> AllowedImagePreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".gif"
        };

        private static readonly HashSet<string> BlockedPreviewExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".zip", ".rar", ".7z", ".env", ".pfx", ".key", ".secrets"
        };

        private static readonly HashSet<string> BlockedOpenExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".scr", ".vbs", ".jar", ".reg",
            ".com", ".pif", ".wsf", ".wsh", ".js"
        };

        private static readonly HashSet<string> AllowedAISummaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".json", ".xml", ".csv", ".log",
            ".cs", ".xaml", ".js", ".ts", ".tsx", ".html", ".css",
            ".py", ".ps1", ".sql", ".yaml", ".yml"
        };

        private static readonly HashSet<string> BlockedSecretExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".env", ".key", ".pem", ".pfx", ".cer", ".crt"
        };

        private static readonly HashSet<string> AllowedCreateFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".json"
        };

        public FileExplorerHostView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await EnsureInitializedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] initError={ex.Message}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (FileExplorerWebView?.CoreWebView2 != null)
                {
                    FileExplorerWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                    FileExplorerWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }
            }
            catch
            {
            }
        }

        private async Task EnsureInitializedAsync()
        {
            System.Diagnostics.Debug.WriteLine("[FileExplorerHost] step=init");

            if (FileExplorerWebView?.CoreWebView2 != null)
                return;

            await FileExplorerWebView.EnsureCoreWebView2Async();

            FileExplorerWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            FileExplorerWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            FileExplorerWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

            var distPath = ResolveDistPath();
            var indexPath = Path.Combine(distPath, "index.html");
            var distExists = File.Exists(indexPath);

            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] distPath={distPath}");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] distExists={distExists}");

            if (!distExists)
            {
                try { MissingUiOverlay.Visibility = Visibility.Visible; } catch { }
                return;
            }

            try { MissingUiOverlay.Visibility = Visibility.Collapsed; } catch { }

            var virtualHostMapped = false;
            try
            {
                FileExplorerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost,
                    distPath,
                    CoreWebView2HostResourceAccessKind.Allow);
                virtualHostMapped = true;
            }
            catch
            {
                virtualHostMapped = false;
            }

            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] virtualHostMapped={virtualHostMapped}");

            FileExplorerWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            FileExplorerWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            FileExplorerWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            FileExplorerWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var navigateUrl = $"https://{VirtualHost}/index.html?v={DateTime.UtcNow.Ticks}";
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] navigate={navigateUrl}");
            FileExplorerWebView.CoreWebView2.Navigate(navigateUrl);
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return;

                if (!doc.RootElement.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                    return;

                var type = (typeElement.GetString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(type))
                    return;

                switch (type)
                {
                    case "list-roots":
                        HandleListRoots();
                        break;
                    case "list-directory":
                        HandleListDirectory(doc.RootElement);
                        break;
                    case "preview-file":
                        HandlePreviewFile(doc.RootElement);
                        break;
                    case "open-file":
                        HandleOpenFile(doc.RootElement);
                        break;
                    case "open-folder-external":
                        HandleOpenFolderExternal(doc.RootElement);
                        break;
                    case "open-network-external":
                        HandleOpenNetworkExternal();
                        break;
                    case "ai-summarize-file":
                        HandleAISummarizeFile(doc.RootElement);
                        break;
                    case "ai-explain-file":
                        HandleAIExplainFile(doc.RootElement);
                        break;
                    case "rename-item":
                        HandleRenameItem(doc.RootElement);
                        break;
                    case "create-file":
                        HandleCreateFile(doc.RootElement);
                        break;
                    case "create-folder":
                        HandleCreateFolder(doc.RootElement);
                        break;
                    case "copy-item":
                        HandleCopyItem(doc.RootElement);
                        break;
                    case "transfer-copy":
                        HandleTransferCopy(doc.RootElement);
                        break;
                    case "move-item":
                        HandleMoveItem(doc.RootElement);
                        break;
                    case "transfer-move":
                        HandleTransferMove(doc.RootElement);
                        break;
                    case "delete-item":
                        HandleDeleteItem(doc.RootElement);
                        break;
                    case "file-explorer-sidebar-tools-status":
                        HandleSidebarToolsStatus();
                        break;
                    case "ai-folder-brief":
                        HandleAIFolderBrief(doc.RootElement);
                        break;
                    case "ai-smart-rename":
                        HandleAISmartRename(doc.RootElement);
                        break;
                    case "ai-folder-question":
                        HandleAIFolderQuestion(doc.RootElement);
                        break;
                    case "ai-folder-actions":
                        HandleAIFolderActions(doc.RootElement);
                        break;
                    case "atlas-brain-project-scan":
                        HandleAtlasBrainProjectScan(doc.RootElement);
                        break;
                    case "atlas-brain-action-plan":
                        HandleAtlasBrainActionPlan(doc.RootElement);
                        break;
                    case "atlas-brain-organize-plan":
                        HandleAtlasBrainOrganizePlan(doc.RootElement);
                        break;
                    case "extract-archive":
                        HandleExtractArchive(doc.RootElement);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void HandleListRoots()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] message=list-roots");

                var roots = new List<object>();
                AddKnownRoot(roots, "home", "Home", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                AddKnownRoot(roots, "desktop", "Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                AddKnownRoot(roots, "documents", "Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                AddKnownRoot(roots, "downloads", "Downloads", ResolveDownloadsPath());
                AddKnownRoot(roots, "pictures", "Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                AddKnownRoot(roots, "videos", "Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
                AddKnownRoot(roots, "music", "Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

                foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
                {
                    try
                    {
                        var driveRoot = drive.RootDirectory.FullName;
                        if (!string.IsNullOrWhiteSpace(driveRoot) && Directory.Exists(driveRoot))
                        {
                            roots.Add(new
                            {
                                id = $"drive-{drive.Name.TrimEnd('\\').Replace(":", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()}",
                                label = driveRoot,
                                path = driveRoot,
                                group = "drive"
                            });
                        }
                    }
                    catch
                    {
                    }
                }

                PostMessage(new { type = "file-explorer-roots", roots });
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostMessage(new { type = "file-explorer-roots", roots = Array.Empty<object>() });
            }
        }

        private void HandleListDirectory(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            var requestDirectoryPath = GetString(root, "directoryPath");
            var requestedPath = string.IsNullOrWhiteSpace(requestPath) ? requestDirectoryPath : requestPath;
            var requestId = GetString(root, "requestId");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] list-directory received path={requestedPath} requestId={requestId}");

            try
            {
                var fullPath = ValidateDirectoryPath(requestedPath);
                var items = new List<object>(MaxDirectoryItems);
                var folderCount = 0;
                var fileCount = 0;

                foreach (var dir in Directory.EnumerateDirectories(fullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (items.Count >= MaxDirectoryItems)
                        break;

                    try
                    {
                        var info = new DirectoryInfo(dir);
                        items.Add(new
                        {
                            name = info.Name,
                            path = info.FullName,
                            kind = "folder",
                            extension = string.Empty,
                            sizeBytes = (long?)null,
                            modifiedUtc = info.LastWriteTimeUtc,
                            isHidden = (info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden,
                            canPreview = false
                        });
                        folderCount += 1;
                    }
                    catch
                    {
                    }
                }

                foreach (var file in Directory.EnumerateFiles(fullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (items.Count >= MaxDirectoryItems)
                        break;

                    try
                    {
                        var info = new FileInfo(file);
                        var ext = info.Extension ?? string.Empty;
                        items.Add(new
                        {
                            name = info.Name,
                            path = info.FullName,
                            kind = "file",
                            extension = ext,
                            sizeBytes = info.Length,
                            modifiedUtc = info.LastWriteTimeUtc,
                            isHidden = (info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden,
                            canPreview = CanPreviewFile(info.FullName, ext)
                        });
                        fileCount += 1;
                    }
                    catch
                    {
                    }
                }

                var parentPath = Directory.GetParent(fullPath)?.FullName;
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] list-directory result path={fullPath} requestId={requestId} folders={folderCount} files={fileCount}");

                PostMessage(new
                {
                    type = "file-explorer-directory",
                    requestId,
                    path = fullPath,
                    directoryPath = fullPath,
                    parentPath,
                    items,
                    folderCount,
                    fileCount
                });
            }
            catch (UnauthorizedAccessException)
            {
                PostDirectoryError(requestedPath, "Access denied for this folder.", requestId);
            }
            catch (DirectoryNotFoundException)
            {
                PostDirectoryError(requestedPath, "Folder not found.", requestId);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostDirectoryError(requestedPath, "Unable to open this folder.", requestId);
            }
        }

        private void HandlePreviewFile(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=preview-file path={requestPath}");

            try
            {
                var fullPath = ValidateFilePath(requestPath);
                var info = new FileInfo(fullPath);
                var extension = (info.Extension ?? string.Empty).ToLowerInvariant();
                var fileName = info.Name;

                if (IsBlockedByPolicy(fileName, extension))
                {
                    PostPreview(fullPath, false, "none", null, null, "Preview blocked for this file type.");
                    return;
                }

                if (AllowedTextPreviewExtensions.Contains(extension))
                {
                    if (info.Length > MaxTextPreviewBytes)
                    {
                        PostPreview(fullPath, false, "text", null, null, "Text preview is limited to 512KB.");
                        return;
                    }

                    string text;
                    using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        text = reader.ReadToEnd();
                    }

                    PostPreview(fullPath, true, "text", text, null, null);
                    return;
                }

                if (AllowedImagePreviewExtensions.Contains(extension))
                {
                    if (info.Length > MaxImagePreviewBytes)
                    {
                        PostPreview(fullPath, false, "image", null, null, "Image preview is limited to 8MB.");
                        return;
                    }

                    var url = new Uri(fullPath).AbsoluteUri;
                    PostPreview(fullPath, true, "image", null, url, null);
                    return;
                }

                PostPreview(fullPath, false, "none", null, null, "Preview is not available for this file type yet.");
            }
            catch (UnauthorizedAccessException)
            {
                PostPreview(requestPath, false, "none", null, null, "Access denied for this file.");
            }
            catch (FileNotFoundException)
            {
                PostPreview(requestPath, false, "none", null, null, "File not found.");
            }
            catch (DirectoryNotFoundException)
            {
                PostPreview(requestPath, false, "none", null, null, "File path not found.");
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostPreview(requestPath, false, "none", null, null, "Unable to preview this file.");
            }
        }

        private void HandleOpenFile(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=open-file path={requestPath}");

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath))
                {
                    PostOpenResult(requestPath, false, "File path is required.");
                    return;
                }

                if (requestPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    PostOpenResult(requestPath, false, "Path contains invalid characters.");
                    return;
                }

                var fullPath = Path.GetFullPath(requestPath.Trim());

                if (!File.Exists(fullPath))
                {
                    PostOpenResult(requestPath, false, "File not found.");
                    return;
                }

                var ext = Path.GetExtension(fullPath);
                if (BlockedOpenExtensions.Contains(ext))
                {
                    PostOpenResult(requestPath, false, $"Opening '{ext}' files is blocked for security reasons.");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });

                PostOpenResult(fullPath, true, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostOpenResult(requestPath, false, "Unable to open this file.");
            }
        }

        private void HandleOpenFolderExternal(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=open-folder-external path={requestPath}");

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath))
                {
                    PostOpenResult(requestPath, false, "Folder path is required.");
                    return;
                }

                if (requestPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    PostOpenResult(requestPath, false, "Path contains invalid characters.");
                    return;
                }

                var fullPath = Path.GetFullPath(requestPath.Trim());

                if (!Directory.Exists(fullPath))
                {
                    PostOpenResult(requestPath, false, "Folder not found.");
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{fullPath}\"",
                    UseShellExecute = true
                });

                PostOpenResult(fullPath, true, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostOpenResult(requestPath, false, "Unable to open folder in Explorer.");
            }
        }

        private void HandleOpenNetworkExternal()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:networkplacesfolder",
                    UseShellExecute = true
                });

                PostMessage(new { type = "file-explorer-network-open-result", ok = true, error = (string?)null });
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostMessage(new { type = "file-explorer-network-open-result", ok = false, error = "Unable to open Windows Network tools." });
            }
        }

        private void HandleSidebarToolsStatus()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] tool-status start");
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var workspaceRoot = ResolveWorkspaceRootPath();

                var cloudFolders = new[]
                {
                    new { label = "OneDrive", path = Path.Combine(userProfile, "OneDrive"), exists = Directory.Exists(Path.Combine(userProfile, "OneDrive")) },
                    new { label = "Google Drive", path = Path.Combine(userProfile, "Google Drive"), exists = Directory.Exists(Path.Combine(userProfile, "Google Drive")) },
                    new { label = "Dropbox", path = Path.Combine(userProfile, "Dropbox"), exists = Directory.Exists(Path.Combine(userProfile, "Dropbox")) },
                };

                var vaultFolders = new[]
                {
                    new { label = "Workspace Secrets", path = Path.Combine(workspaceRoot, "Secrets"), exists = Directory.Exists(Path.Combine(workspaceRoot, "Secrets")) },
                    new { label = "Documents Secrets", path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Secrets"), exists = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Secrets")) },
                    new { label = "Atlas Secrets", path = @"D:\My Apps\AOS\Atlas.OS\Secrets", exists = Directory.Exists(@"D:\My Apps\AOS\Atlas.OS\Secrets") },
                };

                var mappedDrives = DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.DriveType == DriveType.Network)
                    .Select(drive => new
                    {
                        label = drive.Name.TrimEnd(Path.DirectorySeparatorChar),
                        path = drive.RootDirectory.FullName
                    })
                    .ToArray();

                PostMessage(new
                {
                    type = "file-explorer-sidebar-tools-status",
                    cloudFolders,
                    vaultFolders,
                    mappedDrives,
                    error = (string?)null
                });
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] tool-status result ok=true cloud={cloudFolders.Length} vault={vaultFolders.Length} mapped={mappedDrives.Length}");
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostMessage(new
                {
                    type = "file-explorer-sidebar-tools-status",
                    cloudFolders = Array.Empty<object>(),
                    vaultFolders = Array.Empty<object>(),
                    mappedDrives = Array.Empty<object>(),
                    error = "Unable to inspect local tool folders."
                });
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] tool-status result ok=false");
            }
        }

        private void PostOpenResult(string path, bool ok, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] open-result ok={ok}");
            if (!string.IsNullOrWhiteSpace(error))
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] error={error}");

            PostMessage(new { type = "file-explorer-open-result", ok, path, error });
        }

        private void HandleAISummarizeFile(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=ai-summarize-file path={requestPath}");

            if (!TryPrepareAIFileContent(
                requestPath,
                "ai-summary",
                "summary",
                out var fullPath,
                out var fileName,
                out var extension,
                out var sizeBytes,
                out var fileContent,
                out var error,
                out var blockedBySafety))
            {
                PostAISummaryResult(fullPath, false, null, error, null);
                return;
            }

            CallAISummarizeAsync(fullPath, fileName, extension, sizeBytes, fileContent);
        }

        private void HandleAIExplainFile(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=ai-explain-file path={requestPath}");

            if (!TryPrepareAIFileContent(
                requestPath,
                "ai-explain",
                "explanation",
                out var fullPath,
                out var fileName,
                out var extension,
                out var sizeBytes,
                out var fileContent,
                out var error,
                out var blockedBySafety))
            {
                PostAIExplanationResult(fullPath, false, null, error, null);
                return;
            }

            CallAIExplainAsync(fullPath, fileName, extension, sizeBytes, fileContent);
        }

        private static bool TryPrepareAIFileContent(
            string requestPath,
            string logScope,
            string userAction,
            out string fullPath,
            out string fileName,
            out string extension,
            out long sizeBytes,
            out string content,
            out string error,
            out bool blockedBySafety)
        {
            fullPath = requestPath;
            fileName = string.Empty;
            extension = string.Empty;
            sizeBytes = 0;
            content = string.Empty;
            blockedBySafety = false;
            error = $"Unable to {userAction} this file.";

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath))
                {
                    blockedBySafety = true;
                    error = "File path is required.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] {logScope} safety=blocked reason=missing_path");
                    return false;
                }

                var normalized = Path.GetFullPath(requestPath.Trim());
                fullPath = normalized;

                if (Directory.Exists(normalized))
                {
                    blockedBySafety = true;
                    error = "Folders are not supported for this AI action.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] {logScope} safety=blocked reason=folder");
                    return false;
                }

                fullPath = ValidateFilePath(normalized);
                var info = new FileInfo(fullPath);
                fileName = info.Name;
                extension = (info.Extension ?? string.Empty).ToLowerInvariant();
                sizeBytes = info.Length;

                if (!AllowedAISummaryExtensions.Contains(extension))
                {
                    blockedBySafety = true;
                    error = "File type not supported for AI actions.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] {logScope} safety=blocked reason=unsupported_extension");
                    return false;
                }

                if (BlockedSecretExtensions.Contains(extension) || IsBlockedBySecretFilename(fileName))
                {
                    blockedBySafety = true;
                    error = "This file is blocked for security reasons.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] {logScope} safety=blocked reason=secret_file");
                    return false;
                }

                if (sizeBytes > MaxTextPreviewBytes)
                {
                    blockedBySafety = true;
                    error = $"File is too large ({FormatBytes(sizeBytes)}). Max size: 512 KB.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] {logScope} safety=blocked reason=size_exceeded");
                    return false;
                }

                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    content = reader.ReadToEnd();
                }

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] {logScope} safety=allowed");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied for this file.";
                return false;
            }
            catch (FileNotFoundException)
            {
                error = "File not found.";
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async void CallAISummarizeAsync(string fullPath, string fileName, string extension, long sizeBytes, string content)
        {
            try
            {
                var promptMessages = new List<object>
                {
                    new { role = "system", content = "You are a code/text analyst. Analyze the selected local file and provide a brief honest summary. If the content appears to contain secrets/credentials, STOP and warn the user. Respond with:\n1. What this file is\n2. Key points (3-5 bullets)\n3. Risks/concerns if any\n4. Suggested next action\n\nKeep the response under 300 tokens." },
                    new { role = "user", content = $"File: {fileName}\nType: {extension}\nSize: {FormatBytes(sizeBytes)}\n\n=== CONTENT ===\n{content}" }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer",
                    promptMessages,
                    500,
                    System.Threading.CancellationToken.None
                );

                if (response?.Success == true)
                {
                    var summary = response.Content ?? "[No summary returned]";
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-summary result ok=true provider={providerName}");
                    PostAISummaryResult(fullPath, true, summary, null, providerName);
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable or returned no response.";
                    if (error.Contains("not wired") || error.Contains("unavailable") || error.Contains("not configured"))
                    {
                        error = "AI summarization is not wired to Atlas AI provider yet.";
                    }
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-summary result ok=false error=provider_error");
                    PostAISummaryResult(fullPath, false, null, error, null);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostAISummaryResult(fullPath, false, null, "AI service error: " + (ex.Message ?? "Unknown"), null);
            }
        }

        private async void CallAIExplainAsync(string fullPath, string fileName, string extension, long sizeBytes, string content)
        {
            try
            {
                var promptMessages = new List<object>
                {
                    new { role = "system", content = "Explain this selected local file for the user in plain English. Do not expose secrets. If the content appears to contain credentials, stop and warn. Return:\n1. What this file does\n2. Main structure or sections\n3. Important functions/classes/settings\n4. Risks or things to check\n5. Suggested next step" },
                    new { role = "user", content = $"File: {fileName}\nType: {extension}\nSize: {FormatBytes(sizeBytes)}\n\n=== CONTENT ===\n{content}" }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer",
                    promptMessages,
                    900,
                    System.Threading.CancellationToken.None
                );

                if (response?.Success == true)
                {
                    var explanation = response.Content ?? "[No explanation returned]";
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-explain result ok=true provider={providerName}");
                    PostAIExplanationResult(fullPath, true, explanation, null, providerName);
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable or returned no response.";
                    if (error.Contains("not wired") || error.Contains("unavailable") || error.Contains("not configured"))
                    {
                        error = "AI explanation is not wired to Atlas AI provider yet.";
                    }
                    System.Diagnostics.Debug.WriteLine("[FileExplorerHost] ai-explain result ok=false provider=none");
                    PostAIExplanationResult(fullPath, false, null, error, null);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostAIExplanationResult(fullPath, false, null, "AI service error: " + (ex.Message ?? "Unknown"), null);
            }
        }

        private void PostAISummaryResult(string path, bool ok, string? summary, string? error, string? provider)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-summary posted ok={ok}");
            PostMessage(new { type = "file-explorer-ai-summary", ok, path, summary, error, provider });
        }

        private void PostAIExplanationResult(string path, bool ok, string? explanation, string? error, string? provider)
        {
            PostMessage(new { type = "file-explorer-ai-explanation", ok, path, explanation, error, provider });
        }

        private void HandleRenameItem(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            var requestNewName = GetString(root, "newName");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=rename-item path={requestPath}");

            try
            {
                if (!TryBuildSafeRenameTarget(requestPath, requestNewName, out var oldPath, out var newPath, out var error))
                {
                    PostRenameResult(false, requestPath, string.Empty, error);
                    return;
                }

                if (File.Exists(oldPath))
                {
                    File.Move(oldPath, newPath);
                    PostRenameResult(true, oldPath, newPath, null);
                    return;
                }

                if (Directory.Exists(oldPath))
                {
                    Directory.Move(oldPath, newPath);
                    PostRenameResult(true, oldPath, newPath, null);
                    return;
                }

                PostRenameResult(false, requestPath, string.Empty, "Item not found.");
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostRenameResult(false, requestPath, string.Empty, "Unable to rename this item.");
            }
        }

        private static bool TryBuildSafeRenameTarget(string requestPath, string requestNewName, out string oldPath, out string newPath, out string error)
        {
            oldPath = string.Empty;
            newPath = string.Empty;
            error = "Rename failed.";

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath))
                {
                    error = "Path is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(requestNewName))
                {
                    error = "New name is required.";
                    return false;
                }

                oldPath = Path.GetFullPath(requestPath.Trim());
                var existsAsFile = File.Exists(oldPath);
                var existsAsDir = Directory.Exists(oldPath);
                if (!existsAsFile && !existsAsDir)
                {
                    error = "Item not found.";
                    return false;
                }

                if (IsDriveRootPath(oldPath))
                {
                    error = "Root paths cannot be renamed.";
                    return false;
                }

                if (IsProtectedPath(oldPath))
                {
                    error = "This protected path cannot be renamed.";
                    return false;
                }

                var itemAttributes = existsAsFile ? File.GetAttributes(oldPath) : new DirectoryInfo(oldPath).Attributes;
                var isSystemOrHidden = (itemAttributes & FileAttributes.System) == FileAttributes.System
                    || (itemAttributes & FileAttributes.Hidden) == FileAttributes.Hidden;

                if (isSystemOrHidden && IsProtectedPath(oldPath))
                {
                    error = "System-protected items cannot be renamed.";
                    return false;
                }

                var newName = requestNewName.Trim();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    error = "New name is required.";
                    return false;
                }

                if (newName.Contains("..", StringComparison.Ordinal) || newName.Contains('/') || newName.Contains('\\'))
                {
                    error = "New name must be a simple name only.";
                    return false;
                }

                if (!string.Equals(newName, Path.GetFileName(newName), StringComparison.Ordinal))
                {
                    error = "New name must not include path separators.";
                    return false;
                }

                if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    error = "New name contains invalid characters.";
                    return false;
                }

                var parent = Directory.GetParent(oldPath)?.FullName;
                if (string.IsNullOrWhiteSpace(parent))
                {
                    error = "Cannot rename this item.";
                    return false;
                }

                var currentName = Path.GetFileName(oldPath.TrimEnd(Path.DirectorySeparatorChar));
                if (string.Equals(currentName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    error = "New name must be different from current name.";
                    return false;
                }

                newPath = Path.Combine(parent, newName);
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    error = "An item with that name already exists.";
                    return false;
                }

                if (!string.Equals(Path.GetDirectoryName(newPath)?.TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    error = "Rename must stay in the same folder.";
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDriveRootPath(string path)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar);
            return !string.IsNullOrWhiteSpace(root)
                && string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProtectedPath(string path)
        {
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var protectedPaths = GetProtectedPaths();
            return protectedPaths.Contains(normalized);
        }

        private static HashSet<string> GetProtectedPaths()
        {
            var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                        continue;

                    var root = drive.RootDirectory.FullName;
                    protectedPaths.Add(Path.GetFullPath(Path.Combine(root, "Windows")).TrimEnd(Path.DirectorySeparatorChar));
                    protectedPaths.Add(Path.GetFullPath(Path.Combine(root, "Program Files")).TrimEnd(Path.DirectorySeparatorChar));
                    protectedPaths.Add(Path.GetFullPath(Path.Combine(root, "Program Files (x86)")).TrimEnd(Path.DirectorySeparatorChar));
                    protectedPaths.Add(Path.GetFullPath(Path.Combine(root, "ProgramData")).TrimEnd(Path.DirectorySeparatorChar));
                }
                catch
                {
                }
            }

            try
            {
                var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData")
                    .TrimEnd(Path.DirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(appDataRoot))
                    protectedPaths.Add(appDataRoot);
            }
            catch
            {
            }

            return protectedPaths;
        }

        private void PostRenameResult(bool ok, string oldPath, string newPath, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] rename-result ok={ok}");
            PostMessage(new
            {
                type = "file-explorer-rename-result",
                ok,
                oldPath,
                newPath,
                error
            });
        }

        private void HandleCreateFile(JsonElement root)
        {
            var directoryPath = GetString(root, "directoryPath");
            var fileName = GetString(root, "fileName");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=create-file directory={directoryPath}");

            try
            {
                if (!TryBuildCreateTarget(directoryPath, fileName, true, out var targetPath, out var error))
                {
                    PostCreateResult(false, targetPath, "file", error);
                    return;
                }

                File.WriteAllText(targetPath, "Atlas test file created from AI File Explorer.\n", new UTF8Encoding(false));
                PostCreateResult(true, targetPath, "file", null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostCreateResult(false, string.Empty, "file", "Unable to create file.");
            }
        }

        private void HandleCreateFolder(JsonElement root)
        {
            var directoryPath = GetString(root, "directoryPath");
            var folderName = GetString(root, "folderName");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=create-folder directory={directoryPath}");

            try
            {
                if (!TryBuildCreateTarget(directoryPath, folderName, false, out var targetPath, out var error))
                {
                    PostCreateResult(false, targetPath, "folder", error);
                    return;
                }

                Directory.CreateDirectory(targetPath);
                PostCreateResult(true, targetPath, "folder", null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostCreateResult(false, string.Empty, "folder", "Unable to create folder.");
            }
        }

        private void HandleTransferCopy(JsonElement root)
        {
            var jobId = GetString(root, "jobId");
            var sourcePath = GetString(root, "sourcePath");
            var destinationDirectoryPath = GetString(root, "destinationDirectoryPath");

            if (string.IsNullOrWhiteSpace(jobId))
                return;

            _ = Task.Run(() => RunTransferCopyAsync(jobId, sourcePath, destinationDirectoryPath));
        }

        private void HandleTransferMove(JsonElement root)
        {
            var jobId = GetString(root, "jobId");
            var sourcePath = GetString(root, "sourcePath");
            var destinationDirectoryPath = GetString(root, "destinationDirectoryPath");

            if (string.IsNullOrWhiteSpace(jobId))
                return;

            _ = Task.Run(() => RunTransferMoveAsync(jobId, sourcePath, destinationDirectoryPath));
        }

        private async Task RunTransferCopyAsync(string jobId, string sourcePath, string destinationDirectoryPath)
        {
            string fullSourcePath = string.Empty;
            string destinationPath = string.Empty;
            string sourceKind = "unknown";
            long bytesTotal = 0;

            try
            {
                if (!TryBuildSafeCopyTarget(sourcePath, destinationDirectoryPath, out fullSourcePath, out destinationPath, out sourceKind, out var error))
                {
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=false");
                    PostTransferUpdate(jobId, "failed", 0, 0, 0, error);
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer start jobId={jobId} type=copy kind={sourceKind}");
                PostTransferUpdate(jobId, "queued", 0, 0, 0, null);

                if (string.Equals(sourceKind, "file", StringComparison.Ordinal))
                {
                    bytesTotal = new FileInfo(fullSourcePath).Length;
                    PostTransferUpdate(jobId, "running", 10, 0, bytesTotal, null);
                    await CopyFileWithProgressAsync(fullSourcePath, destinationPath, bytesTotal, jobId, 0);
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=true");
                    PostTransferUpdate(jobId, "success", 100, bytesTotal, bytesTotal, null);
                    return;
                }

                if (!TryEvaluateFolderCopyLimits(fullSourcePath, out _, out bytesTotal, out var limitError))
                {
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=false");
                    PostTransferUpdate(jobId, "failed", 0, 0, 0, limitError);
                    return;
                }

                PostTransferUpdate(jobId, "running", 5, 0, bytesTotal, null);
                await CopyDirectoryWithProgressAsync(fullSourcePath, destinationPath, bytesTotal, jobId);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=true");
                PostTransferUpdate(jobId, "success", 100, bytesTotal, bytesTotal, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=false");
                PostTransferUpdate(jobId, "failed", 0, 0, bytesTotal, "Unable to copy this item.");
            }
        }

        private async Task RunTransferMoveAsync(string jobId, string sourcePath, string destinationDirectoryPath)
        {
            string fullSourcePath = string.Empty;
            string destinationPath = string.Empty;
            string sourceKind = "unknown";
            long bytesTotal = 0;

            try
            {
                if (!TryBuildSafeMoveTarget(sourcePath, destinationDirectoryPath, out fullSourcePath, out destinationPath, out sourceKind, out var error))
                {
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=false");
                    PostTransferUpdate(jobId, "failed", 0, 0, 0, error);
                    return;
                }

                bytesTotal = string.Equals(sourceKind, "file", StringComparison.Ordinal)
                    ? new FileInfo(fullSourcePath).Length
                    : EstimateDirectoryBytes(fullSourcePath);

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer start jobId={jobId} type=move kind={sourceKind}");
                PostTransferUpdate(jobId, "queued", 0, 0, bytesTotal, null);
                PostTransferUpdate(jobId, "running", 10, 0, bytesTotal, null);
                await Task.Yield();
                PostTransferUpdate(jobId, "running", 55, bytesTotal > 0 ? Math.Max(1, bytesTotal / 2) : 0, bytesTotal, null);

                if (string.Equals(sourceKind, "file", StringComparison.Ordinal))
                {
                    File.Move(fullSourcePath, destinationPath);
                }
                else
                {
                    Directory.Move(fullSourcePath, destinationPath);
                }

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=true");
                PostTransferUpdate(jobId, "success", 100, bytesTotal, bytesTotal, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer result jobId={jobId} ok=false");
                PostTransferUpdate(jobId, "failed", 0, 0, bytesTotal, "Unable to move this item.");
            }
        }

        private void HandleCopyItem(JsonElement root)
        {
            var sourcePath = GetString(root, "sourcePath");
            var destinationDirectoryPath = GetString(root, "destinationDirectoryPath");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=copy-item source={sourcePath} destination={destinationDirectoryPath}");

            try
            {
                if (!TryBuildSafeCopyTarget(sourcePath, destinationDirectoryPath, out var fullSourcePath, out var destinationPath, out var sourceKind, out var error))
                {
                    PostCopyResult(false, sourcePath, destinationDirectoryPath, sourceKind, error);
                    return;
                }

                if (string.Equals(sourceKind, "file", StringComparison.Ordinal))
                {
                    File.Copy(fullSourcePath, destinationPath, overwrite: false);
                    PostCopyResult(true, fullSourcePath, destinationPath, sourceKind, null);
                    return;
                }

                CopyDirectoryRecursive(fullSourcePath, destinationPath);
                PostCopyResult(true, fullSourcePath, destinationPath, sourceKind, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostCopyResult(false, sourcePath, destinationDirectoryPath, "unknown", "Unable to copy this item.");
            }
        }

        private void HandleMoveItem(JsonElement root)
        {
            var sourcePath = GetString(root, "sourcePath");
            var destinationDirectoryPath = GetString(root, "destinationDirectoryPath");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=move-item source={sourcePath} destination={destinationDirectoryPath}");

            try
            {
                if (!TryBuildSafeMoveTarget(sourcePath, destinationDirectoryPath, out var fullSourcePath, out var destinationPath, out var sourceKind, out var error))
                {
                    PostMoveResult(false, sourcePath, destinationDirectoryPath, sourceKind, error);
                    return;
                }

                if (string.Equals(sourceKind, "file", StringComparison.Ordinal))
                {
                    File.Move(fullSourcePath, destinationPath);
                    PostMoveResult(true, fullSourcePath, destinationPath, sourceKind, null);
                    return;
                }

                Directory.Move(fullSourcePath, destinationPath);
                PostMoveResult(true, fullSourcePath, destinationPath, sourceKind, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostMoveResult(false, sourcePath, destinationDirectoryPath, "unknown", "Unable to move this item.");
            }
        }

        private void HandleDeleteItem(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=delete-item path={requestPath}");

            try
            {
                if (!TryBuildSafeDeleteTarget(requestPath, out var fullPath, out var kind, out var error))
                {
                    PostDeleteResult(false, requestPath, error);
                    return;
                }

                if (string.Equals(kind, "file", StringComparison.Ordinal))
                {
                    FileSystem.DeleteFile(
                        fullPath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);
                    PostDeleteResult(true, fullPath, null);
                    return;
                }

                FileSystem.DeleteDirectory(
                    fullPath,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin);
                PostDeleteResult(true, fullPath, null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostDeleteResult(false, requestPath, "Unable to move this item to Recycle Bin.");
            }
        }

        private static bool TryBuildSafeDeleteTarget(string requestPath, out string fullPath, out string kind, out string error)
        {
            fullPath = string.Empty;
            kind = "unknown";
            error = "Unable to move this item to Recycle Bin.";

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath))
                {
                    error = "Path is required.";
                    return false;
                }

                var trimmed = requestPath.Trim();
                if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    error = "Path contains invalid characters.";
                    return false;
                }

                var segments = trimmed.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
                {
                    error = "Path traversal is not allowed.";
                    return false;
                }

                fullPath = Path.GetFullPath(trimmed);
                var existsAsFile = File.Exists(fullPath);
                var existsAsDirectory = Directory.Exists(fullPath);
                if (!existsAsFile && !existsAsDirectory)
                {
                    error = "Item not found.";
                    return false;
                }

                if (IsDriveRootPath(fullPath))
                {
                    error = "Drive roots cannot be deleted.";
                    return false;
                }

                if (IsUsersRootPath(fullPath))
                {
                    error = "Deleting the Users root is blocked.";
                    return false;
                }

                if (IsDeleteProtectedPathOrChild(fullPath))
                {
                    error = "Deleting protected system paths is blocked.";
                    return false;
                }

                var attributes = existsAsFile
                    ? File.GetAttributes(fullPath)
                    : new DirectoryInfo(fullPath).Attributes;
                if ((attributes & FileAttributes.System) == FileAttributes.System)
                {
                    error = "System-protected items cannot be deleted.";
                    return false;
                }

                kind = existsAsFile ? "file" : "folder";
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied.";
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBuildSafeMoveTarget(
            string requestSourcePath,
            string requestDestinationDirectoryPath,
            out string fullSourcePath,
            out string destinationPath,
            out string sourceKind,
            out string error)
        {
            fullSourcePath = string.Empty;
            destinationPath = string.Empty;
            sourceKind = "unknown";
            error = "Unable to move this item.";

            try
            {
                if (string.IsNullOrWhiteSpace(requestSourcePath))
                {
                    error = "Source path is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(requestDestinationDirectoryPath))
                {
                    error = "Destination folder path is required.";
                    return false;
                }

                fullSourcePath = Path.GetFullPath(requestSourcePath.Trim());
                var sourceIsFile = File.Exists(fullSourcePath);
                var sourceIsFolder = Directory.Exists(fullSourcePath);
                if (!sourceIsFile && !sourceIsFolder)
                {
                    error = "Source item was not found.";
                    return false;
                }

                if (IsDriveRootPath(fullSourcePath))
                {
                    error = "Drive roots cannot be moved.";
                    return false;
                }

                if (IsProtectedPathOrChild(fullSourcePath))
                {
                    error = "Moving protected system paths is blocked.";
                    return false;
                }

                var destinationDirectoryPath = ValidateDirectoryPath(requestDestinationDirectoryPath);
                if (IsDriveRootPath(destinationDirectoryPath) || IsProtectedPathOrChild(destinationDirectoryPath))
                {
                    error = "Moving into this protected directory is blocked.";
                    return false;
                }

                var sourceName = sourceIsFile
                    ? Path.GetFileName(fullSourcePath)
                    : Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar));

                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    error = "Unable to resolve source name.";
                    return false;
                }

                destinationPath = Path.Combine(destinationDirectoryPath, sourceName);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    error = "An item with the same name already exists in destination.";
                    return false;
                }

                if (sourceIsFile)
                {
                    sourceKind = "file";
                    return true;
                }

                sourceKind = "folder";
                if (IsSameOrChildPath(destinationDirectoryPath, fullSourcePath))
                {
                    error = "Cannot move a folder into itself or its child folder.";
                    return false;
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied.";
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                error = "Destination directory not found.";
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProtectedPathOrChild(string path)
        {
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var protectedPath in GetProtectedPaths())
            {
                if (string.Equals(normalized, protectedPath, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (normalized.StartsWith(protectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsUsersRootPath(string path)
        {
            try
            {
                var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
                var root = Path.GetPathRoot(normalized)?.TrimEnd(Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(root))
                    return false;

                var usersRoot = Path.Combine(root + Path.DirectorySeparatorChar, "Users").TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(normalized, usersRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDeleteProtectedPathOrChild(string path)
        {
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

            foreach (var protectedPath in GetProtectedPaths())
            {
                if (string.Equals(normalized, protectedPath, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (normalized.StartsWith(protectedPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryBuildSafeCopyTarget(
            string requestSourcePath,
            string requestDestinationDirectoryPath,
            out string fullSourcePath,
            out string destinationPath,
            out string sourceKind,
            out string error)
        {
            fullSourcePath = string.Empty;
            destinationPath = string.Empty;
            sourceKind = "unknown";
            error = "Unable to copy this item.";

            try
            {
                if (string.IsNullOrWhiteSpace(requestSourcePath))
                {
                    error = "Source path is required.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(requestDestinationDirectoryPath))
                {
                    error = "Destination folder path is required.";
                    return false;
                }

                fullSourcePath = Path.GetFullPath(requestSourcePath.Trim());
                var sourceIsFile = File.Exists(fullSourcePath);
                var sourceIsFolder = Directory.Exists(fullSourcePath);
                if (!sourceIsFile && !sourceIsFolder)
                {
                    error = "Source item was not found.";
                    return false;
                }

                if (IsDriveRootPath(fullSourcePath))
                {
                    error = "Drive roots cannot be copied.";
                    return false;
                }

                var destinationDirectoryPath = ValidateDirectoryPath(requestDestinationDirectoryPath);
                if (IsDriveRootPath(destinationDirectoryPath) || IsProtectedPath(destinationDirectoryPath))
                {
                    error = "Copying to this protected directory is blocked.";
                    return false;
                }

                var sourceName = sourceIsFile
                    ? Path.GetFileName(fullSourcePath)
                    : Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar));

                if (string.IsNullOrWhiteSpace(sourceName))
                {
                    error = "Unable to resolve source name.";
                    return false;
                }

                destinationPath = Path.Combine(destinationDirectoryPath, sourceName);
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    error = "An item with the same name already exists in destination.";
                    return false;
                }

                if (sourceIsFile)
                {
                    sourceKind = "file";
                    var fileInfo = new FileInfo(fullSourcePath);
                    if (fileInfo.Length > MaxCopyFileBytes)
                    {
                        error = "File exceeds the 500 MB copy limit.";
                        return false;
                    }

                    return true;
                }

                sourceKind = "folder";

                if (IsSameOrChildPath(destinationDirectoryPath, fullSourcePath))
                {
                    error = "Cannot copy a folder into itself or its child folder.";
                    return false;
                }

                if (!TryEvaluateFolderCopyLimits(fullSourcePath, out var itemCount, out var totalSizeBytes, out error))
                {
                    return false;
                }

                if (itemCount > MaxCopyFolderItemCount)
                {
                    error = "Folder exceeds max item limit (1000).";
                    return false;
                }

                if (totalSizeBytes > MaxCopyTotalBytes)
                {
                    error = "Folder exceeds max size limit (500 MB).";
                    return false;
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied.";
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                error = "Destination directory not found.";
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryEvaluateFolderCopyLimits(string folderPath, out int itemCount, out long totalSizeBytes, out string error)
        {
            itemCount = 0;
            totalSizeBytes = 0;
            error = "Unable to evaluate folder size.";

            try
            {
                var pending = new Queue<string>();
                pending.Enqueue(folderPath);

                while (pending.Count > 0)
                {
                    var current = pending.Dequeue();

                    foreach (var subDir in Directory.EnumerateDirectories(current))
                    {
                        itemCount += 1;
                        if (itemCount > MaxCopyFolderItemCount)
                        {
                            error = "Folder exceeds max item limit (1000).";
                            return false;
                        }

                        pending.Enqueue(subDir);
                    }

                    foreach (var filePath in Directory.EnumerateFiles(current))
                    {
                        itemCount += 1;
                        if (itemCount > MaxCopyFolderItemCount)
                        {
                            error = "Folder exceeds max item limit (1000).";
                            return false;
                        }

                        var info = new FileInfo(filePath);
                        totalSizeBytes += info.Length;
                        if (totalSizeBytes > MaxCopyTotalBytes)
                        {
                            error = "Folder exceeds max size limit (500 MB).";
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied while scanning folder.";
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                error = "Source folder not found.";
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void CopyDirectoryRecursive(string sourceDirectoryPath, string destinationDirectoryPath)
        {
            Directory.CreateDirectory(destinationDirectoryPath);

            foreach (var filePath in Directory.EnumerateFiles(sourceDirectoryPath))
            {
                var fileName = Path.GetFileName(filePath);
                var targetFilePath = Path.Combine(destinationDirectoryPath, fileName);
                File.Copy(filePath, targetFilePath, overwrite: false);
            }

            foreach (var subDirectoryPath in Directory.EnumerateDirectories(sourceDirectoryPath))
            {
                var folderName = Path.GetFileName(subDirectoryPath.TrimEnd(Path.DirectorySeparatorChar));
                var targetSubDirectoryPath = Path.Combine(destinationDirectoryPath, folderName);
                CopyDirectoryRecursive(subDirectoryPath, targetSubDirectoryPath);
            }
        }

        private async Task CopyDirectoryWithProgressAsync(string sourceDirectoryPath, string destinationDirectoryPath, long bytesTotal, string jobId)
        {
            Directory.CreateDirectory(destinationDirectoryPath);

            var directories = Directory.EnumerateDirectories(sourceDirectoryPath, "*", System.IO.SearchOption.AllDirectories).ToArray();
            foreach (var directory in directories)
            {
                var relativePath = Path.GetRelativePath(sourceDirectoryPath, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectoryPath, relativePath));
            }

            long bytesDone = 0;
            var filePaths = Directory.EnumerateFiles(sourceDirectoryPath, "*", System.IO.SearchOption.AllDirectories).ToArray();
            foreach (var filePath in filePaths)
            {
                var relativePath = Path.GetRelativePath(sourceDirectoryPath, filePath);
                var targetPath = Path.Combine(destinationDirectoryPath, relativePath);
                var parentDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }

                var fileSize = new FileInfo(filePath).Length;
                bytesDone = await CopyFileWithProgressAsync(filePath, targetPath, bytesTotal, jobId, bytesDone);
                if (fileSize == 0)
                {
                    var percent = bytesTotal <= 0 ? 90 : Math.Min(99, (int)Math.Round((double)bytesDone / bytesTotal * 100));
                    PostTransferUpdate(jobId, "running", percent, bytesDone, bytesTotal, null);
                }
            }
        }

        private async Task<long> CopyFileWithProgressAsync(string sourcePath, string destinationPath, long bytesTotal, string jobId, long baseBytesDone)
        {
            long bytesDone = baseBytesDone;

            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, TransferBufferBytes, useAsync: true);
            await using var destinationStream = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, TransferBufferBytes, useAsync: true);

            var buffer = new byte[TransferBufferBytes];
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destinationStream.WriteAsync(buffer, 0, bytesRead);
                bytesDone += bytesRead;
                var percent = bytesTotal <= 0 ? 50 : Math.Min(99, Math.Max(10, (int)Math.Round((double)bytesDone / bytesTotal * 100)));
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer progress jobId={jobId} percent={percent}");
                PostTransferUpdate(jobId, "running", percent, bytesDone, bytesTotal, null);
            }

            return bytesDone;
        }

        private static long EstimateDirectoryBytes(string directoryPath)
        {
            try
            {
                return Directory.EnumerateFiles(directoryPath, "*", System.IO.SearchOption.AllDirectories)
                    .Select(filePath =>
                    {
                        try { return new FileInfo(filePath).Length; }
                        catch { return 0L; }
                    })
                    .Sum();
            }
            catch
            {
                return 0;
            }
        }

        private void PostTransferUpdate(string jobId, string status, int progressPercent, long bytesDone, long bytesTotal, string? error)
        {
            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] transfer progress jobId={jobId} percent={progressPercent}");
            }

            PostMessage(new
            {
                type = "file-explorer-transfer-update",
                jobId,
                status,
                progressPercent,
                bytesDone,
                bytesTotal,
                error
            });
        }

        private static string ResolveWorkspaceRootPath()
        {
            try
            {
                return Path.GetFullPath(Path.Combine(ResolveDistPath(), "..", "..", ".."));
            }
            catch
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private static bool IsSameOrChildPath(string candidatePath, string parentPath)
        {
            var candidateNormalized = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar);
            var parentNormalized = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar);

            if (string.Equals(candidateNormalized, parentNormalized, StringComparison.OrdinalIgnoreCase))
                return true;

            return candidateNormalized.StartsWith(parentNormalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private void PostCopyResult(bool ok, string sourcePath, string destinationPath, string kind, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] copy-result ok={ok} kind={kind}");
            PostMessage(new
            {
                type = "file-explorer-copy-result",
                ok,
                sourcePath,
                destinationPath,
                error
            });
        }

        private void PostMoveResult(bool ok, string sourcePath, string destinationPath, string kind, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] move-result ok={ok} kind={kind}");
            PostMessage(new
            {
                type = "file-explorer-move-result",
                ok,
                sourcePath,
                destinationPath,
                error
            });
        }

        private void PostDeleteResult(bool ok, string path, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] delete-result ok={ok}");
            PostMessage(new
            {
                type = "file-explorer-delete-result",
                ok,
                path,
                error
            });
        }

        private static bool TryBuildCreateTarget(string directoryPath, string rawName, bool isFile, out string targetPath, out string error)
        {
            targetPath = string.Empty;
            error = isFile ? "Unable to create file." : "Unable to create folder.";

            try
            {
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    error = "Folder path is required.";
                    return false;
                }

                var fullDirectoryPath = ValidateDirectoryPath(directoryPath);
                if (IsDriveRootPath(fullDirectoryPath) || IsProtectedPath(fullDirectoryPath))
                {
                    error = "Creating items in this protected directory is blocked.";
                    return false;
                }

                var cleanedName = rawName.Trim();
                if (string.IsNullOrWhiteSpace(cleanedName))
                {
                    error = isFile ? "File name is required." : "Folder name is required.";
                    return false;
                }

                if (cleanedName.Contains("..", StringComparison.Ordinal) || cleanedName.Contains('/') || cleanedName.Contains('\\'))
                {
                    error = "Name must be a simple name only.";
                    return false;
                }

                if (!string.Equals(Path.GetFileName(cleanedName), cleanedName, StringComparison.Ordinal))
                {
                    error = "Name must not include folder separators.";
                    return false;
                }

                if (cleanedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    error = "Name contains invalid characters.";
                    return false;
                }

                if (isFile)
                {
                    var extension = Path.GetExtension(cleanedName);
                    if (string.IsNullOrWhiteSpace(extension))
                    {
                        cleanedName += ".txt";
                        extension = ".txt";
                    }

                    if (!AllowedCreateFileExtensions.Contains(extension))
                    {
                        error = "Only .txt, .md, and .json files can be created.";
                        return false;
                    }
                }

                targetPath = Path.Combine(fullDirectoryPath, cleanedName);
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    error = "An item with that name already exists.";
                    return false;
                }

                var parent = Path.GetDirectoryName(targetPath);
                if (!string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar), fullDirectoryPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    error = "New items must stay in the current directory.";
                    return false;
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied for this directory.";
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                error = "Directory not found.";
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void PostCreateResult(bool ok, string path, string kind, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] create-result ok={ok} kind={kind}");
            PostMessage(new
            {
                type = "file-explorer-create-result",
                ok,
                path,
                kind,
                error
            });
        }

        private static bool IsBlockedBySecretFilename(string fileName)
        {
            var lower = fileName.ToLowerInvariant();
            var secretTerms = new[] { "secret", "token", "password", "private", "credential", "api-key", "apikey" };
            return secretTerms.Any(term => lower.Contains(term));
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024)):F1} MB";
            return $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB";
        }

        private void PostDirectoryError(string requestPath, string message, string? requestId = null)
        {
            var normalized = NormalizePathOrNull(requestPath);
            var responsePath = string.IsNullOrWhiteSpace(normalized) ? requestPath : normalized;
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] list-directory error path={responsePath} requestId={requestId} error={message}");
            PostMessage(new
            {
                type = "file-explorer-directory-error",
                requestId,
                path = responsePath,
                directoryPath = responsePath,
                error = message
            });
        }

        private void PostPreview(string path, bool ok, string previewKind, string? text, string? url, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] preview-result kind={previewKind} ok={ok}");
            if (!string.IsNullOrWhiteSpace(error))
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] error={error}");

            PostMessage(new
            {
                type = "file-explorer-preview",
                ok,
                path,
                previewKind,
                text,
                url,
                error
            });
        }

        private static string ValidateDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.");

            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("Path contains invalid characters.");

            var fullPath = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException("Directory does not exist.");

            return fullPath;
        }

        private static string ValidateFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.");

            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("Path contains invalid characters.");

            var fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("File does not exist.");

            return fullPath;
        }

        private static bool CanPreviewFile(string fullPath, string extension)
        {
            if (IsBlockedByPolicy(Path.GetFileName(fullPath), extension))
                return false;

            return AllowedTextPreviewExtensions.Contains(extension) || AllowedImagePreviewExtensions.Contains(extension);
        }

        private static bool IsBlockedByPolicy(string fileName, string extension)
        {
            if (BlockedPreviewExtensions.Contains(extension))
                return true;

            if (string.Equals(fileName, ".env", StringComparison.OrdinalIgnoreCase))
                return true;

            return fileName.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PostMessage(object payload)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(() => PostMessage(payload));
                    return;
                }

                var core = FileExplorerWebView?.CoreWebView2;
                if (core == null)
                    return;

                var json = JsonSerializer.Serialize(payload);
                core.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private static string GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
                return string.Empty;

            return (element.GetString() ?? string.Empty).Trim();
        }

        private static void AddKnownRoot(List<object> roots, string id, string label, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var normalized = Path.GetFullPath(path);
                if (!Directory.Exists(normalized))
                    return;

                roots.Add(new
                {
                    id,
                    label,
                    path = normalized,
                    group = "known"
                });
            }
            catch
            {
            }
        }

        private static string ResolveDownloadsPath()
        {
            try
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(profile))
                    return string.Empty;

                var downloads = Path.Combine(profile, "Downloads");
                return Directory.Exists(downloads) ? downloads : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string? NormalizePathOrNull(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return null;
            }
        }

        // ─── ATLAS BRAIN: PROJECT SCAN ─────────────────────────────────

        private void HandleAtlasBrainProjectScan(JsonElement root)
        {
            var requestPath = GetString(root, "path");

            try
            {
                var fullPath = ValidateDirectoryPath(requestPath);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] atlas-brain-project-scan start path={fullPath} maxDepth={MaxAtlasBrainScanDepth} maxEntries={MaxAtlasBrainScanEntries}");

                var entries = ScanAtlasBrainEntries(fullPath, MaxAtlasBrainScanDepth, MaxAtlasBrainScanEntries);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] atlas-brain-project-scan result ok=true entries={entries.Count}");
                PostMessage(new
                {
                    type = "file-explorer-atlas-brain-project-result",
                    ok = true,
                    path = fullPath,
                    entries
                });
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-project-scan result ok=false entries=0");
                PostMessage(new { type = "file-explorer-atlas-brain-project-result", ok = false, path = requestPath, error = "Access denied for this folder." });
            }
            catch (DirectoryNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-project-scan result ok=false entries=0");
                PostMessage(new { type = "file-explorer-atlas-brain-project-result", ok = false, path = requestPath, error = "Folder not found." });
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-project-scan result ok=false entries=0");
                PostMessage(new { type = "file-explorer-atlas-brain-project-result", ok = false, path = requestPath, error = "Unable to scan project metadata." });
            }
        }

        private static List<object> ScanAtlasBrainEntries(string rootPath, int maxDepth, int maxEntries)
        {
            var entries = new List<object>(maxEntries);
            var queue = new Queue<(string path, int depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0 && entries.Count < maxEntries)
            {
                var (current, depth) = queue.Dequeue();
                if (depth >= maxDepth)
                    continue;

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(current).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        if (entries.Count >= maxEntries)
                            break;

                        try
                        {
                            var info = new DirectoryInfo(dir);
                            entries.Add(new
                            {
                                name = info.Name,
                                path = info.FullName,
                                kind = "folder",
                                extension = string.Empty,
                                sizeBytes = (long?)null,
                                modifiedUtc = info.LastWriteTimeUtc,
                                depth = depth + 1
                            });

                            if (depth + 1 < maxDepth)
                                queue.Enqueue((info.FullName, depth + 1));
                        }
                        catch
                        {
                        }
                    }

                    foreach (var file in Directory.EnumerateFiles(current).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        if (entries.Count >= maxEntries)
                            break;

                        try
                        {
                            var info = new FileInfo(file);
                            entries.Add(new
                            {
                                name = info.Name,
                                path = info.FullName,
                                kind = "file",
                                extension = info.Extension ?? string.Empty,
                                sizeBytes = info.Length,
                                modifiedUtc = info.LastWriteTimeUtc,
                                depth = depth + 1
                            });
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            return entries;
        }

        // ─── ATLAS BRAIN: ACTION PLAN ───────────────────────────────────

        private void HandleAtlasBrainActionPlan(JsonElement root)
        {
            var requestPath = GetString(root, "path");

            if (!root.TryGetProperty("metadata", out var metadataElement)
                || (metadataElement.ValueKind != JsonValueKind.Object && metadataElement.ValueKind != JsonValueKind.Array))
            {
                PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = false, path = requestPath, error = "Metadata payload is required." });
                return;
            }

            try
            {
                var fullPath = ValidateDirectoryPath(requestPath);
                var itemCount = GetAtlasBrainItemCount(metadataElement);
                var metadataJson = metadataElement.GetRawText();
                if (metadataJson.Length > 45000)
                    metadataJson = metadataJson[..45000];

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] atlas-brain-action-plan start path={fullPath} items={itemCount}");
                CallAtlasBrainActionPlanAsync(fullPath, metadataJson);
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-action-plan result ok=false provider=");
                PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = false, path = requestPath, error = "Access denied for this folder.", provider = (string?)null });
            }
            catch (DirectoryNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-action-plan result ok=false provider=");
                PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = false, path = requestPath, error = "Folder not found.", provider = (string?)null });
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-action-plan result ok=false provider=");
                PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = false, path = requestPath, error = "Unable to generate action plan.", provider = (string?)null });
            }
        }

        private static int GetAtlasBrainItemCount(JsonElement metadataElement)
        {
            try
            {
                if (metadataElement.ValueKind == JsonValueKind.Object)
                {
                    if (metadataElement.TryGetProperty("itemCount", out var itemCountElement)
                        && itemCountElement.ValueKind == JsonValueKind.Number
                        && itemCountElement.TryGetInt32(out var countFromProperty)
                        && countFromProperty >= 0)
                    {
                        return countFromProperty;
                    }

                    if (metadataElement.TryGetProperty("topItems", out var topItemsElement)
                        && topItemsElement.ValueKind == JsonValueKind.Array)
                    {
                        return topItemsElement.GetArrayLength();
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private async void CallAtlasBrainActionPlanAsync(string folderPath, string metadataJson)
        {
            try
            {
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? folderPath;

                var promptMessages = new List<object>
                {
                    new
                    {
                        role = "system",
                        content = "You are Atlas File Brain. Use ONLY provided metadata. Do not claim file contents were read. Do not recommend destructive actions without explicit review. Return concise sections only:\n1. What this folder is\n2. What matters\n3. What to review\n4. What to clean/archive\n5. What not to touch\n6. 5 safe next actions\nKeep total response under 800 words."
                    },
                    new
                    {
                        role = "user",
                        content = $"Folder: {folderName}\nMetadata-only payload:\n{metadataJson}"
                    }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer", promptMessages, 1300, System.Threading.CancellationToken.None);

                if (response?.Success == true)
                {
                    var plan = response.Content ?? "[No plan returned]";
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] atlas-brain-action-plan result ok=true provider={providerName}");
                    PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = true, path = folderPath, plan, error = (string?)null, provider = providerName });
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable.";
                    System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-action-plan result ok=false provider=");
                    PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = false, path = folderPath, plan = (string?)null, error, provider = (string?)null });
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-action-plan result ok=false provider=");
                PostMessage(new { type = "file-explorer-atlas-brain-action-plan-result", ok = false, path = folderPath, plan = (string?)null, error = "AI service error.", provider = (string?)null });
            }
        }

        // ─── ATLAS BRAIN: ORGANIZE PLAN ─────────────────────────────────

        private void HandleAtlasBrainOrganizePlan(JsonElement root)
        {
            var requestPath = GetString(root, "path");

            if (!root.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = requestPath, planJson = (string?)null, error = "Items payload is required.", provider = (string?)null });
                return;
            }

            try
            {
                var fullPath = ValidateDirectoryPath(requestPath);
                var itemCount = itemsElement.GetArrayLength();
                var itemsJson = itemsElement.GetRawText();
                if (itemsJson.Length > 60000)
                    itemsJson = itemsJson[..60000];

                var instruction = GetString(root, "instruction");
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] atlas-brain-organize-plan start path={fullPath} items={itemCount}");
                CallAtlasBrainOrganizePlanAsync(fullPath, itemsJson, instruction);
            }
            catch (UnauthorizedAccessException)
            {
                PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = requestPath, planJson = (string?)null, error = "Access denied for this folder.", provider = (string?)null });
            }
            catch (DirectoryNotFoundException)
            {
                PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = requestPath, planJson = (string?)null, error = "Folder not found.", provider = (string?)null });
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = requestPath, planJson = (string?)null, error = "Unable to generate organize plan.", provider = (string?)null });
            }
        }

        private async void CallAtlasBrainOrganizePlanAsync(string folderPath, string itemsJson, string instruction)
        {
            try
            {
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? folderPath;
                var instructionText = string.IsNullOrWhiteSpace(instruction) ? "none" : instruction.Trim();

                var promptMessages = new List<object>
                {
                    new
                    {
                        role = "system",
                        content = "You are Atlas File Organizer. Use ONLY provided metadata. Never claim file contents were read. Return minified JSON only (no markdown, no prose) with keys: summary (string), confidence (low|medium|high), groups (array of {name, reason, folderName, items}), renameSuggestions (array of {item, suggestedName, reason}), warnings (array of string), actions (array of {type, sourceName, targetFolder, newName, reason, group, reviewOnly}). Allowed action type values: create-folder, move, rename. No delete actions. Max 20 actions. Prefer groups over actions. Use reviewOnly=true for sensitive/large/unknown items. Do not move every file."
                    },
                    new
                    {
                        role = "user",
                        content = $"Folder: {folderName}\nInstruction: {instructionText}\nCurrent folder metadata items (no file contents):\n{itemsJson}"
                    }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer", promptMessages, 1300, System.Threading.CancellationToken.None);

                if (response?.Success == true)
                {
                    var rawResponse = (response.Content ?? string.Empty).Trim();
                    var providerName = response.Provider.ToString();
                    if (string.IsNullOrWhiteSpace(rawResponse))
                    {
                        PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = folderPath, planJson = (string?)null, error = "AI provider returned an empty plan.", provider = (string?)null });
                        return;
                    }

                    if (!TryNormalizeOrganizePlanJson(rawResponse, out var planJson, out var wasRepaired))
                    {
                        PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = folderPath, planJson = (string?)null, error = "AI returned invalid organize JSON.", provider = providerName });
                        return;
                    }

                    if (TryApplyOrganizePlanSafetyCaps(planJson, out var cappedJson, out var wasTrimmed))
                    {
                        planJson = cappedJson;
                        if (wasTrimmed)
                            wasRepaired = true;
                    }

                    var providerLabel = wasRepaired ? $"{providerName} repaired" : providerName;

                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] atlas-brain-organize-plan result ok=true provider={providerLabel}");
                    PostMessage(new { type = "atlas-brain-organize-plan-result", ok = true, path = folderPath, planJson, error = (string?)null, provider = providerLabel, repaired = wasRepaired });
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable.";
                    System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-organize-plan result ok=false provider=");
                    PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = folderPath, planJson = (string?)null, error, provider = (string?)null });
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] atlas-brain-organize-plan result ok=false provider=");
                PostMessage(new { type = "atlas-brain-organize-plan-result", ok = false, path = folderPath, planJson = (string?)null, error = "AI service error.", provider = (string?)null });
            }
        }

        private static bool TryNormalizeOrganizePlanJson(string rawResponse, out string normalizedJson, out bool wasRepaired)
        {
            normalizedJson = string.Empty;
            wasRepaired = false;

            var trimmed = (rawResponse ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return false;

            if (TryParseJsonObject(trimmed, out normalizedJson))
                return true;

            var withoutFences = StripMarkdownCodeFence(trimmed);
            if (!string.Equals(withoutFences, trimmed, StringComparison.Ordinal) && TryParseJsonObject(withoutFences, out normalizedJson))
            {
                wasRepaired = true;
                return true;
            }

            if (TryExtractFirstJsonObject(withoutFences, out var extracted) && TryParseJsonObject(extracted, out normalizedJson))
            {
                wasRepaired = true;
                return true;
            }

            if (TryExtractFirstJsonObject(trimmed, out extracted) && TryParseJsonObject(extracted, out normalizedJson))
            {
                wasRepaired = true;
                return true;
            }

            return false;
        }

        private static bool TryParseJsonObject(string input, out string normalizedJson)
        {
            normalizedJson = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(input);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                normalizedJson = doc.RootElement.GetRawText();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyOrganizePlanSafetyCaps(string normalizedJson, out string cappedJson, out bool wasTrimmed)
        {
            cappedJson = normalizedJson;
            wasTrimmed = false;

            try
            {
                var rootNode = JsonNode.Parse(normalizedJson) as JsonObject;
                if (rootNode == null)
                    return false;

                var actions = rootNode["actions"] as JsonArray;
                if (actions == null || actions.Count <= 40)
                    return true;

                var trimmed = new JsonArray();
                for (var i = 0; i < Math.Min(20, actions.Count); i++)
                {
                    trimmed.Add(actions[i]?.DeepClone());
                }
                rootNode["actions"] = trimmed;

                var warnings = rootNode["warnings"] as JsonArray ?? new JsonArray();
                warnings.Add($"Action list trimmed to 20 from {actions.Count} for safe review.");
                rootNode["warnings"] = warnings;

                cappedJson = rootNode.ToJsonString();
                wasTrimmed = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string StripMarkdownCodeFence(string input)
        {
            var trimmed = (input ?? string.Empty).Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
                return trimmed;

            var lines = trimmed.Replace("\r\n", "\n").Split('\n');
            if (lines.Length >= 2 && lines[0].TrimStart().StartsWith("```", StringComparison.Ordinal) && lines[^1].Trim().StartsWith("```", StringComparison.Ordinal))
            {
                return string.Join("\n", lines.Skip(1).Take(lines.Length - 2)).Trim();
            }

            return trimmed.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```", string.Empty, StringComparison.Ordinal).Trim();
        }

        private static bool TryExtractFirstJsonObject(string input, out string jsonObject)
        {
            jsonObject = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var start = input.IndexOf('{');
            if (start < 0)
                return false;

            var depth = 0;
            var inString = false;
            var escaping = false;

            for (var i = start; i < input.Length; i++)
            {
                var ch = input[i];

                if (inString)
                {
                    if (escaping)
                    {
                        escaping = false;
                        continue;
                    }

                    if (ch == '\\')
                    {
                        escaping = true;
                        continue;
                    }

                    if (ch == '"')
                        inString = false;

                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        jsonObject = input.Substring(start, i - start + 1).Trim();
                        return true;
                    }
                }
            }

            return false;
        }

        // ─── AI FOLDER BRIEF ─────────────────────────────────────────────

        private void HandleAIFolderBrief(JsonElement root)
        {
            var requestPath = GetString(root, "path");

            try
            {
                var fullPath = ValidateDirectoryPath(requestPath);
                var metadata = BuildFolderMetadataText(fullPath, 300, out var itemCount);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=ai-folder-brief path={fullPath} items={itemCount}");
                CallAIFolderBriefAsync(fullPath, metadata, itemCount);
            }
            catch (UnauthorizedAccessException)
            {
                PostFolderBriefResult(requestPath, false, null, "Access denied for this folder.", null);
            }
            catch (DirectoryNotFoundException)
            {
                PostFolderBriefResult(requestPath, false, null, "Folder not found.", null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostFolderBriefResult(requestPath, false, null, "Unable to create folder brief.", null);
            }
        }

        private async void CallAIFolderBriefAsync(string folderPath, string metadataText, int itemCount)
        {
            try
            {
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? folderPath;

                var promptMessages = new List<object>
                {
                    new { role = "system", content = "You are a practical file management assistant. Analyze this folder using ONLY the provided item metadata (names, types, sizes, dates). You have NOT read any file contents. Return:\n1. What this folder appears to contain\n2. Important or recently modified items\n3. Cleanup opportunities (old files, large items, duplicates)\n4. Files worth reviewing\n5. Suggested next action\n\nKeep the response under 450 tokens. Be specific, honest, and practical." },
                    new { role = "user", content = $"Folder: {folderName}\nTotal items analyzed: {itemCount}\n\n=== METADATA ONLY (no file contents were read) ===\n{metadataText}" }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer", promptMessages, 650, System.Threading.CancellationToken.None);

                if (response?.Success == true)
                {
                    var brief = response.Content ?? "[No brief returned]";
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-brief result ok=true provider={providerName}");
                    PostFolderBriefResult(folderPath, true, brief, null, providerName);
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable or returned no response.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-brief result ok=false error=provider_error");
                    PostFolderBriefResult(folderPath, false, null, error, null);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostFolderBriefResult(folderPath, false, null, "AI service error.", null);
            }
        }

        private void PostFolderBriefResult(string path, bool ok, string? brief, string? error, string? provider)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-brief result ok={ok} provider={provider ?? "none"}");
            PostMessage(new { type = "file-explorer-folder-brief-result", ok, path, brief, error, provider });
        }

        // ─── AI FOLDER QUESTION ──────────────────────────────────────────

        private void HandleAIFolderQuestion(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            var question = GetString(root, "question");

            if (string.IsNullOrWhiteSpace(question))
            {
                PostFolderQuestionResult(requestPath, false, null, "Question is required.", null);
                return;
            }

            if (question.Length > 500)
                question = question[..500];

            try
            {
                var fullPath = ValidateDirectoryPath(requestPath);
                var metadata = BuildFolderMetadataText(fullPath, 300, out var itemCount);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=ai-folder-question path={fullPath} items={itemCount}");
                CallAIFolderQuestionAsync(fullPath, metadata, itemCount, question);
            }
            catch (UnauthorizedAccessException)
            {
                PostFolderQuestionResult(requestPath, false, null, "Access denied for this folder.", null);
            }
            catch (DirectoryNotFoundException)
            {
                PostFolderQuestionResult(requestPath, false, null, "Folder not found.", null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostFolderQuestionResult(requestPath, false, null, "Unable to answer folder question.", null);
            }
        }

        private async void CallAIFolderQuestionAsync(string folderPath, string metadataText, int itemCount, string question)
        {
            try
            {
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? folderPath;

                var promptMessages = new List<object>
                {
                    new { role = "system", content = "You are a practical file management assistant. Answer the user's question about this folder using ONLY the provided metadata (names, types, sizes, dates). You have NOT read any file contents — say so if relevant. Be honest about what you can and cannot determine from metadata alone. Keep the response under 350 tokens." },
                    new { role = "user", content = $"Folder: {folderName}\nTotal items: {itemCount}\n\n=== METADATA ONLY (no file contents were read) ===\n{metadataText}\n\n=== USER QUESTION ===\n{question}" }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer", promptMessages, 500, System.Threading.CancellationToken.None);

                if (response?.Success == true)
                {
                    var answer = response.Content ?? "[No answer returned]";
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-question result ok=true provider={providerName}");
                    PostFolderQuestionResult(folderPath, true, answer, null, providerName);
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-question result ok=false error=provider_error");
                    PostFolderQuestionResult(folderPath, false, null, error, null);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostFolderQuestionResult(folderPath, false, null, "AI service error.", null);
            }
        }

        private void PostFolderQuestionResult(string path, bool ok, string? answer, string? error, string? provider)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-question result ok={ok} provider={provider ?? "none"}");
            PostMessage(new { type = "file-explorer-folder-question-result", ok, path, answer, error, provider });
        }

        // ─── AI FOLDER ACTION SUGGESTIONS ───────────────────────────────

        private void HandleAIFolderActions(JsonElement root)
        {
            var requestPath = GetString(root, "path");

            try
            {
                var fullPath = ValidateDirectoryPath(requestPath);
                var metadata = BuildFolderMetadataText(fullPath, 300, out var itemCount);
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=ai-folder-actions path={fullPath} items={itemCount}");
                CallAIFolderActionsAsync(fullPath, metadata, itemCount);
            }
            catch (UnauthorizedAccessException)
            {
                PostFolderActionsResult(requestPath, false, null, "Access denied for this folder.", null);
            }
            catch (DirectoryNotFoundException)
            {
                PostFolderActionsResult(requestPath, false, null, "Folder not found.", null);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostFolderActionsResult(requestPath, false, null, "Unable to suggest actions for this folder.", null);
            }
        }

        private async void CallAIFolderActionsAsync(string folderPath, string metadataText, int itemCount)
        {
            try
            {
                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? folderPath;

                var promptMessages = new List<object>
                {
                    new { role = "system", content = "You are a practical file-management assistant. Suggest practical actions for this folder using ONLY metadata (names, types, sizes, dates). Do NOT claim you read file contents. Return at most 5 actions. For each action include: action title, why, safest next step. Keep it concise and honest. Avoid malware claims; use words like review/check/potential where uncertain." },
                    new { role = "user", content = $"Folder: {folderName}\nTotal items analyzed: {itemCount}\n\n=== METADATA ONLY (no file contents were read) ===\n{metadataText}" }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer", promptMessages, 600, System.Threading.CancellationToken.None);

                if (response?.Success == true)
                {
                    var actions = response.Content ?? "[No actions returned]";
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-actions result ok=true provider={providerName}");
                    PostFolderActionsResult(folderPath, true, actions, null, providerName);
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable.";
                    System.Diagnostics.Debug.WriteLine("[FileExplorerHost] ai-folder-actions result ok=false error=provider_error");
                    PostFolderActionsResult(folderPath, false, null, error, null);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostFolderActionsResult(folderPath, false, null, "AI service error.", null);
            }
        }

        private void PostFolderActionsResult(string path, bool ok, string? actions, string? error, string? provider)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-folder-actions result ok={ok} provider={provider ?? "none"}");
            PostMessage(new { type = "file-explorer-folder-actions-result", ok, path, actions, error, provider });
        }

        // ─── AI SMART RENAME ─────────────────────────────────────────────

        private void HandleAISmartRename(JsonElement root)
        {
            var requestPath = GetString(root, "path");
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=ai-smart-rename path={requestPath}");

            var previewText = string.Empty;
            if (root.TryGetProperty("previewText", out var previewEl) && previewEl.ValueKind == JsonValueKind.String)
            {
                previewText = previewEl.GetString() ?? string.Empty;
                if (previewText.Length > 20480)
                    previewText = previewText[..20480];
            }

            try
            {
                if (string.IsNullOrWhiteSpace(requestPath) || requestPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    PostSmartRenameResult(requestPath, false, null, "Invalid path.", null);
                    return;
                }

                var fullPath = Path.GetFullPath(requestPath.Trim());
                var isFile = File.Exists(fullPath);
                var isDir = Directory.Exists(fullPath);

                if (!isFile && !isDir)
                {
                    PostSmartRenameResult(requestPath, false, null, "Item not found.", null);
                    return;
                }

                CallAISmartRenameAsync(fullPath, isFile, previewText);
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostSmartRenameResult(requestPath, false, null, "Unable to get rename suggestions.", null);
            }
        }

        private async void CallAISmartRenameAsync(string fullPath, bool isFile, string previewText)
        {
            try
            {
                var name = isFile ? Path.GetFileName(fullPath) : Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
                var extension = isFile ? (Path.GetExtension(fullPath) ?? string.Empty).ToLowerInvariant() : string.Empty;
                var kind = isFile ? "file" : "folder";
                var folderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                var modifiedUtc = isFile ? new FileInfo(fullPath).LastWriteTimeUtc : new DirectoryInfo(fullPath).LastWriteTimeUtc;
                var sizeText = isFile ? FormatBytes(new FileInfo(fullPath).Length) : string.Empty;

                var contentSection = string.IsNullOrWhiteSpace(previewText)
                    ? "No file content available (metadata only)."
                    : $"File preview (first 20 KB only):\n{previewText}";

                var promptMessages = new List<object>
                {
                    new { role = "system", content = $"Suggest 5 clearer, more descriptive {kind} names for this item. Rules: preserve the extension for files; avoid unsafe characters (<>:\"/\\|?*); avoid leading/trailing spaces; use descriptive words; prefer lowercase-with-hyphens or PascalCase. Return EXACTLY 5 suggestions in this format (one per line, nothing else before or after):\n1. suggested-name{extension} | reason why\n2. another-name{extension} | reason why\n3. third-name{extension} | reason why\n4. fourth-name{extension} | reason why\n5. fifth-name{extension} | reason why" },
                    new { role = "user", content = $"Item: {name}\nKind: {kind}\nExtension: {extension}\nSize: {sizeText}\nFolder: {folderPath}\nModified: {modifiedUtc:yyyy-MM-dd}\n\n{contentSection}" }
                };

                var response = await AtlasAI.AI.AIManager.SendMessageAsync(
                    "FileExplorer", promptMessages, 600, System.Threading.CancellationToken.None);

                if (response?.Success == true)
                {
                    var suggestions = response.Content ?? string.Empty;
                    var providerName = response.Provider.ToString();
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-smart-rename result ok=true provider={providerName}");
                    PostSmartRenameResult(fullPath, true, suggestions, null, providerName);
                }
                else
                {
                    var error = response?.Error ?? "AI provider unavailable.";
                    System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-smart-rename result ok=false error=provider_error");
                    PostSmartRenameResult(fullPath, false, null, error, null);
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                PostSmartRenameResult(fullPath, false, null, "AI service error.", null);
            }
        }

        private void PostSmartRenameResult(string path, bool ok, string? suggestions, string? error, string? provider)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] ai-smart-rename result ok={ok} provider={provider ?? "none"}");
            PostMessage(new { type = "file-explorer-smart-rename-result", ok, path, suggestions, error, provider });
        }

        // ─── FOLDER METADATA HELPER ──────────────────────────────────────

        private static string BuildFolderMetadataText(string fullPath, int maxItems, out int itemCount)
        {
            var sb = new StringBuilder();
            itemCount = 0;

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(fullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (itemCount >= maxItems) break;
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        sb.AppendLine($"[folder] {info.Name} | modified={info.LastWriteTimeUtc:yyyy-MM-dd}");
                        itemCount++;
                    }
                    catch { }
                }

                foreach (var file in Directory.EnumerateFiles(fullPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (itemCount >= maxItems) break;
                    try
                    {
                        var info = new FileInfo(file);
                        sb.AppendLine($"[file] {info.Name} | ext={info.Extension} | size={FormatBytes(info.Length)} | modified={info.LastWriteTimeUtc:yyyy-MM-dd}");
                        itemCount++;
                    }
                    catch { }
                }
            }
            catch { }

            return sb.ToString();
        }

        // ─── EXTRACT ARCHIVE ────────────────────────────────────────────

        private void HandleExtractArchive(JsonElement root)
        {
            var archivePath = GetString(root, "archivePath");
            var destinationDirectoryPath = GetString(root, "destinationDirectoryPath");
            var mode = GetString(root, "mode");

            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] message=extract-archive archive={archivePath} mode={mode}");

            if (string.IsNullOrWhiteSpace(archivePath))
            {
                PostExtractResult(false, archivePath, string.Empty, "Archive path is required.");
                return;
            }

            if (archivePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                PostExtractResult(false, archivePath, string.Empty, "Archive path contains invalid characters.");
                return;
            }

            var fullArchivePath = Path.GetFullPath(archivePath.Trim());

            if (!File.Exists(fullArchivePath))
            {
                PostExtractResult(false, archivePath, string.Empty, "Archive file not found.");
                return;
            }

            var extension = (Path.GetExtension(fullArchivePath) ?? string.Empty).ToLowerInvariant();

            if (!AllowedExtractExtensions.Contains(extension))
            {
                PostExtractResult(false, fullArchivePath, string.Empty, $"Extract is only supported for .zip, .rar, and .7z files. '{extension}' is not supported.");
                return;
            }

            if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                PostExtractResult(false, fullArchivePath, string.Empty, "Destination folder path is required.");
                return;
            }

            if (destinationDirectoryPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                PostExtractResult(false, fullArchivePath, string.Empty, "Destination path contains invalid characters.");
                return;
            }

            var fullDestination = Path.GetFullPath(destinationDirectoryPath.Trim());

            if (IsDriveRootPath(fullDestination) || IsProtectedPath(fullDestination) || IsProtectedPathOrChild(fullDestination))
            {
                PostExtractResult(false, fullArchivePath, string.Empty, "Extracting into this protected directory is blocked.");
                return;
            }

            if (Directory.Exists(fullDestination))
            {
                PostExtractResult(false, fullArchivePath, fullDestination, "Destination folder already exists. Choose a different name or delete the existing folder first.");
                return;
            }

            if (string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(() => ExtractZipSafeAsync(fullArchivePath, fullDestination));
            }
            else
            {
                var sevenZip = SevenZipPaths.FirstOrDefault(File.Exists);
                if (sevenZip == null)
                {
                    PostExtractResult(false, fullArchivePath, string.Empty,
                        $"RAR/7Z extraction requires 7-Zip. Install 7-Zip from https://www.7-zip.org/ or extract manually. (checked: {string.Join(", ", SevenZipPaths)})");
                    return;
                }

                _ = Task.Run(() => ExtractWith7ZipAsync(fullArchivePath, fullDestination, sevenZip));
            }
        }

        private async Task ExtractZipSafeAsync(string archivePath, string destinationDirectory)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] extract-zip start archive={archivePath} destination={destinationDirectory}");

                using var zipFile = ZipFile.OpenRead(archivePath);
                var entries = zipFile.Entries;
                var entryCount = entries.Count;
                long totalUncompressed = 0;

                // Validate before extracting: check count, size, and zip slip
                if (entryCount > MaxExtractFileCount)
                {
                    PostExtractResult(false, archivePath, destinationDirectory,
                        $"Archive contains {entryCount} entries which exceeds the {MaxExtractFileCount} file limit.");
                    return;
                }

                foreach (var entry in entries)
                {
                    totalUncompressed += entry.Length;
                    if (totalUncompressed > MaxExtractTotalBytes)
                    {
                        PostExtractResult(false, archivePath, destinationDirectory,
                            $"Archive uncompressed size exceeds the 10 GB limit.");
                        return;
                    }

                    // Zip slip check: ensure entry path stays inside destination
                    if (!string.IsNullOrWhiteSpace(entry.FullName))
                    {
                        var normalizedEntry = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        var outputPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedEntry));
                        var destNorm = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (!outputPath.StartsWith(destNorm, StringComparison.OrdinalIgnoreCase))
                        {
                            PostExtractResult(false, archivePath, destinationDirectory,
                                "Archive contains path traversal entries. Extraction blocked.");
                            return;
                        }
                    }
                }

                Directory.CreateDirectory(destinationDirectory);

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.FullName)) continue;
                    var normalizedEntry = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var outputPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedEntry));
                    var destNorm = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    if (!outputPath.StartsWith(destNorm, StringComparison.OrdinalIgnoreCase))
                        continue; // double-check during actual extraction

                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        Directory.CreateDirectory(outputPath);
                        continue;
                    }

                    var parentDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(parentDir))
                        Directory.CreateDirectory(parentDir);

                    entry.ExtractToFile(outputPath, overwrite: false);
                    await Task.Yield();
                }

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] extract-zip result ok=true destination={destinationDirectory}");
                PostExtractResult(true, archivePath, destinationDirectory, null);
            }
            catch (InvalidDataException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] extract-zip result ok=false reason=invalid_zip");
                PostExtractResult(false, archivePath, destinationDirectory, "Archive appears to be corrupt or is not a valid ZIP file.");
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] extract-zip result ok=false reason=access_denied");
                PostExtractResult(false, archivePath, destinationDirectory, "Access denied. Cannot write to destination folder.");
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] extract-zip result ok=false reason=exception");
                PostExtractResult(false, archivePath, destinationDirectory, "Unable to extract archive.");
            }
        }

        private async Task ExtractWith7ZipAsync(string archivePath, string destinationDirectory, string sevenZipExe)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] extract-7zip start archive={archivePath} destination={destinationDirectory} tool={sevenZipExe}");

                Directory.CreateDirectory(destinationDirectory);

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"x \"{archivePath}\" -o\"{destinationDirectory}\" -y",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    PostExtractResult(false, archivePath, destinationDirectory, "Unable to start 7-Zip process.");
                    return;
                }

                await process.WaitForExitAsync();
                var exitCode = process.ExitCode;

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] extract-7zip result exitCode={exitCode}");

                if (exitCode == 0)
                {
                    PostExtractResult(true, archivePath, destinationDirectory, null);
                }
                else
                {
                    PostExtractResult(false, archivePath, destinationDirectory,
                        $"7-Zip extraction failed (exit code {exitCode}). The archive may be corrupt or password protected.");
                }
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] extract-7zip result ok=false reason=access_denied");
                PostExtractResult(false, archivePath, destinationDirectory, "Access denied. Cannot write to destination folder.");
            }
            catch (Exception ex)
            {
                LogError(ex);
                System.Diagnostics.Debug.WriteLine("[FileExplorerHost] extract-7zip result ok=false reason=exception");
                PostExtractResult(false, archivePath, destinationDirectory, "Unable to extract archive.");
            }
        }

        private void PostExtractResult(bool ok, string archivePath, string destinationPath, string? error)
        {
            System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] extract-result ok={ok}");
            if (!string.IsNullOrWhiteSpace(error))
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] error={error}");

            PostMessage(new
            {
                type = "file-explorer-extract-result",
                ok,
                archivePath,
                destinationPath,
                error
            });
        }

        // ─── LOG ERROR ───────────────────────────────────────────────────

        private static void LogError(Exception ex)
        {
            try
            {
                var message = ex.Message ?? "Unknown error";
                message = message.Replace("\r", " ").Replace("\n", " ");
                if (message.Length > 220)
                    message = message[..220];

                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] error={message}");
            }
            catch
            {
            }
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[FileExplorerHost] navigationCompleted success={e.IsSuccess}");
            }
            catch
            {
            }
        }

        private static string ResolveDistPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "Figma", "Futuristic AI File Explorer", "dist");
        }
    }
}
