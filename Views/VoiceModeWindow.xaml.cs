using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AtlasAI.Voice;

namespace AtlasAI.Views
{
    public partial class VoiceModeWindow : Window
    {
        private bool _isLoading;
        private readonly VoiceManager _fallbackVoiceManager = new VoiceManager();

        public VoiceModeWindow()
        {
            InitializeComponent();
            Loaded += VoiceModeWindow_Loaded;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            }
            catch
            {
            }
        }

        private async void VoiceModeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            try
            {
                await PopulateVoicesAsync(forceRefresh: false);
                SelectCurrentVoice();
            }
            catch
            {
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async Task PopulateVoicesAsync(bool forceRefresh)
        {
            VoiceCombo.Items.Clear();

            var vm = TryGetVoiceManager() ?? _fallbackVoiceManager;
            try
            {
                var keys = global::AtlasAI.SettingsWindow.GetVoiceApiKeys();
                if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrWhiteSpace(elevenKey) && vm != null)
                {
                    vm.ConfigureProvider(AtlasAI.Voice.VoiceProviderType.ElevenLabs, new System.Collections.Generic.Dictionary<string, string> { ["ApiKey"] = elevenKey });
                    await vm.SetProviderAsync(AtlasAI.Voice.VoiceProviderType.ElevenLabs);
                }
            }
            catch
            {
            }

            VoiceCombo.Items.Add(new ComboBoxItem { Content = "Default (personality)", Tag = "" });

            IReadOnlyList<VoiceInfo> voices;
            try
            {
                if (forceRefresh)
                    vm?.RefreshVoices();
                voices = await vm.GetVoicesAsync();
            }
            catch
            {
                voices = Array.Empty<VoiceInfo>();
            }

            var ordered = voices
                .Where(v => v != null && !string.IsNullOrWhiteSpace(v.Id))
                .OrderByDescending(v => string.Equals(v.Category, "My Voices", StringComparison.OrdinalIgnoreCase))
                .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                try
                {
                    if (forceRefresh)
                        await VoiceCatalogService.Instance.RefreshAsync();
                    var catalog = await VoiceCatalogService.Instance.GetVoicesAsync();
                    ordered = catalog
                        .Where(v => v != null && !string.IsNullOrWhiteSpace(v.VoiceId))
                        .Select(v => new VoiceInfo
                        {
                            Id = v.VoiceId,
                            DisplayName = v.DisplayName,
                            Provider = VoiceProviderType.ElevenLabs,
                            Category = "Catalog"
                        })
                        .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    ordered = new System.Collections.Generic.List<VoiceInfo>();
                }
            }

            foreach (var v in ordered)
            {
                VoiceCombo.Items.Add(new ComboBoxItem
                {
                    Content = $"{v.DisplayName} ({v.Id})",
                    Tag = v.Id
                });
            }
        }

        private void SelectCurrentVoice()
        {
            var current = VoicePreferences.Current.GlobalVoiceId ?? "";
            for (var i = 0; i < VoiceCombo.Items.Count; i++)
            {
                if (VoiceCombo.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString() ?? "", current, StringComparison.OrdinalIgnoreCase))
                {
                    VoiceCombo.SelectedIndex = i;
                    return;
                }
            }

            if (VoiceCombo.Items.Count > 0)
                VoiceCombo.SelectedIndex = 0;
        }

        private static VoiceManager? TryGetVoiceManager()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is AtlasAI.CommandCenterWindow commandCenter)
                        return commandCenter.VoiceManager;
                    if (window is ChatWindow chatWindow)
                        return chatWindow.VoiceManager;
                }
            }
            catch
            {
            }
            return null;
        }

        private async void VoiceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;

            try
            {
                if (VoiceCombo.SelectedItem is not ComboBoxItem item) return;
                var voiceId = (item.Tag?.ToString() ?? "").Trim();
                VoicePreferences.Current.SetGlobalVoice(string.IsNullOrWhiteSpace(voiceId) ? null : voiceId);

                var vm = TryGetVoiceManager() ?? _fallbackVoiceManager;

                if (string.IsNullOrWhiteSpace(voiceId)) return;

                try
                {
                    var keys = global::AtlasAI.SettingsWindow.GetVoiceApiKeys();
                    if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrWhiteSpace(elevenKey))
                        vm.ConfigureProvider(AtlasAI.Voice.VoiceProviderType.ElevenLabs, new System.Collections.Generic.Dictionary<string, string> { ["ApiKey"] = elevenKey });
                }
                catch
                {
                }

                await vm.SetProviderAsync(VoiceProviderType.ElevenLabs);
                await vm.SelectVoiceAsync(voiceId);
            }
            catch
            {
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            _isLoading = true;
            try
            {
                var vm = TryGetVoiceManager();
                try
                {
                    vm?.RefreshVoices();
                }
                catch
                {
                }

                await PopulateVoicesAsync(forceRefresh: true);
                SelectCurrentVoice();
            }
            catch
            {
            }
            finally
            {
                _isLoading = false;
            }
        }

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = TryGetVoiceManager() ?? _fallbackVoiceManager;
                try
                {
                    var keys = global::AtlasAI.SettingsWindow.GetVoiceApiKeys();
                    if (keys.TryGetValue("elevenlabs", out var elevenKey) && !string.IsNullOrWhiteSpace(elevenKey))
                        vm.ConfigureProvider(AtlasAI.Voice.VoiceProviderType.ElevenLabs, new System.Collections.Generic.Dictionary<string, string> { ["ApiKey"] = elevenKey });
                }
                catch
                {
                }
                await vm.SetProviderAsync(VoiceProviderType.ElevenLabs);
                if (!vm.SpeechEnabled) vm.SpeechEnabled = true;
                if (vm.Volume < 0.05) vm.Volume = 1.0;
                var voiceId = "";
                if (VoiceCombo.SelectedItem is ComboBoxItem item)
                    voiceId = (item.Tag?.ToString() ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(voiceId))
                    await vm.SelectVoiceAsync(voiceId);

                await vm.SpeakAsync($"Voice preview {DateTime.Now:HHmmss}.");
            }
            catch
            {
            }
        }
    }
}
