using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AtlasAI.AI;

namespace AtlasAI.Views
{
    public partial class AiModeWindow : Window
    {
        private bool _isLoading;

        public AiModeWindow()
        {
            InitializeComponent();
            Loaded += AiModeWindow_Loaded;
        }

        private async void AiModeWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoading = true;
            try
            {
                AutoModeCheckBox.IsChecked = AIManager.GetAutoModeEnabled();
                PopulateProviders();
                SelectCurrentProvider();
                await PopulateModelsAsync();
                SelectCurrentModel();
                SelectCurrentAutoModels();
                UpdateModelUiEnabled();
            }
            catch
            {
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void PopulateProviders()
        {
            ProviderCombo.Items.Clear();
            var providers = AIManager.GetAllProviders()
                .OrderBy(p => p.ProviderType == AIProviderType.Gemini ? 0 : 1)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

            foreach (var p in providers)
            {
                var configured = p.IsConfigured;
                var label = configured ? p.DisplayName : $"{p.DisplayName} (not configured)";
                ProviderCombo.Items.Add(new ComboBoxItem { Content = label, Tag = p.ProviderType });
            }
        }

        private void SelectCurrentProvider()
        {
            var current = AIManager.GetActiveProvider();
            for (var i = 0; i < ProviderCombo.Items.Count; i++)
            {
                if (ProviderCombo.Items[i] is ComboBoxItem item && item.Tag is AIProviderType t && t == current)
                {
                    ProviderCombo.SelectedIndex = i;
                    return;
                }
            }
            if (ProviderCombo.Items.Count > 0) ProviderCombo.SelectedIndex = 0;
        }

        private async Task PopulateModelsAsync()
        {
            ModelCombo.Items.Clear();
            AutoCheapModelCombo.Items.Clear();
            AutoSmartModelCombo.Items.Clear();

            if (ProviderCombo.SelectedItem is not ComboBoxItem providerItem) return;
            if (providerItem.Tag is not AIProviderType providerType) return;

            var ok = await AIManager.SetActiveProviderAsync(providerType);
            if (!ok) return;

            var provider = AIManager.GetActiveProviderInstance();
            if (provider == null) return;

            var models = await provider.GetModelsAsync();
            foreach (var m in models.Where(m => !string.IsNullOrWhiteSpace(m.Id)))
            {
                var item = new ComboBoxItem
                {
                    Content = $"{m.DisplayName} ({m.Id})",
                    Tag = m.Id
                };
                ModelCombo.Items.Add(item);
                AutoCheapModelCombo.Items.Add(new ComboBoxItem { Content = item.Content, Tag = item.Tag });
                AutoSmartModelCombo.Items.Add(new ComboBoxItem { Content = item.Content, Tag = item.Tag });
            }
        }

        private void SelectCurrentModel()
        {
            var current = AIManager.GetSelectedModel();
            if (string.IsNullOrWhiteSpace(current))
            {
                if (ModelCombo.Items.Count > 0) ModelCombo.SelectedIndex = 0;
                return;
            }

            for (var i = 0; i < ModelCombo.Items.Count; i++)
            {
                if (ModelCombo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
                {
                    ModelCombo.SelectedIndex = i;
                    return;
                }
            }

            if (ModelCombo.Items.Count > 0) ModelCombo.SelectedIndex = 0;
        }

        private void UpdateModelUiEnabled()
        {
            var auto = AutoModeCheckBox.IsChecked == true;
            ModelCombo.IsEnabled = !auto;
            AutoCheapModelCombo.IsEnabled = auto;
            AutoSmartModelCombo.IsEnabled = auto;
        }

        private void AutoModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                AIManager.SetAutoModeEnabled(AutoModeCheckBox.IsChecked == true);
                UpdateModelUiEnabled();
            }
            catch
            {
            }
        }

        private async void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                _isLoading = true;
                await PopulateModelsAsync();
                SelectCurrentModel();
                SelectCurrentAutoModels();
                UpdateModelUiEnabled();
            }
            catch
            {
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (AutoModeCheckBox.IsChecked == true) return;

            try
            {
                if (ModelCombo.SelectedItem is ComboBoxItem item)
                {
                    var id = item.Tag?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(id))
                        AIManager.SetSelectedModel(id);
                }
            }
            catch
            {
            }
        }

        private void SelectCurrentAutoModels()
        {
            var provider = AIManager.GetActiveProvider();
            var cheap = AIManager.GetAutoCheapModel(provider);
            var smart = AIManager.GetAutoSmartModel(provider);

            SelectComboById(AutoCheapModelCombo, cheap);
            SelectComboById(AutoSmartModelCombo, smart);
        }

        private static void SelectComboById(ComboBox combo, string id)
        {
            var current = (id ?? "").Trim();
            if (string.IsNullOrWhiteSpace(current))
            {
                if (combo.Items.Count > 0) combo.SelectedIndex = 0;
                return;
            }

            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void AutoCheapModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (AutoModeCheckBox.IsChecked != true) return;
            if (ProviderCombo.SelectedItem is not ComboBoxItem providerItem) return;
            if (providerItem.Tag is not AIProviderType providerType) return;

            try
            {
                if (AutoCheapModelCombo.SelectedItem is ComboBoxItem item)
                {
                    var id = item.Tag?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(id))
                        AIManager.SetAutoCheapModel(providerType, id);
                }
            }
            catch
            {
            }
        }

        private void AutoSmartModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (AutoModeCheckBox.IsChecked != true) return;
            if (ProviderCombo.SelectedItem is not ComboBoxItem providerItem) return;
            if (providerItem.Tag is not AIProviderType providerType) return;

            try
            {
                if (AutoSmartModelCombo.SelectedItem is ComboBoxItem item)
                {
                    var id = item.Tag?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(id))
                        AIManager.SetAutoSmartModel(providerType, id);
                }
            }
            catch
            {
            }
        }
    }
}
