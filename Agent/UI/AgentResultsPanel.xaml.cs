using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AtlasAI.Agent.UI
{
    public partial class AgentResultsPanel : UserControl
    {
        private readonly ObservableCollection<ActivityItem> _activities = new();

        public AgentResultsPanel()
        {
            InitializeComponent();
            ActivityList.ItemsSource = _activities;

            // Subscribe to macro engine events
            AgentMacroEngine.Instance.MacroExecuted += OnMacroExecuted;
            AgentMacroEngine.Instance.ActivityLogged += OnActivityLogged;
            
            // Subscribe to action engine events
            AgentActionEngine.Instance.ActionExecuted += OnActionExecuted;
            AgentActionEngine.Instance.ActivityLogged += OnActionActivityLogged;
        }

        private void OnActionExecuted(object? sender, ActionResult result)
        {
            Dispatcher.Invoke(() => DisplayActionResult(result));
        }

        private void OnActionActivityLogged(object? sender, ActionActivityEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                _activities.Insert(0, new ActivityItem
                {
                    TimeStr = entry.Timestamp.ToString("HH:mm:ss"),
                    Title = entry.ActionTitle,
                    DurationStr = $"{entry.Duration.TotalMilliseconds:F0}ms",
                    Success = entry.Success
                });

                while (_activities.Count > 20)
                    _activities.RemoveAt(_activities.Count - 1);
            });
        }

        public void DisplayActionResult(ActionResult result)
        {
            ResultsContainer.Children.Clear();

            if (!result.Success)
            {
                AddErrorCard(result.ErrorMessage ?? "Action failed");
                return;
            }

            // Success message
            var successBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x15, 0x22, 0xc5, 0x5e)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xc5, 0x5e))
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = "✓",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            });
            stack.Children.Add(new TextBlock
            {
                Text = result.Message ?? "Action completed",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf9)),
                VerticalAlignment = VerticalAlignment.Center
            });

            successBorder.Child = stack;
            ResultsContainer.Children.Add(successBorder);

            // Show output if present
            if (!string.IsNullOrEmpty(result.Output))
            {
                var outputBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var outputText = new TextBlock
                {
                    Text = result.Output,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    TextWrapping = TextWrapping.Wrap
                };

                outputBorder.Child = outputText;
                ResultsContainer.Children.Add(outputBorder);
            }

            // Open file button if path provided
            if (!string.IsNullOrEmpty(result.OpenFilePath))
            {
                var openBtn = CreateActionButton("📂", "Open Location", result.OpenFilePath);
                openBtn.MouseLeftButtonUp += (s, e) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{result.OpenFilePath}\"",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                };
                ResultsContainer.Children.Add(openBtn);
            }
        }

        private void OnMacroExecuted(object? sender, MacroResult result)
        {
            Dispatcher.Invoke(() => DisplayResult(result));
        }

        private void OnActivityLogged(object? sender, MacroActivityEntry entry)
        {
            Dispatcher.Invoke(() =>
            {
                _activities.Insert(0, new ActivityItem
                {
                    TimeStr = entry.Timestamp.ToString("HH:mm:ss"),
                    Title = entry.MacroTitle,
                    DurationStr = $"{entry.Duration.TotalMilliseconds:F0}ms",
                    Success = entry.Success
                });

                // Keep only last 20 activities
                while (_activities.Count > 20)
                    _activities.RemoveAt(_activities.Count - 1);
            });
        }

        public void DisplayResult(MacroResult result)
        {
            ResultsContainer.Children.Clear();

            if (!result.Success)
            {
                AddErrorCard(result.ErrorMessage ?? "Unknown error");
                return;
            }

            // Add confidence statement at the top
            if (!string.IsNullOrEmpty(result.ConfidenceStatement))
            {
                AddConfidenceStatement(result.ConfidenceStatement);
            }

            foreach (var card in result.Cards)
            {
                AddCard(card);
            }

            // Add summary if present
            if (!string.IsNullOrEmpty(result.Summary))
            {
                var summaryBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1a)),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 8, 0, 0)
                };

                var summaryText = new TextBlock
                {
                    Text = result.Summary,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };

                summaryBorder.Child = summaryText;
                ResultsContainer.Children.Add(summaryBorder);
            }

            // Add Action Cards (quick actions based on macro)
            var suggestedActionIds = AgentActionEngine.Instance.GetSuggestedActionsForMacro(result.MacroId);
            if (suggestedActionIds.Count > 0)
            {
                AddActionCardsSection(suggestedActionIds);
            }

            // Add suggestions at the bottom (macro suggestions)
            if (result.Suggestions != null && result.Suggestions.Count > 0)
            {
                AddSuggestionsSection(result.Suggestions);
            }
        }

        private void AddActionCardsSection(List<string> actionIds)
        {
            var sectionBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0c)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0x22, 0xc5, 0x5e))
            };

            var stack = new StackPanel();

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            headerStack.Children.Add(new TextBlock
            {
                Text = "⚡",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "QUICK ACTIONS",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(headerStack);

            // Action buttons in a wrap panel
            var wrapPanel = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (var actionId in actionIds)
            {
                var action = AgentActionEngine.Instance.Actions
                    .FirstOrDefault(a => a.Id == actionId);
                if (action == null) continue;

                var btn = CreateActionCardButton(action);
                wrapPanel.Children.Add(btn);
            }

            stack.Children.Add(wrapPanel);
            sectionBorder.Child = stack;
            ResultsContainer.Children.Add(sectionBorder);
        }

        private Border CreateActionCardButton(AgentActionDefinition action)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 8, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = action.Icon,
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = action.Title,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf9)),
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = stack;
            border.Tag = action.Id;

            // Hover effect
            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1f));
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12));
            };

            // Click to execute
            border.MouseLeftButtonUp += async (s, e) =>
            {
                var actionId = border.Tag as string;
                if (!string.IsNullOrEmpty(actionId))
                {
                    var result = await AgentActionEngine.Instance.ExecuteByIdAsync(actionId);
                    if (result != null)
                    {
                        DisplayActionResult(result);
                    }
                }
            };

            return border;
        }

        private Border CreateActionButton(string icon, string title, string? tooltip = null)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 4),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            if (!string.IsNullOrEmpty(tooltip))
                border.ToolTip = tooltip;

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 12,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf9)),
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = stack;

            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1f));
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12));
            };

            return border;
        }

        private void AddConfidenceStatement(string statement)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x15, 0x22, 0xd3, 0xee)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 12),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xd3, 0xee))
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            
            stack.Children.Add(new TextBlock
            {
                Text = "✓",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0xd3, 0xee)),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            });

            stack.Children.Add(new TextBlock
            {
                Text = statement,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf9)),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            border.Child = stack;
            ResultsContainer.Children.Add(border);
        }

        private void AddSuggestionsSection(List<MacroSuggestion> suggestions)
        {
            var sectionBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0c)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0x8b, 0x5c, 0xf6))
            };

            var stack = new StackPanel();

            // Header
            var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            headerStack.Children.Add(new TextBlock
            {
                Text = "→",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x5c, 0xf6)),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = "SUGGESTED NEXT",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8b, 0x5c, 0xf6)),
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(headerStack);

            // Suggestion buttons
            foreach (var suggestion in suggestions)
            {
                var btn = CreateSuggestionButton(suggestion);
                stack.Children.Add(btn);
            }

            sectionBorder.Child = stack;
            ResultsContainer.Children.Add(sectionBorder);
        }

        private Border CreateSuggestionButton(MacroSuggestion suggestion)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconText = new TextBlock
            {
                Text = suggestion.Icon,
                FontSize = 14,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(iconText, 0);
            grid.Children.Add(iconText);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock
            {
                Text = suggestion.Title,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf9)),
                FontWeight = FontWeights.Medium
            });
            textStack.Children.Add(new TextBlock
            {
                Text = suggestion.Reason,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8b)),
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            border.Child = grid;

            // Store macro ID for click handler
            border.Tag = suggestion.MacroId;

            // Hover effect
            border.MouseEnter += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1f));
            };
            border.MouseLeave += (s, e) =>
            {
                border.Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12));
            };

            // Click to execute
            border.MouseLeftButtonUp += async (s, e) =>
            {
                var macroId = border.Tag as string;
                if (!string.IsNullOrEmpty(macroId))
                {
                    var result = await AgentMacroEngine.Instance.ExecuteByIdAsync(macroId);
                    if (result != null)
                    {
                        DisplayResult(result);
                    }
                }
            };

            return border;
        }

        private void AddCard(MacroResultCard card)
        {
            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0c, 0x0c, 0x12)),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                BorderBrush = GetStatusBrush(card.StatusColor, 0.3)
            };

            var cardStack = new StackPanel();

            // Card header
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0c)),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var headerStack = new StackPanel { Orientation = Orientation.Horizontal };
            headerStack.Children.Add(new TextBlock
            {
                Text = card.Icon,
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(new TextBlock
            {
                Text = card.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = GetStatusBrush(card.StatusColor, 1.0),
                VerticalAlignment = VerticalAlignment.Center
            });

            headerBorder.Child = headerStack;
            cardStack.Children.Add(headerBorder);

            // Card rows
            var rowsStack = new StackPanel { Margin = new Thickness(12, 8, 12, 8) };

            foreach (var row in card.Rows)
            {
                var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

                var iconText = new TextBlock
                {
                    Text = row.Icon ?? "•",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8b)),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconText, 0);
                rowGrid.Children.Add(iconText);

                var labelText = new TextBlock
                {
                    Text = row.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xa3, 0xb8)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(labelText, 1);
                rowGrid.Children.Add(labelText);

                var valueText = new TextBlock
                {
                    Text = row.Value,
                    FontSize = 11,
                    Foreground = GetStatusBrush(row.ValueColor, 1.0) ?? new SolidColorBrush(Color.FromRgb(0xf1, 0xf5, 0xf9)),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(valueText, 2);
                rowGrid.Children.Add(valueText);

                rowsStack.Children.Add(rowGrid);
            }

            cardStack.Children.Add(rowsStack);

            // Footer if present
            if (!string.IsNullOrEmpty(card.Footer))
            {
                var footerBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x08, 0x08, 0x0c)),
                    Padding = new Thickness(12, 6, 12, 6),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x22, 0xd3, 0xee))
                };

                footerBorder.Child = new TextBlock
                {
                    Text = card.Footer,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8b)),
                    FontFamily = new FontFamily("Cascadia Code, Consolas")
                };

                cardStack.Children.Add(footerBorder);
            }

            cardBorder.Child = cardStack;
            ResultsContainer.Children.Add(cardBorder);
        }

        private void AddErrorCard(string message)
        {
            var errorBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x20, 0xef, 0x44, 0x44)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xef, 0x44, 0x44))
            };

            var errorStack = new StackPanel { Orientation = Orientation.Horizontal };
            errorStack.Children.Add(new TextBlock
            {
                Text = "❌",
                FontSize = 14,
                Margin = new Thickness(0, 0, 8, 0)
            });
            errorStack.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            errorBorder.Child = errorStack;
            ResultsContainer.Children.Add(errorBorder);
        }

        private SolidColorBrush? GetStatusBrush(string? color, double opacity)
        {
            if (string.IsNullOrEmpty(color)) return null;

            var baseColor = color.ToLower() switch
            {
                "cyan" => Color.FromRgb(0x22, 0xd3, 0xee),
                "green" => Color.FromRgb(0x22, 0xc5, 0x5e),
                "yellow" => Color.FromRgb(0xf5, 0x9e, 0x0b),
                "red" => Color.FromRgb(0xef, 0x44, 0x44),
                "violet" => Color.FromRgb(0x8b, 0x5c, 0xf6),
                _ => Color.FromRgb(0x94, 0xa3, 0xb8)
            };

            if (opacity < 1.0)
                baseColor = Color.FromArgb((byte)(255 * opacity), baseColor.R, baseColor.G, baseColor.B);

            return new SolidColorBrush(baseColor);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ResultsContainer.Children.Clear();
        }

        public class ActivityItem
        {
            public string TimeStr { get; set; } = "";
            public string Title { get; set; } = "";
            public string DurationStr { get; set; } = "";
            public bool Success { get; set; }
        }
    }
}
