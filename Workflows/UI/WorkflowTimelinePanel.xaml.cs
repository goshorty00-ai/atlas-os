using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace AtlasAI.Workflows.UI
{
    /// <summary>
    /// Workflow Timeline Panel - Displays workflow progress as vertical timeline.
    /// User controls execution via "Run Next Step" button.
    /// 
    /// SAFETY: Display only, no system operations.
    /// All execution is delegated to WorkflowEngine.
    /// </summary>
    public partial class WorkflowTimelinePanel : UserControl
    {
        private readonly WorkflowEngine _engine;
        private bool _isExecuting = false;

        public WorkflowTimelinePanel()
        {
            InitializeComponent();

            _engine = WorkflowEngine.Instance;

            // Subscribe to engine events
            _engine.WorkflowStarted += OnWorkflowStarted;
            _engine.StepStarted += OnStepStarted;
            _engine.StepCompleted += OnStepCompleted;
            _engine.WorkflowCompleted += OnWorkflowCompleted;
            _engine.WorkflowCancelled += OnWorkflowCancelled;

            // Check if there's already an active workflow
            if (_engine.ActiveWorkflow != null)
            {
                UpdateUI(_engine.ActiveWorkflow);
            }

            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from events
            _engine.WorkflowStarted -= OnWorkflowStarted;
            _engine.StepStarted -= OnStepStarted;
            _engine.StepCompleted -= OnStepCompleted;
            _engine.WorkflowCompleted -= OnWorkflowCompleted;
            _engine.WorkflowCancelled -= OnWorkflowCancelled;
        }

        #region Public Methods

        /// <summary>
        /// Start a workflow by ID
        /// </summary>
        public void StartWorkflow(string workflowId)
        {
            var instance = _engine.StartWorkflow(workflowId);
            if (instance != null)
            {
                UpdateUI(instance);
            }
        }

        /// <summary>
        /// Get the active workflow instance
        /// </summary>
        public WorkflowChainInstance? GetActiveWorkflow() => _engine.ActiveWorkflow;

        #endregion

        #region Event Handlers

        private void OnWorkflowStarted(object? sender, WorkflowChainInstance instance)
        {
            Dispatcher.BeginInvoke(() => UpdateUI(instance));
        }

        private void OnStepStarted(object? sender, WorkflowStep step)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isExecuting = true;
                RunNextButton.Content = "⏳ Running...";
                RunNextButton.IsEnabled = false;
                StatusText.Text = $"Running step {step.StepNumber}...";
                RefreshStepsList();
            });
        }

        private void OnStepCompleted(object? sender, WorkflowStep step)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isExecuting = false;
                RefreshStepsList();
                UpdateProgress();

                var workflow = _engine.ActiveWorkflow;
                if (workflow != null && !workflow.IsComplete)
                {
                    RunNextButton.Content = "▶ Run Next Step";
                    RunNextButton.IsEnabled = true;
                    StatusText.Text = $"Step {step.StepNumber} complete. Ready for next.";
                }
            });
        }

        private void OnWorkflowCompleted(object? sender, WorkflowChainInstance instance)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isExecuting = false;
                RunNextButton.Content = "✓ Complete";
                RunNextButton.IsEnabled = false;
                CancelButton.Visibility = Visibility.Collapsed;
                StatusText.Text = instance.FinalInsight ?? "Workflow complete!";
                UpdateProgress();
            });
        }

        private void OnWorkflowCancelled(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isExecuting = false;
                ShowEmptyState();
            });
        }

        #endregion

        #region UI Updates

        private void UpdateUI(WorkflowChainInstance instance)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;

            WorkflowIcon.Text = instance.Icon;
            WorkflowTitle.Text = instance.Title;
            WorkflowDescription.Text = instance.Description;

            RefreshStepsList();
            UpdateProgress();

            if (!instance.IsComplete)
            {
                RunNextButton.Content = "▶ Run Next Step";
                RunNextButton.IsEnabled = true;
                StatusText.Text = "Ready to start";
            }
            else
            {
                RunNextButton.Content = "✓ Complete";
                RunNextButton.IsEnabled = false;
                StatusText.Text = instance.FinalInsight ?? "Complete";
            }
        }

        private void RefreshStepsList()
        {
            var workflow = _engine.ActiveWorkflow;
            if (workflow == null)
            {
                StepsItemsControl.ItemsSource = null;
                return;
            }

            // Create a fresh binding to trigger UI update
            StepsItemsControl.ItemsSource = null;
            StepsItemsControl.ItemsSource = workflow.Steps;
        }

        private void UpdateProgress()
        {
            var workflow = _engine.ActiveWorkflow;
            if (workflow == null)
            {
                ProgressBar.Width = 0;
                return;
            }

            var percent = workflow.ProgressPercent;
            var maxWidth = ActualWidth - 24; // Account for padding
            if (maxWidth > 0)
            {
                ProgressBar.Width = maxWidth * (percent / 100.0);
            }
        }

        private void ShowEmptyState()
        {
            EmptyState.Visibility = Visibility.Visible;
            CancelButton.Visibility = Visibility.Collapsed;
            WorkflowTitle.Text = "No Workflow Active";
            WorkflowDescription.Text = "Select a workflow to begin";
            WorkflowIcon.Text = "🔗";
            StepsItemsControl.ItemsSource = null;
            ProgressBar.Width = 0;
            RunNextButton.Content = "▶ Run Next Step";
            RunNextButton.IsEnabled = false;
            StatusText.Text = "Ready";
        }

        #endregion

        #region Button Handlers

        private async void RunNextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isExecuting)
                return;

            try
            {
                await _engine.RunNextStepAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WorkflowTimeline] Error running step: {ex.Message}");
                StatusText.Text = $"Error: {ex.Message}";
                _isExecuting = false;
                RunNextButton.Content = "▶ Retry";
                RunNextButton.IsEnabled = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _engine.CancelWorkflow();
            ShowEmptyState();
        }

        #endregion
    }

    /// <summary>
    /// Converter for null/empty string to Collapsed visibility
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null || (value is string s && string.IsNullOrEmpty(s)))
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
