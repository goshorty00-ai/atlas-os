// Figma Component: MediaCard
// Design URL: https://www.figma.com/make/d4uCeNSISrzcORhzKRF76b/Atlas.MediaCentre.Main--Copy---Copy-

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AtlasAI.MediaScanner;

namespace AtlasAI.Controls
{
    public partial class MediaCardControl : UserControl
    {
        public event EventHandler<MediaCardItem>? MediaClicked;
        
        private MediaCardItem? _mediaItem;
        
        public MediaCardControl()
        {
            InitializeComponent();
            this.MouseLeftButtonUp += MediaCardControl_MouseLeftButtonUp;
        }
        
        public void SetMedia(MediaCardItem mediaItem)
        {
            _mediaItem = mediaItem;

            PosterImage.Stretch = string.Equals(mediaItem.Genre, "Album", StringComparison.OrdinalIgnoreCase)
                ? Stretch.Uniform
                : Stretch.UniformToFill;
            
            // Set title
            TitleText.Text = mediaItem.Title;
            
            // Set year
            YearText.Text = mediaItem.Year.ToString();
            
            // Set metadata
            var meta = "";
            if (mediaItem.SeasonNumber.HasValue && mediaItem.EpisodeNumber.HasValue)
            {
                meta += $"S{mediaItem.SeasonNumber} E{mediaItem.EpisodeNumber} ";
            }
            
            if (!string.IsNullOrEmpty(mediaItem.Metadata))
            {
                meta += string.IsNullOrEmpty(meta) ? mediaItem.Metadata : $"• {mediaItem.Metadata}";
            }
            
            if (!string.IsNullOrEmpty(meta))
            {
                MetadataText.Text = meta;
                MetadataText.Visibility = Visibility.Visible;
            }
            else
            {
                MetadataText.Visibility = Visibility.Collapsed;
            }
            
            // Set rating
            if (mediaItem.Rating > 0)
            {
                RatingBadge.Visibility = Visibility.Visible;
                RatingText.Text = mediaItem.Rating.ToString("F1");
            }
            else
            {
                RatingBadge.Visibility = Visibility.Collapsed;
            }
            
            // Set genre
            if (!string.IsNullOrEmpty(mediaItem.Genre))
            {
                GenreBadge.Visibility = Visibility.Visible;
                GenreText.Text = mediaItem.Genre.ToUpper();
            }
            
            // Set progress bar
            if (mediaItem.Progress > 0)
            {
                ProgressBarContainer.Visibility = Visibility.Visible;
                ProgressBar.Width = 200 * (mediaItem.Progress / 100.0);
                
                // Update Progress Ring
                UpdateProgressRing(mediaItem.Progress);
                
                // Episode Context
                if (mediaItem.SeasonNumber.HasValue && mediaItem.EpisodeNumber.HasValue)
                {
                    EpisodeContextText.Text = $"Continue S{mediaItem.SeasonNumber:D2}E{mediaItem.EpisodeNumber:D2}";
                    EpisodeContextText.Visibility = Visibility.Visible;
                }
                else
                {
                    EpisodeContextText.Visibility = Visibility.Collapsed;
                }

                // Runtime Remaining
                if (!string.IsNullOrEmpty(mediaItem.RuntimeRemaining))
                {
                    RuntimeRemainingText.Text = mediaItem.RuntimeRemaining;
                    RuntimeRemainingText.Visibility = Visibility.Visible;
                }
                else
                {
                    RuntimeRemainingText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ProgressBarContainer.Visibility = Visibility.Collapsed;
                UpdateProgressRing(0);
                EpisodeContextText.Visibility = Visibility.Collapsed;
                RuntimeRemainingText.Visibility = Visibility.Collapsed;
            }

            // Recent Highlight
            if (RecentHighlight != null)
            {
                RecentHighlight.Visibility = mediaItem.IsRecent ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Load image
            if (!string.IsNullOrEmpty(mediaItem.ImageUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(mediaItem.ImageUrl, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    PosterImage.Source = bitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MediaCard] Failed to load image: {ex.Message}");
                }
            }
        }
        
        private void MediaCardControl_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_mediaItem != null)
            {
                MediaClicked?.Invoke(this, _mediaItem);
            }
        }
        
        private void UpdateProgressRing(double percent)
        {
            if (ProgressRing == null) return;
            
            if (percent <= 0)
            {
                ProgressRing.Visibility = Visibility.Collapsed;
                return;
            }
            
            ProgressRing.Visibility = Visibility.Visible;
            
            // Calculate arc for 48x48 box (radius 24)
            double radius = 24;
            double angle = (percent / 100.0) * 360.0;
            if (angle >= 360) angle = 359.99; 
            
            double rad = (angle - 90) * Math.PI / 180.0;
            double x = 24 + radius * Math.Cos(rad);
            double y = 24 + radius * Math.Sin(rad);
            
            bool isLargeArc = angle > 180.0;
            
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure();
            pathFigure.StartPoint = new Point(24, 0); 
            
            var arcSegment = new ArcSegment(
                new Point(x, y),
                new Size(radius, radius),
                0,
                isLargeArc,
                SweepDirection.Clockwise,
                true);
                
            pathFigure.Segments.Add(arcSegment);
            pathGeometry.Figures.Add(pathFigure);
            
            ProgressRing.Data = pathGeometry;
        }
    }
}
