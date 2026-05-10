// Figma Component: AIRecommendationCard
// Design URL: https://www.figma.com/make/d4uCeNSISrzcORhzKRF76b/Atlas.MediaCentre.Main--Copy---Copy-

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace AtlasAI.Controls
{
    public partial class AIRecommendationCard : UserControl
    {
        public event EventHandler? Clicked;
        
        public AIRecommendationCard()
        {
            InitializeComponent();
            this.MouseLeftButtonUp += (s, e) => Clicked?.Invoke(this, EventArgs.Empty);
        }
        
        public void SetRecommendation(string title, string type, int matchPercentage, string reason, string? imageUrl = null)
        {
            TitleText.Text = title;
            TypeText.Text = type;
            MatchText.Text = $"{matchPercentage}%";
            ReasonText.Text = reason;
            
            if (!string.IsNullOrEmpty(imageUrl))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imageUrl, UriKind.RelativeOrAbsolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    ThumbnailImage.Source = bitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AIRecommendation] Failed to load image: {ex.Message}");
                }
            }
        }
    }
}
