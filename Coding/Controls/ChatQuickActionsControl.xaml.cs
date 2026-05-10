#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.Coding.Services;

namespace AtlasAI.Coding.Controls
{
    /// <summary>
    /// Chat Quick Actions Control - provides quick action buttons for chat-to-code workflows.
    /// </summary>
    public partial class ChatQuickActionsControl : UserControl
    {
        private readonly ChatToCodeService _chatToCode = ChatToCodeService.Instance;
        private PendingChange? _currentChange;
        private string? _currentFilePath;
        private int _selectionStart;
        private int _selectionEnd;
        
        // Events
        public event Action<PendingChange>? OnApplyClicked;
        public event Action<PendingChange>? OnStageClicked;
        public event Action<string>? OnOpenFileClicked;
        public event Action<string>? OnCopyDiffClicked;
        
        public ChatQuickActionsControl()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Set the current pending change for actions.
        /// </summary>
        public void SetPendingChange(PendingChange? change)
        {
            _currentChange = change;
            UpdateButtonStates();
        }
        
        /// <summary>
        /// Update selection context info.
        /// </summary>
        public void UpdateSelectionContext(string? filePath, int selectionStart, int selectionEnd, bool hasSelection)
        {
            _currentFilePath = filePath;
            _selectionStart = selectionStart;
            _selectionEnd = selectionEnd;
            
            if (hasSelection && selectionEnd > selectionStart)
            {
                SelectionBadge.Visibility = Visibility.Visible;
                SelectionInfo.Text = $"Lines {GetLineNumber(selectionStart)}-{GetLineNumber(selectionEnd)}";
                ContextText.Text = "Selection ready";
            }
            else if (!string.IsNullOrEmpty(filePath))
            {
                SelectionBadge.Visibility = Visibility.Collapsed;
                ContextText.Text = System.IO.Path.GetFileName(filePath);
            }
            else
            {
                SelectionBadge.Visibility = Visibility.Collapsed;
                ContextText.Text = "Ready to apply changes";
            }
        }

        /// <summary>
        /// Show buttons for a ready change.
        /// </summary>
        public void ShowChangeReady(PendingChange change)
        {
            _currentChange = change;
            
            CopyDiffButton.Visibility = Visibility.Visible;
            ApplyButton.IsEnabled = true;
            
            // Show stage button for multi-file changes
            ApplyStageButton.Visibility = change.ApplyMode == ApplyMode.MultiFile 
                ? Visibility.Visible 
                : Visibility.Collapsed;
            
            // Show open file button if we have a file path
            OpenFileButton.Visibility = !string.IsNullOrEmpty(change.FilePath) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
            
            // Update context text
            ContextText.Text = change.ApplyMode switch
            {
                ApplyMode.Selection => "Apply to selection",
                ApplyMode.File => $"Apply to {System.IO.Path.GetFileName(change.FilePath)}",
                ApplyMode.MultiFile => $"Apply to {change.MultiFileChanges.Count} files",
                ApplyMode.NewFile => $"Create {System.IO.Path.GetFileName(change.FilePath)}",
                _ => "Ready to apply"
            };
        }
        
        /// <summary>
        /// Reset to default state.
        /// </summary>
        public void Reset()
        {
            _currentChange = null;
            CopyDiffButton.Visibility = Visibility.Collapsed;
            OpenFileButton.Visibility = Visibility.Collapsed;
            ApplyStageButton.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = false;
            SelectionBadge.Visibility = Visibility.Collapsed;
            ContextText.Text = "Ready to apply changes";
        }
        
        private void UpdateButtonStates()
        {
            if (_currentChange == null)
            {
                Reset();
                return;
            }
            
            ShowChangeReady(_currentChange);
        }
        
        private int GetLineNumber(int charIndex)
        {
            // Simple approximation - in real use, would need actual content
            return Math.Max(1, charIndex / 40 + 1);
        }
        
        #region Event Handlers
        
        private void CopyDiffButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChange == null) return;
            
            var diffString = _chatToCode.GetDiffString(_currentChange.Id);
            if (!string.IsNullOrEmpty(diffString))
            {
                Clipboard.SetText(diffString);
                OnCopyDiffClicked?.Invoke(diffString);
                
                // Visual feedback
                var originalContent = CopyDiffButton.Content;
                CopyDiffButton.Content = "✓ Copied";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.5)
                };
                timer.Tick += (s, args) =>
                {
                    CopyDiffButton.Content = originalContent;
                    timer.Stop();
                };
                timer.Start();
            }
        }
        
        private void OpenFileButton_Click(object sender, RoutedEventArgs e)
        {
            var filePath = _currentChange?.FilePath ?? _currentFilePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                OnOpenFileClicked?.Invoke(filePath);
            }
        }
        
        private void ApplyStageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChange != null)
            {
                OnStageClicked?.Invoke(_currentChange);
            }
        }
        
        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChange != null)
            {
                OnApplyClicked?.Invoke(_currentChange);
            }
        }
        
        #endregion
    }
}
  /// 