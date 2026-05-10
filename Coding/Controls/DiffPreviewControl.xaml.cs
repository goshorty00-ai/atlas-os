#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AtlasAI.Coding.Services;

namespace AtlasAI.Coding.Controls
{
    /// <summary>
    /// Diff Preview Control - displays code diffs with apply/reject actions.
    /// </summary>
    public partial class DiffPreviewControl : UserControl
    {
        private readonly ChatToCodeService _chatToCode = ChatToCodeService.Instance;
        private PendingChange? _currentChange;
        
        // Events
        public event Action<PendingChange>? OnApplyRequested;
        public event Action<PendingChange>? OnRejectRequested;
        public event Action<string>? OnOpenFileRequested;
        public event Action<string>? OnDiffCopied;
        public event Action? ApplyRequested;
        public event Action? RejectRequested;
        public event Action? ApplyAllRequested;
        
        public DiffPreviewControl()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Display a pending change with diff preview.
        /// </summary>
        public void ShowChange(PendingChange change)
        {
            _currentChange = change;
            
            // Update header
            FilePathText.Text = change.FilePath ?? "untitled";
            UpdateModeBadge(change.ApplyMode);
            
            // Update stats
            var diff = change.Diff ?? change.SelectionDiff?.Diff;
            if (diff != null)
            {
                StatsText.Text = $"+{diff.AddedLines} -{diff.RemovedLines}";
                StatsText.Foreground = new SolidColorBrush(
                    diff.AddedLines > diff.RemovedLines ? Color.FromRgb(0x4a, 0xde, 0x80) :
                    diff.RemovedLines > diff.AddedLines ? Color.FromRgb(0xf8, 0x71, 0x71) :
                    Color.FromRgb(0x9c, 0xa3, 0xaf));
            }
            
            // Show intent header if available
            if (!string.IsNullOrEmpty(change.Description))
            {
                IntentText.Text = change.Description;
                IntentHeaderPanel.Visibility = Visibility.Visible;
            }
            else
            {
                IntentHeaderPanel.Visibility = Visibility.Collapsed;
            }
            
            // Show/hide open file button
            OpenFileButton.Visibility = !string.IsNullOrEmpty(change.FilePath) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
            
            // Render diff
            RenderDiff(diff);
        }
        
        /// <summary>
        /// Display a pending change with explicit intent.
        /// </summary>
        public void ShowChangeWithIntent(PendingChange change, string intent)
        {
            ShowChange(change);
            SetIntent(intent);
        }
        
        /// <summary>
        /// Set the intent header text.
        /// </summary>
        public void SetIntent(string intent)
        {
            if (!string.IsNullOrEmpty(intent))
            {
                IntentText.Text = intent;
                IntentHeaderPanel.Visibility = Visibility.Visible;
            }
            else
            {
                IntentHeaderPanel.Visibility = Visibility.Collapsed;
            }
        }
        
        /// <summary>
        /// Show a diff result directly.
        /// </summary>
        public void ShowDiff(DiffResult diff, ApplyMode mode = ApplyMode.File)
        {
            FilePathText.Text = diff.FilePath;
            UpdateModeBadge(mode);
            StatsText.Text = $"+{diff.AddedLines} -{diff.RemovedLines}";
            RenderDiff(diff);
        }
        
        /// <summary>
        /// Clear the preview.
        /// </summary>
        public void Clear()
        {
            _currentChange = null;
            FilePathText.Text = "untitled";
            StatsText.Text = "+0 -0";
            DiffContent.Children.Clear();
            EmptyMessage.Visibility = Visibility.Visible;
        }
        
        private void UpdateModeBadge(ApplyMode mode)
        {
            var (text, bg, fg) = mode switch
            {
                ApplyMode.Selection => ("SELECTION", Color.FromRgb(0x1a, 0x2e, 0x1a), Color.FromRgb(0x4a, 0xde, 0x80)),
                ApplyMode.File => ("FILE", Color.FromRgb(0x1a, 0x1a, 0x2e), Color.FromRgb(0x60, 0xa5, 0xfa)),
                ApplyMode.MultiFile => ("MULTI-FILE", Color.FromRgb(0x2e, 0x1a, 0x2e), Color.FromRgb(0xc0, 0x84, 0xfc)),
                ApplyMode.NewFile => ("NEW FILE", Color.FromRgb(0x2e, 0x2e, 0x1a), Color.FromRgb(0xfb, 0xbf, 0x24)),
                _ => ("CHANGE", Color.FromRgb(0x1a, 0x1a, 0x24), Color.FromRgb(0x9c, 0xa3, 0xaf))
            };
            
            ModeText.Text = text;
            ModeBadge.Background = new SolidColorBrush(bg);
            ModeText.Foreground = new SolidColorBrush(fg);
        }
        
        private void RenderDiff(DiffResult? diff)
        {
            DiffContent.Children.Clear();
            
            if (diff == null || !diff.HasChanges)
            {
                EmptyMessage.Visibility = Visibility.Visible;
                return;
            }
            
            EmptyMessage.Visibility = Visibility.Collapsed;
            
            foreach (var hunk in diff.Hunks)
            {
                // Hunk header
                var hunkHeader = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x24)),
                    Padding = new Thickness(12, 4, 12, 4)
                };
                hunkHeader.Child = new TextBlock
                {
                    Text = $"@@ -{hunk.OriginalStart},{hunk.OriginalLength} +{hunk.ModifiedStart},{hunk.ModifiedLength} @@",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xa5, 0xfa))
                };
                DiffContent.Children.Add(hunkHeader);
                
                // Diff lines
                foreach (var line in hunk.Lines)
                {
                    var linePanel = CreateDiffLine(line);
                    DiffContent.Children.Add(linePanel);
                }
            }
        }
        
        private Border CreateDiffLine(DiffLine line)
        {
            var (bg, fg, prefix) = line.Type switch
            {
                DiffLineType.Addition => (
                    (SolidColorBrush)FindResource("AdditionBg"),
                    (SolidColorBrush)FindResource("AdditionFg"),
                    "+"),
                DiffLineType.Deletion => (
                    (SolidColorBrush)FindResource("DeletionBg"),
                    (SolidColorBrush)FindResource("DeletionFg"),
                    "-"),
                _ => (
                    (SolidColorBrush)FindResource("ContextBg"),
                    (SolidColorBrush)FindResource("ContextFg"),
                    " ")
            };
            
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            // Line numbers
            var lineNumText = line.Type == DiffLineType.Addition 
                ? $"    {line.ModifiedLineNumber,4}" 
                : line.Type == DiffLineType.Deletion 
                    ? $"{line.OriginalLineNumber,4}    " 
                    : $"{line.OriginalLineNumber,4} {line.ModifiedLineNumber,4}";
            
            var lineNum = new TextBlock
            {
                Text = lineNumText.Substring(0, Math.Min(8, lineNumText.Length)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = (SolidColorBrush)FindResource("LineNumberFg"),
                Padding = new Thickness(4, 2, 8, 2),
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(lineNum, 0);
            grid.Children.Add(lineNum);
            
            // Content
            var content = new TextBlock
            {
                Text = prefix + line.Content,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = fg,
                Padding = new Thickness(8, 2, 8, 2),
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(content, 1);
            grid.Children.Add(content);
            
            return new Border
            {
                Background = bg,
                Child = grid,
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x24))
            };
        }
        
        #region Event Handlers
        
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChange != null)
            {
                OnApplyRequested?.Invoke(_currentChange);
            }
        }
        
        private void RejectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChange != null)
            {
                OnRejectRequested?.Invoke(_currentChange);
                Clear();
            }
        }
        
        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentChange?.FilePath))
            {
                OnOpenFileRequested?.Invoke(_currentChange.FilePath);
            }
        }
        
        private void CopyDiffButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChange != null)
            {
                var diffString = _chatToCode.GetDiffString(_currentChange.Id);
                if (!string.IsNullOrEmpty(diffString))
                {
                    Clipboard.SetText(diffString);
                    OnDiffCopied?.Invoke(diffString);
                }
            }
        }
        
        #endregion
    }
}
