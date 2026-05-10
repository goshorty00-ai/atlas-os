using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AtlasAI.SocialMedia.Models;
using AtlasAI.SocialMedia.Services;

namespace AtlasAI.SocialMedia
{
    public partial class SocialMediaConsoleWindow : Window
    {
        private readonly SocialMediaMemoryService _memoryService;
        private readonly ContentGeneratorService _contentGenerator;
        private readonly CampaignBuilderService _campaignBuilder;
        private readonly SchedulerService _scheduler;
        private readonly PlatformPublisherService _publisher;
        
        private string _activeTab = "campaigns";
        private SocialContent? _lastGeneratedContent;
        
        public SocialMediaConsoleWindow()
        {
            InitializeComponent();
            
            _memoryService = new SocialMediaMemoryService();
            _contentGenerator = new ContentGeneratorService(_memoryService);
            _publisher = new PlatformPublisherService();
            _campaignBuilder = new CampaignBuilderService(_memoryService, _contentGenerator);
            _scheduler = new SchedulerService(_memoryService, _publisher);
            
            _scheduler.Start();
            Loaded += async (s, e) => await RefreshCurrentTabAsync();
            SetActiveTab("campaigns");
        }
        
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else
                DragMove();
        }
        
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        
        private async void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tab)
            {
                SetActiveTab(tab);
                await RefreshCurrentTabAsync();
            }
        }
        
        private void SetActiveTab(string tab)
        {
            _activeTab = tab;
            CampaignsPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Collapsed;
            SchedulerPanel.Visibility = Visibility.Collapsed;
            BrandsPanel.Visibility = Visibility.Collapsed;
            GeneratePanel.Visibility = Visibility.Collapsed;
            
            var tabs = new[] { TabCampaigns, TabContent, TabScheduler, TabBrands, TabGenerate };
            foreach (var t in tabs)
            {
                t.Foreground = new SolidColorBrush(Color.FromRgb(125, 133, 143));
            }
            
            Button activeTab;
            switch (tab)
            {
                case "campaigns":
                    CampaignsPanel.Visibility = Visibility.Visible;
                    activeTab = TabCampaigns;
                    break;
                case "content":
                    ContentPanel.Visibility = Visibility.Visible;
                    activeTab = TabContent;
                    break;
                case "scheduler":
                    SchedulerPanel.Visibility = Visibility.Visible;
                    activeTab = TabScheduler;
                    break;
                case "brands":
                    BrandsPanel.Visibility = Visibility.Visible;
                    activeTab = TabBrands;
                    break;
                case "generate":
                    GeneratePanel.Visibility = Visibility.Visible;
                    activeTab = TabGenerate;
                    break;
                default:
                    return;
            }
            activeTab.Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243));
        }
        
        private async Task RefreshCurrentTabAsync()
        {
            switch (_activeTab)
            {
                case "campaigns": await LoadCampaignsAsync(); break;
                case "content": await LoadContentAsync(); break;
                case "scheduler": await LoadSchedulesAsync(); break;
                case "brands": await LoadBrandsAsync(); break;
            }
        }
        
        private async Task LoadCampaignsAsync()
        {
            var campaigns = await _memoryService.GetAllCampaignsAsync();
            CampaignsList.ItemsSource = campaigns;
        }
        
        private async void NewCampaign_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CampaignDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                var campaign = await _campaignBuilder.CreateCampaignAsync(
                    dialog.BrandId, dialog.CampaignName, dialog.Goal,
                    dialog.Platforms, dialog.StartDate, dialog.EndDate);
                await LoadCampaignsAsync();
                MessageBox.Show($"Campaign '{campaign.Name}' created!", "Success");
            }
        }
        
        private void ViewCampaign_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
                MessageBox.Show($"Campaign: {id}", "View");
        }
        
        private async void ActivateCampaign_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                await _campaignBuilder.ActivateCampaignAsync(id);
                await LoadCampaignsAsync();
            }
        }
        
        private async Task LoadContentAsync()
        {
            var content = await _memoryService.GetAllContentAsync();
            var filterIndex = ContentStatusFilter.SelectedIndex;
            if (filterIndex > 0)
            {
                var status = filterIndex switch { 1 => ContentStatus.Draft, 2 => ContentStatus.Scheduled, 3 => ContentStatus.Published, _ => ContentStatus.Draft };
                content = content.Where(c => c.Status == status).ToList();
            }
            ContentList.ItemsSource = content;
        }
        
        private async void ContentFilter_Changed(object sender, SelectionChangedEventArgs e) => await LoadContentAsync();
        private void CreateContent_Click(object sender, RoutedEventArgs e) => SetActiveTab("generate");
        private void EditContent_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is string id) MessageBox.Show($"Edit: {id}"); }
        
        private async void ScheduleContent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                var dialog = new ScheduleDialog();
                dialog.Owner = this;
                if (dialog.ShowDialog() == true)
                {
                    await _scheduler.ScheduleAsync(id, dialog.Platform, dialog.ScheduledTime);
                    MessageBox.Show("Scheduled!", "Success");
                }
            }
        }
        
        private async Task LoadSchedulesAsync()
        {
            var schedules = await _scheduler.GetUpcomingAsync(30);
            ScheduleList.ItemsSource = schedules;
        }
        
        private async void ApproveSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                await _scheduler.ApproveAsync(id);
                await LoadSchedulesAsync();
            }
        }
        
        private async void CancelSchedule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string id)
            {
                await _scheduler.CancelAsync(id);
                await LoadSchedulesAsync();
            }
        }
        
        private async Task LoadBrandsAsync()
        {
            var brands = await _memoryService.GetAllBrandsAsync();
            BrandsList.ItemsSource = brands;
        }
        
        private async void AddBrand_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new BrandDialog();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true)
            {
                var brand = new BrandProfile
                {
                    Name = dialog.BrandName,
                    Description = dialog.Description,
                    Industry = dialog.Industry,
                    TargetAudience = dialog.TargetAudience,
                    DefaultTone = dialog.DefaultTone,
                    VoiceKeywords = dialog.VoiceKeywords,
                    DoNotSayPhrases = dialog.DoNotSayPhrases
                };
                await _memoryService.SaveBrandAsync(brand);
                await LoadBrandsAsync();
            }
        }
        
        private void EditBrand_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Tag is string id) MessageBox.Show($"Edit: {id}"); }
        
        private async void Generate_Click(object sender, RoutedEventArgs e)
        {
            var topic = GenerateTopic.Text.Trim();
            if (string.IsNullOrEmpty(topic)) { MessageBox.Show("Enter a topic."); return; }
            
            GeneratedOutput.Text = "Generating...";
            try
            {
                var platforms = new List<SocialPlatform>();
                if (GenerateForTikTok.IsChecked == true) platforms.Add(SocialPlatform.TikTok);
                if (GenerateForInstagram.IsChecked == true) platforms.Add(SocialPlatform.Instagram);
                if (GenerateForFacebook.IsChecked == true) platforms.Add(SocialPlatform.Facebook);
                if (GenerateForYouTube.IsChecked == true) platforms.Add(SocialPlatform.YouTube);
                if (platforms.Count == 0) platforms.Add(SocialPlatform.Instagram);
                
                var tone = (ContentTone)GenerateTone.SelectedIndex;
                var typeIndex = GenerateType.SelectedIndex;
                string output;
                
                switch (typeIndex)
                {
                    case 0:
                        var posts = await _contentGenerator.GeneratePostAsync(new ContentGenerationRequest { Topic = topic, Platforms = platforms, Tone = tone, Type = ContentType.Post, IncludeHashtags = true, IncludeCTA = true });
                        if (posts.Any()) { _lastGeneratedContent = posts.First(); output = $"HOOK:\n{_lastGeneratedContent.Hook}\n\nBODY:\n{_lastGeneratedContent.Body}\n\nCTA:\n{_lastGeneratedContent.CallToAction}\n\nHASHTAGS:\n{string.Join(" ", _lastGeneratedContent.Hashtags)}"; }
                        else output = "Failed to generate.";
                        break;
                    case 1:
                        var script = await _contentGenerator.GenerateVideoScriptAsync("", topic, platforms.First(), tone, 30);
                        output = $"HOOK:\n{script.Hook}\n\nCTA:\n{script.CallToAction}";
                        break;
                    case 2:
                        var ad = await _contentGenerator.GenerateAdCopyAsync("", topic, CampaignGoal.Engagement, platforms.First(), tone, "", 3);
                        output = string.Join("\n\n", ad.Variants.Select((v, i) => $"VARIANT {i + 1}:\n{v.PrimaryText}\nHeadline: {v.Headline}"));
                        break;
                    case 3:
                        var (broad, niche) = await _contentGenerator.GenerateHashtagsAsync(new HashtagGenerationRequest { Topic = topic, Platform = platforms.First(), BroadCount = 10, NicheCount = 10 });
                        output = $"BROAD:\n{string.Join(" ", broad)}\n\nNICHE:\n{string.Join(" ", niche)}";
                        break;
                    default:
                        output = "Unknown type";
                        break;
                }
                GeneratedOutput.Text = output;
            }
            catch (Exception ex) { GeneratedOutput.Text = $"Error: {ex.Message}"; }
        }
        
        private void CopyGenerated_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(GeneratedOutput.Text))
            {
                Clipboard.SetText(GeneratedOutput.Text);
                MessageBox.Show("Copied!");
            }
        }
        
        private async void SaveAsDraft_Click(object sender, RoutedEventArgs e)
        {
            if (_lastGeneratedContent != null)
            {
                _lastGeneratedContent.Title = GenerateTopic.Text.Trim();
                await _memoryService.SaveContentAsync(_lastGeneratedContent);
                MessageBox.Show("Saved!");
            }
            else MessageBox.Show("Generate content first.");
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _scheduler.Dispose();
            base.OnClosed(e);
        }
    }
}
