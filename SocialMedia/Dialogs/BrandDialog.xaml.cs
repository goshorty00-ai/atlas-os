using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AtlasAI.SocialMedia.Models;

namespace AtlasAI.SocialMedia
{
    public partial class BrandDialog : Window
    {
        public string BrandName { get; private set; } = "";
        public string Description { get; private set; } = "";
        public string Industry { get; private set; } = "";
        public string TargetAudience { get; private set; } = "";
        public ContentTone DefaultTone { get; private set; }
        public List<string> VoiceKeywords { get; private set; } = new();
        public List<string> DoNotSayPhrases { get; private set; } = new();
        
        public BrandDialog()
        {
            InitializeComponent();
        }
        
        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text))
            {
                MessageBox.Show("Please enter a brand name.", "Missing Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            BrandName = NameBox.Text.Trim();
            Description = DescriptionBox.Text.Trim();
            Industry = IndustryBox.Text.Trim();
            TargetAudience = AudienceBox.Text.Trim();
            DefaultTone = (ContentTone)ToneBox.SelectedIndex;
            
            VoiceKeywords = KeywordsBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
            
            DoNotSayPhrases = DoNotSayBox.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            
            DialogResult = true;
            Close();
        }
        
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
