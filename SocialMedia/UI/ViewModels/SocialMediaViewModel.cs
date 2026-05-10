using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AtlasAI.AI;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia.UI.ViewModels
{
    public class SocialMediaViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        private string _currentPage = "Campaigns";
        private string _selectedBrand = "Default";
        private bool _isTikTokEnabled = true;
        private bool _isInstagramEnabled = true;
        private bool _isFacebookEnabled = true;
        private bool _isYouTubeEnabled = true;
        private bool _isNavCollapsed = false;
        
        // Campaign Builder
        private string _selectedGoal = "Engagement";
        private string _selectedDuration = "7 days";
        private string _selectedTone = "Professional";
        private string _selectedCta = "Soft";
        private string _campaignOutput = "";
        
        // Create Post
        private string _postPlatform = "Instagram";
        private string _postFormat = "Post";
        private string _postPrompt = "";
        private string _generatedHook = "";
        private string _generatedBody = "";
        private string _generatedCta = "";
        private string _generatedHashtags = "";
        private int _variantCount = 3;
        private bool _includeHashtags = true;
        private bool _includeCta = true;
        private string _imagePrompt = "";
        private string _attachedImagePath = "";
        
        // Scheduler
        private string _schedulerView = "Calendar";
        
        // Content Library
        private string _librarySearchQuery = "";
        private string _libraryPlatformFilter = "All";
        private string _libraryTypeFilter = "All";
        private bool _showBestPerforming = false;
        private bool _isGridView = true;
        private System.Threading.CancellationTokenSource? _generationCts;
        
        public SocialMediaViewModel()
        {
            // Initialize collections
            Brands = new ObservableCollection<string> { "Default", "Brand A", "Brand B" };
            Goals = new ObservableCollection<string> { "Traffic", "Sales", "Followers", "Engagement" };
            Durations = new ObservableCollection<string> { "3 days", "7 days", "14 days", "30 days" };
            Tones = new ObservableCollection<string> { "Professional", "Friendly", "Viral", "Premium", "Casual" };
            CtaStyles = new ObservableCollection<string> { "Soft", "Direct", "Urgent" };
            Platforms = new ObservableCollection<string> { "TikTok", "Instagram", "Facebook", "YouTube" };
            Formats = new ObservableCollection<string> { "Post", "Story", "Reel", "Short", "Long video" };
            
            // Dummy data
            LoadDummyData();
            
            // Commands
            NavigateCommand = new RelayCommand<string>(Navigate);
            GenerateCampaignCommand = new RelayCommand(GenerateCampaign);
            GeneratePostCommand = new RelayCommand(GeneratePost);
            SaveToLibraryCommand = new RelayCommand(SaveToLibrary);
            QueueForSchedulerCommand = new RelayCommand(QueueForScheduler);
            ApprovePostCommand = new RelayCommand<QueueItem>(ApprovePost);
            RejectPostCommand = new RelayCommand<QueueItem>(RejectPost);
            PublishPostCommand = new RelayCommand<QueueItem>(PublishPost);
            CopyContentCommand = new RelayCommand<string>(CopyContent);
            ToggleNavCommand = new RelayCommand(ToggleNav);
            CancelCommand = new RelayCommand(CancelGeneration);
        }
        
        #region Properties
        
        public string CurrentPage
        {
            get => _currentPage;
            set { _currentPage = value; OnPropertyChanged(); }
        }
        
        public string SelectedBrand
        {
            get => _selectedBrand;
            set { _selectedBrand = value; OnPropertyChanged(); }
        }
        
        public bool IsTikTokEnabled
        {
            get => _isTikTokEnabled;
            set { _isTikTokEnabled = value; OnPropertyChanged(); }
        }
        
        public bool IsInstagramEnabled
        {
            get => _isInstagramEnabled;
            set { _isInstagramEnabled = value; OnPropertyChanged(); }
        }
        
        public bool IsFacebookEnabled
        {
            get => _isFacebookEnabled;
            set { _isFacebookEnabled = value; OnPropertyChanged(); }
        }
        
        public bool IsYouTubeEnabled
        {
            get => _isYouTubeEnabled;
            set { _isYouTubeEnabled = value; OnPropertyChanged(); }
        }
        
        public bool IsNavCollapsed
        {
            get => _isNavCollapsed;
            set { _isNavCollapsed = value; OnPropertyChanged(); OnPropertyChanged(nameof(NavWidth)); }
        }
        
        public double NavWidth => IsNavCollapsed ? 56 : 180;
        
        // Campaign
        public string SelectedGoal { get => _selectedGoal; set { _selectedGoal = value; OnPropertyChanged(); } }
        public string SelectedDuration { get => _selectedDuration; set { _selectedDuration = value; OnPropertyChanged(); } }
        public string SelectedTone { get => _selectedTone; set { _selectedTone = value; OnPropertyChanged(); } }
        public string SelectedCta { get => _selectedCta; set { _selectedCta = value; OnPropertyChanged(); } }
        public string CampaignOutput { get => _campaignOutput; set { _campaignOutput = value; OnPropertyChanged(); } }
        
        // Create Post
        public string PostPlatform { get => _postPlatform; set { _postPlatform = value; OnPropertyChanged(); } }
        public string PostFormat { get => _postFormat; set { _postFormat = value; OnPropertyChanged(); } }
        public string PostPrompt { get => _postPrompt; set { _postPrompt = value; OnPropertyChanged(); } }
        public string GeneratedHook { get => _generatedHook; set { _generatedHook = value; OnPropertyChanged(); } }
        public string GeneratedBody { get => _generatedBody; set { _generatedBody = value; OnPropertyChanged(); } }
        public string GeneratedCta { get => _generatedCta; set { _generatedCta = value; OnPropertyChanged(); } }
        public string GeneratedHashtags { get => _generatedHashtags; set { _generatedHashtags = value; OnPropertyChanged(); } }
        public int VariantCount { get => _variantCount; set { _variantCount = value; OnPropertyChanged(); } }
        public bool IncludeHashtags { get => _includeHashtags; set { _includeHashtags = value; OnPropertyChanged(); } }
        public bool IncludeCta { get => _includeCta; set { _includeCta = value; OnPropertyChanged(); } }
        public string ImagePrompt { get => _imagePrompt; set { _imagePrompt = value; OnPropertyChanged(); } }
        public string AttachedImagePath { get => _attachedImagePath; set { _attachedImagePath = value; OnPropertyChanged(); } }
        
        // Scheduler
        public string SchedulerView { get => _schedulerView; set { _schedulerView = value; OnPropertyChanged(); } }
        
        // Library
        public string LibrarySearchQuery { get => _librarySearchQuery; set { _librarySearchQuery = value; OnPropertyChanged(); } }
        public string LibraryPlatformFilter { get => _libraryPlatformFilter; set { _libraryPlatformFilter = value; OnPropertyChanged(); } }
        public string LibraryTypeFilter { get => _libraryTypeFilter; set { _libraryTypeFilter = value; OnPropertyChanged(); } }
        public bool ShowBestPerforming { get => _showBestPerforming; set { _showBestPerforming = value; OnPropertyChanged(); } }
        public bool IsGridView { get => _isGridView; set { _isGridView = value; OnPropertyChanged(); } }
        
        // Collections
        public ObservableCollection<string> Brands { get; }
        public ObservableCollection<string> Goals { get; }
        public ObservableCollection<string> Durations { get; }
        public ObservableCollection<string> Tones { get; }
        public ObservableCollection<string> CtaStyles { get; }
        public ObservableCollection<string> Platforms { get; }
        public ObservableCollection<string> Formats { get; }
        public ObservableCollection<QueueItem> QueueItems { get; } = new();
        public ObservableCollection<ContentItem> LibraryItems { get; } = new();
        public ObservableCollection<AutomationRule> AutomationRules { get; } = new();
        public ObservableCollection<AutomationLog> AutomationLogs { get; } = new();
        
        #endregion
        
        #region Commands
        
        public ICommand NavigateCommand { get; }
        public ICommand GenerateCampaignCommand { get; }
        public ICommand GeneratePostCommand { get; }
        public ICommand SaveToLibraryCommand { get; }
        public ICommand QueueForSchedulerCommand { get; }
        public ICommand ApprovePostCommand { get; }
        public ICommand RejectPostCommand { get; }
        public ICommand PublishPostCommand { get; }
        public ICommand CopyContentCommand { get; }
        public ICommand ToggleNavCommand { get; }
        public ICommand CancelCommand { get; }
        
        #endregion
        
        #region Methods
        
        private void Navigate(string? page)
        {
            if (!string.IsNullOrEmpty(page))
                CurrentPage = page;
        }
        
        private void ToggleNav()
        {
            IsNavCollapsed = !IsNavCollapsed;
        }

        private void CancelGeneration()
        {
            _generationCts?.Cancel();
        }
        
        private async void GenerateCampaign()
        {
            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new System.Threading.CancellationTokenSource();
            var ct = _generationCts.Token;

            try
            {
                CampaignOutput = "Generating campaign plan...";

                var prompt = $@"Create a detailed social media campaign plan.

Goal: {SelectedGoal}
Duration: {SelectedDuration}
Tone: {SelectedTone}
CTA Style: {SelectedCta}
Platforms: {(IsTikTokEnabled ? "TikTok, " : "")}{(IsInstagramEnabled ? "Instagram, " : "")}{(IsFacebookEnabled ? "Facebook, " : "")}{(IsYouTubeEnabled ? "YouTube" : "")}

Provide a comprehensive campaign plan including:
1. Weekly posting schedule
2. Content themes and topics
3. Hook formulas to use
4. CTA strategy progression
5. Suggested content types and assets needed

Format with clear sections and bullet points.";

                var messages = new System.Collections.Generic.List<object>
                {
                    new { role = "system", content = "You are a social media marketing strategist. Create detailed, actionable campaign plans." },
                    new { role = "user", content = prompt }
                };

                var response = await AIManager.SendMessageAsync(messages, 2000, ct);

                if (response.Success && !string.IsNullOrEmpty(response.Content))
                {
                    CampaignOutput = response.Content;
                    LogAction("Campaign plan generated with AI");
                }
                else
                {
                    CampaignOutput = $"Failed to generate campaign plan.\n\nError: {response.Error ?? "Check your API key settings."}";
                }
            }
            catch (OperationCanceledException)
            {
                CampaignOutput = "CANCELLED · OPERATION STOPPED";
            }
            catch (Exception ex)
            {
                CampaignOutput = $"Error generating campaign: {ex.Message}";
            }
            finally
            {
                if (_generationCts?.Token == ct)
                {
                    _generationCts?.Dispose();
                    _generationCts = null;
                }
            }
        }
        
        private async void GeneratePost()
        {
            if (string.IsNullOrWhiteSpace(PostPrompt))
            {
                GeneratedHook = "Please enter a topic or prompt first.";
                return;
            }

            _generationCts?.Cancel();
            _generationCts?.Dispose();
            _generationCts = new System.Threading.CancellationTokenSource();
            var ct = _generationCts.Token;

            try
            {
                GeneratedHook = "Generating content...";
                GeneratedBody = "";
                GeneratedCta = "";
                GeneratedHashtags = "";

                var prompt = $@"Create a {PostPlatform} {PostFormat} about: {PostPrompt}

Tone: Professional but engaging
{(IncludeHashtags ? "Include relevant hashtags" : "")}
{(IncludeCta ? "Include a call to action" : "")}

Provide the response in this exact format:
HOOK: [attention-grabbing first line with emoji]
BODY: [main content, 2-3 short paragraphs]
CTA: [call to action]
{(IncludeHashtags ? "HASHTAGS: [5-10 relevant hashtags]" : "")}";

                var messages = new System.Collections.Generic.List<object>
                {
                    new { role = "system", content = "You are a social media marketing expert. Create engaging, platform-optimized content that drives engagement." },
                    new { role = "user", content = prompt }
                };

                var response = await AIManager.SendMessageAsync(messages, 1500, ct);

                if (response.Success && !string.IsNullOrEmpty(response.Content))
                {
                    ParseGeneratedContent(response.Content);
                    LogAction("Post content generated with AI");
                }
                else
                {
                    GeneratedHook = "Failed to generate content. Check your API key settings.";
                    GeneratedBody = response.Error ?? "Unknown error occurred.";
                }
            }
            catch (OperationCanceledException)
            {
                GeneratedHook = "CANCELLED · OPERATION STOPPED";
                GeneratedBody = "";
            }
            catch (Exception ex)
            {
                GeneratedHook = "Error generating content";
                GeneratedBody = ex.Message;
            }
            finally
            {
                if (_generationCts?.Token == ct)
                {
                    _generationCts?.Dispose();
                    _generationCts = null;
                }
            }
        }

        private void ParseGeneratedContent(string response)
        {
            var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var currentSection = "";
            var bodyLines = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                var upper = trimmed.ToUpper();

                if (upper.StartsWith("HOOK:"))
                {
                    GeneratedHook = trimmed.Substring(5).Trim();
                    currentSection = "";
                }
                else if (upper.StartsWith("BODY:"))
                {
                    bodyLines.Add(trimmed.Substring(5).Trim());
                    currentSection = "body";
                }
                else if (upper.StartsWith("CTA:"))
                {
                    GeneratedCta = trimmed.Substring(4).Trim();
                    currentSection = "";
                }
                else if (upper.StartsWith("HASHTAGS:"))
                {
                    GeneratedHashtags = trimmed.Substring(9).Trim();
                    currentSection = "";
                }
                else if (currentSection == "body" && !string.IsNullOrWhiteSpace(trimmed))
                {
                    bodyLines.Add(trimmed);
                }
            }

            GeneratedBody = string.Join("\n\n", bodyLines);
        }
        
        private void SaveToLibrary()
        {
            LibraryItems.Add(new ContentItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = GeneratedHook.Length > 40 ? GeneratedHook.Substring(0, 40) + "..." : GeneratedHook,
                Platform = PostPlatform,
                Type = PostFormat,
                CreatedAt = DateTime.Now,
                Hook = GeneratedHook,
                Body = GeneratedBody,
                Cta = GeneratedCta,
                Hashtags = GeneratedHashtags
            });
            LogAction("Content saved to library");
        }
        
        private void QueueForScheduler()
        {
            QueueItems.Add(new QueueItem
            {
                Id = Guid.NewGuid().ToString(),
                Title = GeneratedHook.Length > 40 ? GeneratedHook.Substring(0, 40) + "..." : GeneratedHook,
                Platform = PostPlatform,
                Status = "Needs Approval",
                ScheduledTime = DateTime.Now.AddDays(1),
                Content = GeneratedBody
            });
            LogAction("Content queued for scheduling");
        }
        
        private void ApprovePost(QueueItem? item)
        {
            if (item != null)
            {
                item.Status = "Approved";
                OnPropertyChanged(nameof(QueueItems));
                LogAction($"Post approved: {item.Title}");
            }
        }
        
        private void RejectPost(QueueItem? item)
        {
            if (item != null)
            {
                item.Status = "Draft";
                OnPropertyChanged(nameof(QueueItems));
                LogAction($"Post rejected: {item.Title}");
            }
        }
        
        private void PublishPost(QueueItem? item)
        {
            if (item != null && item.Status == "Approved")
            {
                item.Status = "Published";
                OnPropertyChanged(nameof(QueueItems));
                LogAction($"Post published: {item.Title}");
            }
        }
        
        private void CopyContent(string? content)
        {
            if (!string.IsNullOrEmpty(content))
            {
                System.Windows.Clipboard.SetText(content);
                LogAction("Content copied to clipboard");
            }
        }
        
        private void LogAction(string action)
        {
            AutomationLogs.Insert(0, new AutomationLog
            {
                Timestamp = DateTime.Now,
                Action = action,
                Status = "Success"
            });
        }
        
        private void LoadDummyData()
        {
            // Queue items
            QueueItems.Add(new QueueItem { Id = "1", Title = "10x your productivity with this...", Platform = "Instagram", Status = "Needs Approval", ScheduledTime = DateTime.Now.AddDays(1) });
            QueueItems.Add(new QueueItem { Id = "2", Title = "POV: You discovered the secret", Platform = "TikTok", Status = "Approved", ScheduledTime = DateTime.Now.AddDays(2) });
            QueueItems.Add(new QueueItem { Id = "3", Title = "Why most people fail at...", Platform = "Facebook", Status = "Scheduled", ScheduledTime = DateTime.Now.AddDays(3) });
            QueueItems.Add(new QueueItem { Id = "4", Title = "The truth about success", Platform = "YouTube", Status = "Published", ScheduledTime = DateTime.Now.AddDays(-1) });
            
            // Library items
            LibraryItems.Add(new ContentItem { Id = "1", Title = "Productivity hack carousel", Platform = "Instagram", Type = "Post", CreatedAt = DateTime.Now.AddDays(-5) });
            LibraryItems.Add(new ContentItem { Id = "2", Title = "Morning routine reel", Platform = "TikTok", Type = "Reel", CreatedAt = DateTime.Now.AddDays(-3) });
            LibraryItems.Add(new ContentItem { Id = "3", Title = "Business tips thread", Platform = "Facebook", Type = "Post", CreatedAt = DateTime.Now.AddDays(-2) });
            
            // Automation rules
            AutomationRules.Add(new AutomationRule { Name = "Auto-generate weekly ideas", IsEnabled = true });
            AutomationRules.Add(new AutomationRule { Name = "Reuse best-performing hooks", IsEnabled = true });
            AutomationRules.Add(new AutomationRule { Name = "Require approval before scheduling", IsEnabled = true });
            AutomationRules.Add(new AutomationRule { Name = "Posting time limits (9AM-9PM)", IsEnabled = false });
        }
        
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        
        #endregion
    }
    
    #region Data Models
    
    public class QueueItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Platform { get; set; } = "";
        private string _status = "";
        public string Status 
        { 
            get => _status; 
            set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
        }
        public DateTime ScheduledTime { get; set; }
        public string Content { get; set; } = "";
    }
    
    public class ContentItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string Hook { get; set; } = "";
        public string Body { get; set; } = "";
        public string Cta { get; set; } = "";
        public string Hashtags { get; set; } = "";
    }
    
    public class AutomationRule : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public string Name { get; set; } = "";
        private bool _isEnabled;
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
        }
    }
    
    public class AutomationLog
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = "";
        public string Status { get; set; } = "";
    }
    
    #endregion
    
    #region RelayCommand
    
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        
        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
    }
    
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;
        
        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        
        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
        public void Execute(object? parameter) => _execute((T?)parameter);
    }
    
    #endregion
}
