using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using AtlasAI.SocialMedia.Services;
using AtlasAI.SocialMedia.UI.ViewModels;
using Microsoft.Win32;

namespace AtlasAI.SocialMedia.UI
{
    public partial class SocialMediaPanel : UserControl
    {
        private readonly SocialMediaViewModel _viewModel;
        private readonly ImageGenerationService _imageService;
        private ImageGenerationResult _lastGeneratedImage;
        
        public SocialMediaPanel()
        {
            InitializeComponent();
            _viewModel = new SocialMediaViewModel();
            _imageService = new ImageGenerationService();
            DataContext = _viewModel;
            
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
        
        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SocialMediaViewModel.CurrentPage))
            {
                UpdatePageVisibility();
            }
        }
        
        private void UpdatePageVisibility()
        {
            // Hide all pages
            CampaignsPage.Visibility = Visibility.Collapsed;
            CreatePostPage.Visibility = Visibility.Collapsed;
            SchedulerPage.Visibility = Visibility.Collapsed;
            ContentLibraryPage.Visibility = Visibility.Collapsed;
            AnalyticsPage.Visibility = Visibility.Collapsed;
            AutomationsPage.Visibility = Visibility.Collapsed;
            
            // Show selected page
            switch (_viewModel.CurrentPage)
            {
                case "Campaigns":
                    CampaignsPage.Visibility = Visibility.Visible;
                    break;
                case "Create Post":
                    CreatePostPage.Visibility = Visibility.Visible;
                    break;
                case "Scheduler":
                    SchedulerPage.Visibility = Visibility.Visible;
                    break;
                case "Content Library":
                    ContentLibraryPage.Visibility = Visibility.Visible;
                    break;
                case "Analytics":
                    AnalyticsPage.Visibility = Visibility.Visible;
                    break;
                case "Automations":
                    AutomationsPage.Visibility = Visibility.Visible;
                    break;
            }
        }

        private async void GenerateImage_Click(object sender, RoutedEventArgs e)
        {
            var prompt = ImagePromptBox?.Text?.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                ImageGenStatus.Text = "Please enter an image prompt";
                return;
            }

            if (!_imageService.HasApiKey)
            {
                ImageGenStatus.Text = "AI is not configured on this installation (admin-only).";
                return;
            }

            try
            {
                GenerateImageBtn.IsEnabled = false;
                ImageGenStatus.Text = "Generating image... This may take 15-30 seconds.";
                ImagePreviewBorder.Visibility = Visibility.Collapsed;

                // Get selected size
                var sizeItem = ImageSizeCombo.SelectedItem as ComboBoxItem;
                var sizeTag = sizeItem?.Tag?.ToString() ?? "Square";
                var size = sizeTag switch
                {
                    "Portrait" => ImageSize.Portrait,
                    "Landscape" => ImageSize.Landscape,
                    _ => ImageSize.Square
                };

                var request = new ImageGenerationRequest
                {
                    Prompt = prompt,
                    Platform = _viewModel.PostPlatform,
                    Size = size,
                    HighQuality = HighQualityCheck?.IsChecked ?? true,
                    Style = ImageStyle.Vivid
                };

                var result = await _imageService.GenerateImageAsync(request);

                if (result.Success)
                {
                    _lastGeneratedImage = result;
                    GeneratedImagePreview.Source = result.GetBitmapImage();
                    ImagePreviewBorder.Visibility = Visibility.Visible;
                    ImageGenStatus.Text = $"Image generated! Saved to: {result.LocalPath}";
                }
                else
                {
                    ImageGenStatus.Text = result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                ImageGenStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                GenerateImageBtn.IsEnabled = true;
            }
        }

        private void AttachImage_Click(object sender, RoutedEventArgs e)
        {
            if (_lastGeneratedImage?.LocalPath != null)
            {
                // Set the image path in the view model for the post
                _viewModel.AttachedImagePath = _lastGeneratedImage.LocalPath;
                ImageGenStatus.Text = "Image attached to post!";
            }
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (_lastGeneratedImage?.ImageBytes == null)
                return;

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                FileName = $"social_media_image_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    System.IO.File.WriteAllBytes(dialog.FileName, _lastGeneratedImage.ImageBytes);
                    ImageGenStatus.Text = $"Saved to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    ImageGenStatus.Text = $"Save failed: {ex.Message}";
                }
            }
        }
    }
}
