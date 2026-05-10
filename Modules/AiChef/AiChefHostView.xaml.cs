using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AtlasAI.Brain;

namespace AtlasAI.Modules.AiChef
{
    public partial class AiChefHostView : UserControl
    {
        private string _cuisine = "Custom";
        private string _mealType = "Dinner";
        private string _diet = "None";
        private string _skill = "Normal";
        private string _time = "30 minutes";

        private ChefRecipe? _currentRecipe;
        private readonly List<ChefRecipe> _savedRecipes = new();
        private readonly List<ShoppingItem> _shoppingList = new();
        private CancellationTokenSource? _cts;
        private readonly bool _chefMicWired = SectionSpeechMicStandard.IsMicWired("AiChef");
        private DispatcherTimer? _micNoteTimer;
        private TextBlock? _activeMicNoteTarget;

        private record ChefRecipe(string Name, string Description, string Cuisine,
            string MealType, int PrepMins, int CookMins, int Servings,
            string Difficulty, int Calories,
            List<string> Ingredients, List<string> Steps, List<string> Tips, string Allergens);

        private record ShoppingItem(string Name, bool Checked = false);

        private static readonly (string Icon, string Label)[] QuickActions = {
            ("🏠", "Make this with what I have"),
            ("🔄", "Replace missing ingredient"),
            ("🥗", "Make it healthier"),
            ("💪", "Make it higher protein"),
            ("💰", "Make it cheaper"),
            ("👶", "Make it kid friendly"),
            ("🌶️", "Make it spicy"),
            ("⭐", "Make it restaurant quality"),
            ("🔥", "Convert to air fryer"),
            ("🍲", "Convert to slow cooker"),
            ("🍳", "Convert to one-pan meal"),
            ("🆘", "Fix my cooking mistake"),
            ("❓", "Explain this step"),
            ("⚖️", "Scale recipe up/down"),
            ("🥙", "Pair with side dish"),
            ("🍷", "Pair with drink"),
            ("🍰", "Generate dessert after this meal"),
        };

        public AiChefHostView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildChips(CuisinePanel, new[] {
                "Italian","Chinese","Indian","Mexican","Japanese",
                "Korean","British","American","Mediterranean","Thai","Custom"
            }, ref _cuisine);
            BuildChips(MealTypePanel, new[] {
                "Breakfast","Lunch","Dinner","Snack","Dessert","Drink","Meal prep"
            }, ref _mealType);
            BuildChips(DietPanel, new[] {
                "None","Vegetarian","Vegan","High protein","Low calorie",
                "Keto","Gluten free","Dairy free","Low carb","Halal"
            }, ref _diet);
            BuildChips(SkillPanel, new[] { "Easy", "Normal", "Chef mode" }, ref _skill);
            BuildChips(TimePanel, new[] { "10 mins","20 mins","30 mins","1 hour","Slow cook" }, ref _time);

            BuildQuickActions();
            WireSuggestionCards();
            SetActiveTab("recipe");
        }

        private void BuildChips(WrapPanel panel, string[] opts, ref string selected)
        {
            var sel = opts.Contains(selected) ? selected : opts[0];
            selected = sel;
            foreach (var opt in opts)
            {
                var o = opt;
                var tb = new ToggleButton
                {
                    Content = o,
                    IsChecked = string.Equals(o, sel, StringComparison.OrdinalIgnoreCase),
                    Style = (Style)FindResource("Chip")
                };
                tb.Checked += (s, _) => OnChipChecked(panel, (ToggleButton)s);
                panel.Children.Add(tb);
            }
        }

        private void OnChipChecked(WrapPanel panel, ToggleButton active)
        {
            foreach (var tb in panel.Children.OfType<ToggleButton>())
                tb.IsChecked = tb == active;
            var val = active.Content?.ToString() ?? "";
            if      (panel == CuisinePanel)  _cuisine  = val;
            else if (panel == MealTypePanel) _mealType = val;
            else if (panel == DietPanel)     _diet     = val;
            else if (panel == SkillPanel)    _skill    = val;
            else if (panel == TimePanel)     _time     = val;
        }

        private void WireSuggestionCards()
        {
            void Wire(Border b, string text) =>
                b.MouseLeftButtonUp += (s, e) => { PromptBox.Text = text; GenerateBtn_Click(this, new RoutedEventArgs()); };
            Wire(Sugg1, "I want to make pasta for dinner tonight");
            Wire(Sugg2, "Quick healthy breakfast under 10 minutes");
            Wire(Sugg3, "High-protein meal prep for the week");
            Wire(Sugg4, "Something impressive for a date night");

            foreach (var b in new[] { Sugg1, Sugg2, Sugg3, Sugg4 })
            {
                b.MouseEnter += (s, _) => ((Border)s).BorderBrush = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
                b.MouseLeave += (s, _) => ((Border)s).BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
            }
        }

        private void BuildQuickActions()
        {
            QuickActionsPanel.Children.Clear();
            foreach (var (icon, label) in QuickActions)
            {
                var lbl = label;
                var btn = new Button
                {
                    Style = (Style)FindResource("QuickActionBtn"),
                    IsEnabled = false,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock { Text = icon, FontSize = 16, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center },
                            new TextBlock { Text = lbl, Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }
                        }
                    }
                };
                btn.Click += (s, e) => RunQuickAction(lbl);
                QuickActionsPanel.Children.Add(btn);
            }
        }

        private void SetQuickActionsEnabled(bool enabled)
        {
            foreach (var btn in QuickActionsPanel.Children.OfType<Button>())
                btn.IsEnabled = enabled;
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) SetActiveTab(btn.Tag?.ToString() ?? "recipe");
        }

        private void BackToRecipe_Click(object sender, RoutedEventArgs e) => SetActiveTab("recipe");

        private void SetActiveTab(string tab)
        {
            RecipeView.Visibility   = tab == "recipe"    ? Visibility.Visible : Visibility.Collapsed;
            CookingView.Visibility  = tab == "cooking"   ? Visibility.Visible : Visibility.Collapsed;
            MealPlanView.Visibility = tab == "meal-plan" ? Visibility.Visible : Visibility.Collapsed;
            ShoppingView.Visibility = tab == "shopping"  ? Visibility.Visible : Visibility.Collapsed;
            SavedView.Visibility    = tab == "saved"     ? Visibility.Visible : Visibility.Collapsed;

            foreach (var btn in new[] { TabRecipeBtn, TabCookingBtn, TabMealPlanBtn, TabShoppingBtn, TabSavedBtn })
            {
                bool active = string.Equals(btn.Tag?.ToString(), tab, StringComparison.OrdinalIgnoreCase);
                btn.Style = (Style)FindResource(active ? "NavActive" : "NavInactive");
                if (active && btn == TabRecipeBtn) btn.Foreground = new SolidColorBrush(Color.FromRgb(0x09, 0x09, 0x0B));
                else if (!active) btn.Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
            }
        }

        private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
        {
            var prompt = PromptBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt)) prompt = $"A delicious {_mealType.ToLower()} dish";

            var servings = (ServingsCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "2";
            var budget   = (BudgetCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()   ?? "Medium";

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            SetGeneratingState(true);

            try
            {
                var system =
                    "You are an expert chef AI. Respond ONLY with a valid JSON object — no markdown, no code fences. " +
                    "Schema: {\"name\":\"string\",\"description\":\"string\",\"cuisine\":\"string\",\"mealType\":\"string\"," +
                    "\"prepMins\":0,\"cookMins\":0,\"servings\":0,\"difficulty\":\"string\",\"calories\":0," +
                    "\"ingredients\":[\"string\"],\"steps\":[\"string\"],\"tips\":[\"string\"],\"allergens\":\"string\"}";

                var user =
                    $"Create a recipe for: {prompt}. " +
                    $"Cuisine: {_cuisine}. Meal type: {_mealType}. Diet: {_diet}. " +
                    $"Skill: {_skill}. Time: {_time}. Servings: {servings}. Budget: {budget}. Return ONLY JSON.";

                var msgs = new List<object>
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = user   }
                };

                var resp = await AI.AIManager.SendMessageAsync("AiChef", msgs, 1500, _cts.Token);
                var json = (resp?.Content ?? "").Trim();
                if (json.StartsWith("```")) json = json[(json.IndexOf('\n') + 1)..];
                if (json.EndsWith("```"))   json = json[..json.LastIndexOf("```")];
                json = json.Trim();

                _currentRecipe = ParseRecipe(json);
                ShowRecipeCard(_currentRecipe);
                TabCookingBtn.IsEnabled = true;
                AddToShoppingList(_currentRecipe);
                AskChefBtn.IsEnabled = true;
                SetQuickActionsEnabled(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError($"Could not generate recipe: {ex.Message}"); }
            finally { SetGeneratingState(false); }
        }

        private static ChefRecipe ParseRecipe(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string Str(string k, string d = "") => r.TryGetProperty(k, out var v) ? v.GetString() ?? d : d;
            int    Int(string k, int d = 0) => r.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : d;
            List<string> Arr(string k) =>
                r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
                    ? v.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
                    : new();
            return new ChefRecipe(Str("name","Recipe"), Str("description"), Str("cuisine"), Str("mealType"),
                Int("prepMins"), Int("cookMins"), Int("servings",2), Str("difficulty"), Int("calories"),
                Arr("ingredients"), Arr("steps"), Arr("tips"), Str("allergens"));
        }

        private void SetGeneratingState(bool on)
        {
            GenerateBtn.IsEnabled   = !on;
            EmptyState.Visibility   = on || _currentRecipe != null ? Visibility.Collapsed : Visibility.Visible;
            LoadingState.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) RecipeCardScroll.Visibility = Visibility.Collapsed;
        }

        private void ShowRecipeCard(ChefRecipe r)
        {
            RecipeCardPanel.Children.Clear();
            RecipeCardScroll.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;

            var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleTb = new TextBlock { Text = r.Name, Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), FontSize = 24, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
            var saveBtn = new Button { Content = "★ Save", Style = (Style)FindResource("NavInactive"), Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            saveBtn.Click += (s, e) => SaveRecipe(r);
            Grid.SetColumn(saveBtn, 1);
            titleRow.Children.Add(titleTb);
            titleRow.Children.Add(saveBtn);
            RecipeCardPanel.Children.Add(titleRow);

            RecipeCardPanel.Children.Add(new TextBlock { Text = r.Description, Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 16) });

            var stats = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            foreach (var (lbl, val) in new[] {
                ("🕐 Prep", $"{r.PrepMins} min"), ("🔥 Cook", $"{r.CookMins} min"),
                ("⏱ Total", $"{r.PrepMins+r.CookMins} min"), ("👤 Serves", r.Servings.ToString()),
                ("⚡ Level", r.Difficulty), ("🌍", r.Cuisine) })
            {
                if (string.IsNullOrWhiteSpace(val) || val == "0 min") continue;
                stats.Children.Add(StatBadge(lbl, val));
            }
            if (r.Calories > 0) stats.Children.Add(StatBadge("🔥 kcal", $"~{r.Calories}"));
            RecipeCardPanel.Children.Add(stats);

            if (!string.IsNullOrWhiteSpace(r.Allergens))
                RecipeCardPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x0A, 0x0A)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 0, 16),
                    Child = new TextBlock { Text = "⚠ Allergens: " + r.Allergens, Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)), FontSize = 12, TextWrapping = TextWrapping.Wrap }
                });

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });

            var ingSp = new StackPanel();
            ingSp.Children.Add(SectionHead("🥩 Ingredients"));
            foreach (var i in r.Ingredients) ingSp.Children.Add(Line("• " + i));
            Grid.SetColumn(ingSp, 0); grid.Children.Add(ingSp);

            var stepSp = new StackPanel();
            stepSp.Children.Add(SectionHead("👨‍🍳 Method"));
            for (int n = 0; n < r.Steps.Count; n++) stepSp.Children.Add(NumStep(n + 1, r.Steps.Count, r.Steps[n]));
            Grid.SetColumn(stepSp, 2); grid.Children.Add(stepSp);

            RecipeCardPanel.Children.Add(grid);

            if (r.Tips.Count > 0)
            {
                var tipSp = new StackPanel();
                tipSp.Children.Add(new TextBlock { Text = "✦ Chef Tips", Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
                foreach (var t in r.Tips) tipSp.Children.Add(new TextBlock { Text = "• " + t, Foreground = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
                RecipeCardPanel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x00)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), BorderThickness = new Thickness(2, 0, 0, 0), CornerRadius = new CornerRadius(0, 8, 8, 0), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 16), Child = tipSp });
            }

            var cookBtn = new Button { Content = "▶  Start Cooking Mode", Style = (Style)FindResource("AmberBtn"), Padding = new Thickness(0, 12, 0, 12) };
            cookBtn.Click += (s, e) => { BuildCookingMode(_currentRecipe!); SetActiveTab("cooking"); };
            RecipeCardPanel.Children.Add(cookBtn);
        }

        private static Border StatBadge(string label, string val) =>
            new()
            {
                Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label + "  ", Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)), FontSize = 11 }, new TextBlock { Text = val, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)), FontSize = 11, FontWeight = FontWeights.SemiBold } } }
            };

        private static TextBlock SectionHead(string t) => new() { Text = t, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)), FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
        private static TextBlock Line(string t) => new() { Text = t, Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) };

        private static UIElement NumStep(int n, int total, string text)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromRgb(0x45, 0x1A, 0x03)), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = n.ToString(), Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var tb = new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD8)), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(badge, 0); Grid.SetColumn(tb, 1);
            g.Children.Add(badge); g.Children.Add(tb);
            return g;
        }

        private void BuildCookingMode(ChefRecipe r)
        {
            CookingPanel.Children.Clear();
            CookingPanel.Children.Add(new TextBlock { Text = r.Name + " — Cooking Mode", Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 24) });
            for (int i = 0; i < r.Steps.Count; i++)
            {
                var n = i + 1;
                CookingPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 12),
                    Child = new StackPanel { Children = { new TextBlock { Text = $"Step {n} of {r.Steps.Count}", Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)), FontSize = 11, Margin = new Thickness(0, 0, 0, 6) }, new TextBlock { Text = r.Steps[i], Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)), FontSize = 15, TextWrapping = TextWrapping.Wrap } } }
                });
            }
        }

        private async void AskChef_Click(object sender, RoutedEventArgs e)
        {
            await RunAiChefQuery(AiPromptBox.Text.Trim());
        }

        private void PromptMicBtn_Click(object sender, RoutedEventArgs e)
        {
            HandleMicClick(PromptMicNote);
        }

        private void AiPromptMicBtn_Click(object sender, RoutedEventArgs e)
        {
            HandleMicClick(AiPromptMicNote);
        }

        private void HandleMicClick(TextBlock targetNote)
        {
            if (!_chefMicWired)
            {
                ShowMicInlineNote(targetNote, "Mic not wired");
                return;
            }

            ShowMicInlineNote(targetNote, "Mic ready");
        }

        // Transcript fill never triggers Generate/Ask automatically; user keeps manual control.
        private void ApplyMicTranscript(string transcript, bool recipePrompt)
        {
            var value = (transcript ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return;
            }

            if (recipePrompt)
            {
                PromptBox.Text = value;
                ShowMicInlineNote(PromptMicNote, "Voice captured. Press Generate Recipe.");
            }
            else
            {
                AiPromptBox.Text = value;
                ShowMicInlineNote(AiPromptMicNote, "Voice captured. Press Ask Chef.");
            }
        }

        private void ShowMicInlineNote(TextBlock target, string text)
        {
            if (_activeMicNoteTarget != null && _activeMicNoteTarget != target)
            {
                _activeMicNoteTarget.Visibility = Visibility.Collapsed;
            }

            target.Text = text;
            target.Visibility = Visibility.Visible;
            _activeMicNoteTarget = target;

            _micNoteTimer?.Stop();
            _micNoteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
            _micNoteTimer.Tick += (_, _) =>
            {
                _micNoteTimer?.Stop();
                if (_activeMicNoteTarget != null)
                {
                    _activeMicNoteTarget.Visibility = Visibility.Collapsed;
                }
            };
            _micNoteTimer.Start();
        }

        private async void RunQuickAction(string label)
        {
            AiPromptBox.Text = label;
            await RunAiChefQuery(label);
        }

        private async Task RunAiChefQuery(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt) || _currentRecipe == null) return;
            AskChefBtn.IsEnabled = false;
            AiResponseBorder.Visibility = Visibility.Visible;
            AiResponseText.Text = "Thinking...";

            try
            {
                var recipeJson = JsonSerializer.Serialize(new {
                    _currentRecipe.Name, _currentRecipe.Ingredients, _currentRecipe.Steps, _currentRecipe.Description
                });

                var msgs = new List<object>
                {
                    new { role = "system", content = "You are an expert chef AI assistant. Answer questions about or suggest modifications to recipes. Be concise and practical." },
                    new { role = "user", content = $"Recipe: {recipeJson}\n\nRequest: {prompt}" }
                };

                var resp = await AI.AIManager.SendMessageAsync("AiChef", msgs, 600, CancellationToken.None);
                AiResponseText.Text = resp?.Content?.Trim() ?? "No response.";
            }
            catch (Exception ex)
            {
                AiResponseText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                AskChefBtn.IsEnabled = true;
            }
        }

        private void AddToShoppingList(ChefRecipe r)
        {
            foreach (var ing in r.Ingredients)
                if (!_shoppingList.Any(x => x.Name.Equals(ing, StringComparison.OrdinalIgnoreCase)))
                    _shoppingList.Add(new ShoppingItem(ing));
            RefreshShoppingUI();
        }

        private void RefreshShoppingUI()
        {
            ShoppingListPanel.Children.Clear();
            if (_shoppingList.Count == 0) { ShoppingListPanel.Children.Add(new TextBlock { Text = "No items yet.", Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)), FontSize = 13, Margin = new Thickness(0, 20, 0, 0) }); return; }
            foreach (var item in _shoppingList.ToList())
            {
                var it = item;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var cb = new CheckBox { IsChecked = it.Checked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var tb = new TextBlock { Text = it.Name, Foreground = new SolidColorBrush(it.Checked ? Color.FromRgb(0x52, 0x52, 0x5B) : Color.FromRgb(0xD4, 0xD4, 0xD8)), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextDecorations = it.Checked ? TextDecorations.Strikethrough : null };
                var del = new Button { Content = "✕", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)), Padding = new Thickness(6, 2, 6, 2), Cursor = Cursors.Hand, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                del.Click += (s, e) => { _shoppingList.Remove(it); RefreshShoppingUI(); };
                Grid.SetColumn(cb, 0); Grid.SetColumn(tb, 1); Grid.SetColumn(del, 2);
                row.Children.Add(cb); row.Children.Add(tb); row.Children.Add(del);
                ShoppingListPanel.Children.Add(row);
            }
        }

        private void ClearShopping_Click(object sender, RoutedEventArgs e) { _shoppingList.Clear(); RefreshShoppingUI(); }

        private void SaveRecipe(ChefRecipe r)
        {
            if (_savedRecipes.Any(x => x.Name == r.Name)) return;
            _savedRecipes.Add(r);
            RefreshSavedRecipes();
            SetActiveTab("saved");
        }

        private void RefreshSavedRecipes()
        {
            SavedRecipesPanel.Children.Clear();
            foreach (var r in _savedRecipes)
            {
                var rec = r;
                var card = new Border { Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 14, 16, 14), Width = 220, Margin = new Thickness(0, 0, 14, 14), Cursor = Cursors.Hand };
                card.Child = new StackPanel { Children = { new TextBlock { Text = rec.Name, Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = $"{rec.MealType} · {rec.Cuisine}", Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)), FontSize = 11, Margin = new Thickness(0, 4, 0, 6) }, new TextBlock { Text = $"⏱ {rec.PrepMins + rec.CookMins} min  •  👤 {rec.Servings}", Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)), FontSize = 11 } } };
                card.MouseLeftButtonUp += (s, e) => { _currentRecipe = rec; ShowRecipeCard(rec); SetActiveTab("recipe"); };
                SavedRecipesPanel.Children.Add(card);
            }
        }

        private void ShowError(string msg)
        {
            RecipeCardPanel.Children.Clear();
            RecipeCardPanel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x0A, 0x0A)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 40, 0, 0), Child = new TextBlock { Text = "⚠ " + msg, Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)), FontSize = 13, TextWrapping = TextWrapping.Wrap } });
            RecipeCardScroll.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }
}
