// Figma Component: MediaCarousel
// Design URL: https://www.figma.com/make/d4uCeNSISrzcORhzKRF76b/Atlas.MediaCentre.Main--Copy---Copy-

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using AtlasAI.MediaScanner;

namespace AtlasAI.Controls
{
    public partial class MediaCarouselControl : UserControl
    {
        public event EventHandler<MediaCardItem>? MediaClicked;
        
        private List<MediaCardItem> _mediaItems = new List<MediaCardItem>();
        
        public MediaCarouselControl()
        {
            InitializeComponent();
        }
        
        public void SetTitle(string title)
        {
            CarouselTitle.Text = title;
        }
        
        public void SetMediaItems(List<MediaCardItem> items)
        {
            _mediaItems = items;
            CarouselPanel.Children.Clear();
            
            foreach (var item in items)
            {
                var card = new MediaCardControl();
                card.SetMedia(item);
                card.Width = 200; // Fixed width for 3-column grid
                card.Margin = new Thickness(0, 0, 16, 16); // Right and bottom spacing
                card.MediaClicked += Card_MediaClicked;
                CarouselPanel.Children.Add(card);
            }
        }
        
        private void Card_MediaClicked(object? sender, MediaCardItem e)
        {
            MediaClicked?.Invoke(this, e);
        }
    }
}
