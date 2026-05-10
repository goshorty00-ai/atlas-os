using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AtlasAI.Controls
{
    public partial class CodeControl : UserControl
    {
        private readonly CodeBuilderViewModel _vm;
        private string _searchRoot = "";
        private readonly System.Collections.Generic.List<string> _searchFiles = new System.Collections.Generic.List<string>();
        private static readonly Regex _msBuildPathRegex = new Regex(@"(?<path>[A-Za-z]:\\[^:\r\n]+?)\((?<line>\d+)(,(?<col>\d+))?\)", RegexOptions.Compiled);
        private static readonly Regex _colonPathRegex = new Regex(@"(?<path>[A-Za-z]:\\[^:\r\n]+?):(?<line>\d+)(:(?<col>\d+))?", RegexOptions.Compiled);
        private const string FolderIconGeometry = "M3 6.5C3 5.67157 3.67157 5 4.5 5H9.2C9.50904 5 9.80933 5.09518 10.06 5.2725L11.2 6.1C11.4507 6.27732 11.751 6.3725 12.06 6.3725H19.5C20.3284 6.3725 21 7.04407 21 7.8725V17.5C21 18.3284 20.3284 19 19.5 19H4.5C3.67157 19 3 18.3284 3 17.5V6.5Z";
        private const string FileIconGeometry = "M7 3H14.2L18 6.8V21H7V3ZM14 3V7H18";

        public event EventHandler<OpenFileRequestEventArgs>? OpenFileRequested;

        public CodeControl()
        {
            InitializeComponent();
            _vm = new CodeBuilderViewModel();
            DataContext = _vm;

            try
            {
                _vm.OpenFileRequested += (_, e) =>
                {
                    try
                    {
                        OpenFileRequested?.Invoke(this, e);
                    }
                    catch
                    {
                    }
                };
            }
            catch
            {
            }

            Loaded += (_, __) =>
            {
                try
                {
                    _vm.Start();
                    _vm.RestartSimulation();
                    HookScroll();
                    RefreshProjectFilesTree();
                }
                catch
                {
                }
            };

            IsVisibleChanged += (_, __) =>
            {
                try
                {
                    if (IsVisible)
                        _vm.RestartSimulation();
                }
                catch
                {
                }
            };
        }

        public string WorkspaceRoot => _vm.WorkspaceRoot;

        public void SetWorkspaceRoot(string path)
        {
            try
            {
                _vm.SetWorkspaceRoot(path);
                RefreshProjectFilesTree();
            }
            catch
            {
            }
        }

        public void RunDefaultTask()
        {
            try
            {
                if (_vm.RunDefaultTaskCommand.CanExecute(null))
                    _vm.RunDefaultTaskCommand.Execute(null);
            }
            catch
            {
            }
        }

        public void BuildDefault()
        {
            try
            {
                if (_vm.BuildDefaultTaskCommand.CanExecute(null))
                    _vm.BuildDefaultTaskCommand.Execute(null);
            }
            catch
            {
            }
        }

        public void TestDefault()
        {
            try
            {
                if (_vm.TestDefaultTaskCommand.CanExecute(null))
                    _vm.TestDefaultTaskCommand.Execute(null);
            }
            catch
            {
            }
        }

        public void ListTasks()
        {
            try
            {
                if (_vm.ListTasksCommand.CanExecute(null))
                    _vm.ListTasksCommand.Execute(null);
            }
            catch
            {
            }
        }

        public void ClearOutput()
        {
            try
            {
                if (_vm.ClearOutputCommand.CanExecute(null))
                    _vm.ClearOutputCommand.Execute(null);
            }
            catch
            {
            }
        }

        public void StopRunningTask()
        {
            try
            {
                _vm.StopRunningTask();
            }
            catch
            {
            }
        }

        public void RunLastTask()
        {
            try
            {
                _ = _vm.RunLastTaskAsync();
            }
            catch
            {
            }
        }

        public void ShowTaskHistory()
        {
            try
            {
                _vm.ShowTaskHistory();
            }
            catch
            {
            }
        }

        public void OpenFirstError()
        {
            try
            {
                _vm.OpenFirstError();
            }
            catch
            {
            }
        }

        public void OpenNextError()
        {
            try
            {
                _vm.OpenNextError();
            }
            catch
            {
            }
        }

        public void OpenPrevError()
        {
            try
            {
                _vm.OpenPrevError();
            }
            catch
            {
            }
        }

        private void HookScroll()
        {
            try
            {
                _vm.PropertyChanged -= VmOnPropertyChanged;
                _vm.PropertyChanged += VmOnPropertyChanged;
            }
            catch
            {
            }
        }

        private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (string.Equals(e.PropertyName, nameof(CodeBuilderViewModel.WorkspaceRoot), StringComparison.Ordinal))
                {
                    RefreshProjectFilesTree();
                    return;
                }
                if (!string.Equals(e.PropertyName, nameof(CodeBuilderViewModel.OutputLog), StringComparison.Ordinal))
                    return;
                if (ChatScrollViewer == null) return;
                ChatScrollViewer.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { ChatScrollViewer.ScrollToEnd(); } catch { }
                }));
            }
            catch
            {
            }
        }

        private void RefreshProjectFilesTree()
        {
            try
            {
                if (ProjectFilesTree == null) return;

                ProjectFilesTree.Items.Clear();

                var root = _vm.WorkspaceRoot;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    return;

                var rootItem = CreateProjectTreeItem(root, isRoot: true);
                rootItem.IsExpanded = true;
                ProjectFilesTree.Items.Add(rootItem);

                RefreshProjectFileSearchIndex(root);
            }
            catch
            {
            }
        }

        private void RefreshProjectFileSearchIndex(string root)
        {
            try
            {
                var r = (root ?? "").Trim();
                if (string.IsNullOrWhiteSpace(r) || !Directory.Exists(r)) return;

                if (string.Equals(_searchRoot, r, StringComparison.OrdinalIgnoreCase) && _searchFiles.Count > 0)
                    return;

                _searchRoot = r;
                _searchFiles.Clear();

                var stack = new System.Collections.Generic.Stack<string>();
                stack.Push(r);

                while (stack.Count > 0 && _searchFiles.Count < 15000)
                {
                    var dir = stack.Pop();
                    try
                    {
                        foreach (var subDir in Directory.GetDirectories(dir))
                        {
                            var name = Path.GetFileName(subDir);
                            if (ShouldSkip(name)) continue;
                            stack.Push(subDir);
                        }

                        foreach (var file in Directory.GetFiles(dir))
                        {
                            var name = Path.GetFileName(file);
                            if (ShouldSkip(name)) continue;
                            _searchFiles.Add(file);
                            if (_searchFiles.Count >= 15000) break;
                        }
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

        private TreeViewItem CreateProjectTreeItem(string path, bool isRoot = false)
        {
            var isDirectory = Directory.Exists(path);
            var name = isRoot ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name))
                name = path;

            var item = new TreeViewItem
            {
                Header = BuildTreeHeader(path, name, isDirectory),
                Tag = path,
                Foreground = GetThemeBrush("AtlasTextSecondaryBrush", Brushes.White)
            };

            if (isDirectory)
            {
                item.Items.Add(new TreeViewItem { Header = "…" });
                item.Expanded += (_, __) => LoadProjectTreeChildren(item);
            }

            return item;
        }

        private static FrameworkElement BuildTreeHeader(string path, string name, bool isDirectory)
        {
            var iconBrush = isDirectory
                ? GetThemeBrush("AtlasWarningBrush", Brushes.Orange)
                : GetFileBrush(path);

            var root = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            root.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(isDirectory ? FolderIconGeometry : FileIconGeometry),
                Stroke = iconBrush,
                StrokeThickness = 1.6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            root.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = GetThemeBrush("AtlasTextSecondaryBrush", Brushes.White),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            });

            return root;
        }

        private static Brush GetFileBrush(string path)
        {
            try
            {
                var ext = (Path.GetExtension(path) ?? "").Trim().TrimStart('.').ToLowerInvariant();
                return ext switch
                {
                    "xaml" => GetThemeBrush("AtlasCyanBrush", Brushes.Cyan),
                    "cs" => GetThemeBrush("AtlasSuccessBrush", Brushes.LimeGreen),
                    "md" => GetThemeBrush("AtlasWarningBrush", Brushes.Orange),
                    _ => GetThemeBrush("AtlasTextMutedBrush", Brushes.Gray)
                };
            }
            catch
            {
                return Brushes.Gray;
            }
        }

        private static Brush GetThemeBrush(string key, Brush fallback)
        {
            try
            {
                var res = Application.Current?.Resources;
                if (res == null) return fallback;
                if (res.Contains(key) && res[key] is Brush b) return b;
            }
            catch
            {
            }
            return fallback;
        }

        private void LoadProjectTreeChildren(TreeViewItem parent)
        {
            try
            {
                if (parent.Tag is not string path) return;
                if (!Directory.Exists(path)) return;

                if (parent.Items.Count == 1 && parent.Items[0] is TreeViewItem t && (t.Header as string) == "…")
                    parent.Items.Clear();
                else if (parent.Items.Count > 0 && parent.Items.OfType<TreeViewItem>().All(i => i.Tag is string))
                    return;

                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => Path.GetFileName(d)))
                {
                    var dirName = Path.GetFileName(dir);
                    if (ShouldSkip(dirName)) continue;
                    parent.Items.Add(CreateProjectTreeItem(dir));
                }

                foreach (var file in Directory.GetFiles(path).OrderBy(f => Path.GetFileName(f)))
                {
                    var fileName = Path.GetFileName(file);
                    if (ShouldSkip(fileName)) continue;
                    parent.Items.Add(CreateProjectTreeItem(file));
                }
            }
            catch
            {
            }
        }

        private static bool ShouldSkip(string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name)) return true;
                if (name.StartsWith(".", StringComparison.Ordinal)) return true;
                if (string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(name, ".vs", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static string GetFileIcon(string path)
        {
            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                return ext switch
                {
                    ".cs" => "🟣",
                    ".ts" or ".tsx" => "🔵",
                    ".js" or ".jsx" => "🟡",
                    ".py" => "🐍",
                    ".json" => "📋",
                    ".xml" or ".xaml" => "📄",
                    ".html" or ".htm" => "🌐",
                    ".css" or ".scss" or ".sass" => "🎨",
                    ".md" => "📝",
                    ".txt" => "📃",
                    ".sln" => "🔷",
                    ".csproj" => "⚙️",
                    _ => "📄"
                };
            }
            catch
            {
                return "📄";
            }
        }

        private void ProjectFilesTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            try
            {
                if (e.NewValue is not TreeViewItem item) return;
                if (item.Tag is not string path) return;
                if (!File.Exists(path)) return;

                OpenFileRequested?.Invoke(this, new OpenFileRequestEventArgs(path));
            }
            catch
            {
            }
        }

        private void ProjectFileSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var q = (ProjectFileSearchBox?.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                {
                    if (ProjectFileSearchResultsHost != null) ProjectFileSearchResultsHost.Visibility = Visibility.Collapsed;
                    if (ProjectFileSearchResults != null) ProjectFileSearchResults.Items.Clear();
                    return;
                }

                var root = _vm.WorkspaceRoot;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    return;

                RefreshProjectFileSearchIndex(root);

                var qLower = q.ToLowerInvariant();
                var matches = _searchFiles
                    .Select(p =>
                    {
                        string rel;
                        try { rel = Path.GetRelativePath(root, p); } catch { rel = p; }
                        return new { Full = p, Rel = rel };
                    })
                    .Where(x =>
                        x.Rel.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(x.Rel).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        x.Rel.ToLowerInvariant().Contains(qLower))
                    .OrderBy(x => x.Rel.Length)
                    .ThenBy(x => x.Rel)
                    .Take(60)
                    .ToList();

                if (ProjectFileSearchResults == null || ProjectFileSearchResultsHost == null)
                    return;

                ProjectFileSearchResults.Items.Clear();
                foreach (var m in matches)
                {
                    var header = new StackPanel { Orientation = Orientation.Horizontal };
                    header.Children.Add(new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(FileIconGeometry),
                        Stroke = GetFileBrush(m.Full),
                        StrokeThickness = 1.6,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Fill = Brushes.Transparent,
                        Width = 14,
                        Height = 14,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    header.Children.Add(new TextBlock
                    {
                        Text = m.Rel.Replace('\\', '/'),
                        Foreground = GetThemeBrush("AtlasTextSecondaryBrush", Brushes.White)
                    });

                    ProjectFileSearchResults.Items.Add(new ListBoxItem
                    {
                        Content = header,
                        Tag = m.Full
                    });
                }

                ProjectFileSearchResultsHost.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
            }
        }

        private void ProjectFileSearchResults_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ProjectFileSearchResults?.SelectedItem is not ListBoxItem it) return;
                if (it.Tag is not string path) return;
                if (!File.Exists(path)) return;

                OpenFileRequested?.Invoke(this, new OpenFileRequestEventArgs(path));
            }
            catch
            {
            }
        }

        private void OutputLogBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (OutputLogBox == null) return;
                if (string.IsNullOrWhiteSpace(OutputLogBox.Text)) return;

                var caret = OutputLogBox.CaretIndex;
                var lineIndex = OutputLogBox.GetLineIndexFromCharacterIndex(caret);
                if (lineIndex < 0) return;

                var lineText = OutputLogBox.GetLineText(lineIndex)?.Trim();
                if (string.IsNullOrWhiteSpace(lineText)) return;

                if (!TryParseFileLocation(lineText, out var path, out var line, out var col))
                    return;

                var root = _vm.WorkspaceRoot;
                var full = path;
                if (!Path.IsPathRooted(full) && !string.IsNullOrWhiteSpace(root))
                {
                    try { full = Path.GetFullPath(Path.Combine(root, full)); } catch { }
                }

                if (!File.Exists(full)) return;
                OpenFileRequested?.Invoke(this, new OpenFileRequestEventArgs(full, line, col));
            }
            catch
            {
            }
        }

        private void OutputLogBox_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (OutputLogBox == null) return;
                if (string.IsNullOrWhiteSpace(OutputLogBox.Text))
                {
                    OutputLogBox.Cursor = Cursors.IBeam;
                    OutputLogBox.ToolTip = null;
                    return;
                }

                var idx = OutputLogBox.GetCharacterIndexFromPoint(e.GetPosition(OutputLogBox), true);
                if (idx < 0)
                {
                    OutputLogBox.Cursor = Cursors.IBeam;
                    OutputLogBox.ToolTip = null;
                    return;
                }

                var lineIndex = OutputLogBox.GetLineIndexFromCharacterIndex(idx);
                if (lineIndex < 0)
                {
                    OutputLogBox.Cursor = Cursors.IBeam;
                    OutputLogBox.ToolTip = null;
                    return;
                }

                var lineText = (OutputLogBox.GetLineText(lineIndex) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(lineText))
                {
                    OutputLogBox.Cursor = Cursors.IBeam;
                    OutputLogBox.ToolTip = null;
                    return;
                }

                if (TryParseFileLocation(lineText, out _, out _, out _))
                {
                    OutputLogBox.Cursor = Cursors.Hand;
                    OutputLogBox.ToolTip = "Double-click to open";
                }
                else
                {
                    OutputLogBox.Cursor = Cursors.IBeam;
                    OutputLogBox.ToolTip = null;
                }
            }
            catch
            {
            }
        }

        private void OutputLogBox_MouseLeave(object sender, MouseEventArgs e)
        {
            try
            {
                if (OutputLogBox == null) return;
                OutputLogBox.Cursor = Cursors.IBeam;
                OutputLogBox.ToolTip = null;
            }
            catch
            {
            }
        }

        private static bool TryParseFileLocation(string text, out string path, out int line, out int col)
        {
            path = "";
            line = 0;
            col = 0;
            try
            {
                var m = _msBuildPathRegex.Match(text);
                if (m.Success)
                {
                    path = (m.Groups["path"].Value ?? "").Trim();
                    _ = int.TryParse(m.Groups["line"].Value, out line);
                    _ = int.TryParse(m.Groups["col"].Value, out col);
                    return !string.IsNullOrWhiteSpace(path);
                }

                m = _colonPathRegex.Match(text);
                if (m.Success)
                {
                    path = (m.Groups["path"].Value ?? "").Trim();
                    _ = int.TryParse(m.Groups["line"].Value, out line);
                    _ = int.TryParse(m.Groups["col"].Value, out col);
                    return !string.IsNullOrWhiteSpace(path);
                }

                var idx = text.IndexOf(@":\", StringComparison.Ordinal);
                if (idx >= 1)
                {
                    var start = idx - 1;
                    while (start > 0 && text[start - 1] != ' ' && text[start - 1] != '"' && text[start - 1] != '\'' && text[start - 1] != '(' && text[start - 1] != '[')
                        start--;
                    var end = idx + 2;
                    while (end < text.Length && text[end] != ' ' && text[end] != '"' && text[end] != '\'' && text[end] != ')' && text[end] != ']' && text[end] != ',')
                        end++;
                    var candidate = text.Substring(start, end - start).Trim().Trim('"', '\'', '(', ')', '[', ']', ',');
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        path = candidate;
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (e.Key != Key.Enter) return;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift) return;
                if (_vm.SendMessageCommand.CanExecute(null))
                {
                    _vm.SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }

        private void CodeControl_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ChatInputBox == null) return;
                if (ChatInputBox.IsKeyboardFocusWithin) return;
                ChatInputBox.Focus();
                Keyboard.Focus(ChatInputBox);
            }
            catch
            {
            }
        }

        private void CodeControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (ChatInputBox == null) return;

                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                    (e.Key == Key.K || e.Key == Key.L))
                {
                    ChatInputBox.Focus();
                    ChatInputBox.SelectAll();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Oem2 && Keyboard.Modifiers == ModifierKeys.None && !ChatInputBox.IsKeyboardFocusWithin)
                {
                    ChatInputBox.Focus();
                    ChatInputBox.SelectAll();
                    e.Handled = true;
                }
            }
            catch
            {
            }
        }
    }
}
