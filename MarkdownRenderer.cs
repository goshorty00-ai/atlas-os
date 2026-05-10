using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace AtlasAI
{
    public static class MarkdownRenderer
    {
        private static readonly Regex CodeBlockRegex = new(@"```(\w*)\n?([\s\S]*?)```", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new(@"\*(.+?)\*", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex BulletListRegex = new(@"^[\-\*]\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex NumberedListRegex = new(@"^\d+\.\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

        public static StackPanel RenderMarkdown(string text, Color textColor)
        {
            var container = new StackPanel();
            var segments = ParseSegments(text);

            foreach (var segment in segments)
            {
                if (segment.IsCodeBlock)
                {
                    container.Children.Add(CreateCodeBlock(segment.Content, segment.Language));
                }
                else
                {
                    var textBlock = CreateFormattedTextBlock(segment.Content, textColor);
                    container.Children.Add(textBlock);
                }
            }

            return container;
        }

        private static List<TextSegment> ParseSegments(string text)
        {
            var segments = new List<TextSegment>();
            var lastIndex = 0;

            foreach (Match match in CodeBlockRegex.Matches(text))
            {
                // Add text before code block
                if (match.Index > lastIndex)
                {
                    var beforeText = text.Substring(lastIndex, match.Index - lastIndex).Trim();
                    if (!string.IsNullOrEmpty(beforeText))
                        segments.Add(new TextSegment { Content = beforeText, IsCodeBlock = false });
                }

                // Add code block
                segments.Add(new TextSegment
                {
                    Content = match.Groups[2].Value.Trim(),
                    Language = match.Groups[1].Value,
                    IsCodeBlock = true
                });

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < text.Length)
            {
                var remaining = text.Substring(lastIndex).Trim();
                if (!string.IsNullOrEmpty(remaining))
                    segments.Add(new TextSegment { Content = remaining, IsCodeBlock = false });
            }

            if (segments.Count == 0)
                segments.Add(new TextSegment { Content = text, IsCodeBlock = false });

            return segments;
        }

        private static Border CreateCodeBlock(string code, string language)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 5, 0, 5)
            };

            var stack = new StackPanel();

            // Language label
            if (!string.IsNullOrEmpty(language))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = language.ToUpper(),
                    Foreground = new SolidColorBrush(Color.FromRgb(130, 130, 130)),
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 5)
                });
            }

            // Code content with copy button
            var codeGrid = new Grid();
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            codeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var codeText = new TextBlock
            {
                Text = code,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 170)),
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(codeText, 0);
            codeGrid.Children.Add(codeText);

            // Copy button
            var copyBtn = new Button
            {
                Content = "📋",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Copy code",
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(5, 0, 0, 0)
            };
            copyBtn.Click += (s, e) =>
            {
                Clipboard.SetText(code);
                copyBtn.Content = "✓";
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, _) => { copyBtn.Content = "📋"; timer.Stop(); };
                timer.Start();
            };
            Grid.SetColumn(copyBtn, 1);
            codeGrid.Children.Add(copyBtn);

            stack.Children.Add(codeGrid);
            border.Child = stack;
            return border;
        }

        private static TextBlock CreateFormattedTextBlock(string text, Color textColor)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            };

            // Process text with inline formatting
            ProcessInlineFormatting(text, textColor, textBlock);

            return textBlock;
        }

        private static string ProcessInlineFormatting(string text, Color textColor, TextBlock textBlock)
        {
            // If no inline code, just process bold/italic
            if (!text.Contains("`"))
            {
                AddFormattedText(text, textColor, textBlock);
                return text;
            }

            // Simple approach: split by inline code first
            var segments = InlineCodeRegex.Split(text);
            var codeMatches = InlineCodeRegex.Matches(text);
            var codeIndex = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (string.IsNullOrEmpty(segment)) continue;

                // Check if this segment was a code match
                if (codeIndex < codeMatches.Count && codeMatches[codeIndex].Groups[1].Value == segment)
                {
                    // This is inline code
                    var run = new Run(segment)
                    {
                        Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 180, 100)),
                        FontFamily = new FontFamily("Consolas")
                    };
                    textBlock.Inlines.Add(run);
                    codeIndex++;
                }
                else
                {
                    // Process bold and italic
                    AddFormattedText(segment, textColor, textBlock);
                }
            }

            return text;
        }

        private static void AddFormattedText(string text, Color textColor, TextBlock textBlock)
        {
            // Handle bullet points
            if (text.TrimStart().StartsWith("- ") || text.TrimStart().StartsWith("* "))
            {
                text = "• " + text.TrimStart().Substring(2);
            }

            // If no bold markers, just add the text directly with italic check
            if (!text.Contains("**"))
            {
                AddTextWithItalics(text, textColor, textBlock);
                return;
            }

            // Process bold
            var boldParts = BoldRegex.Split(text);
            var boldMatches = BoldRegex.Matches(text);
            var boldIndex = 0;

            foreach (var part in boldParts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                if (boldIndex < boldMatches.Count && boldMatches[boldIndex].Groups[1].Value == part)
                {
                    textBlock.Inlines.Add(new Run(part)
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(textColor)
                    });
                    boldIndex++;
                }
                else
                {
                    AddTextWithItalics(part, textColor, textBlock);
                }
            }
        }

        private static void AddTextWithItalics(string text, Color textColor, TextBlock textBlock)
        {
            // If no italic markers, just add plain text
            if (!text.Contains("*"))
            {
                textBlock.Inlines.Add(new Run(text)
                {
                    Foreground = new SolidColorBrush(textColor)
                });
                return;
            }

            var italicParts = ItalicRegex.Split(text);
            var italicMatches = ItalicRegex.Matches(text);
            var italicIndex = 0;

            foreach (var iPart in italicParts)
            {
                if (string.IsNullOrEmpty(iPart)) continue;

                if (italicIndex < italicMatches.Count && italicMatches[italicIndex].Groups[1].Value == iPart)
                {
                    textBlock.Inlines.Add(new Run(iPart)
                    {
                        FontStyle = FontStyles.Italic,
                        Foreground = new SolidColorBrush(textColor)
                    });
                    italicIndex++;
                }
                else
                {
                    textBlock.Inlines.Add(new Run(iPart)
                    {
                        Foreground = new SolidColorBrush(textColor)
                    });
                }
            }
        }

        private class TextSegment
        {
            public string Content { get; set; } = "";
            public string Language { get; set; } = "";
            public bool IsCodeBlock { get; set; }
        }
    }
}
