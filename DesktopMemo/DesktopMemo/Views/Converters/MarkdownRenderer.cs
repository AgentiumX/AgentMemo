using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DesktopMemo.Views.Converters
{
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        /// <summary>
        /// Render markdown to a list of UIElements for use in a StackPanel.
        /// TextBlock naturally wraps text to container width — fixes clipping and no-wrap issues.
        /// </summary>
        public static List<UIElement> RenderToElements(string markdown, Brush foreground, double baseFontSize)
        {
            var fontFamily = new FontFamily("Segoe UI, Microsoft YaHei, SimHei");
            var elements = new List<UIElement>();

            if (string.IsNullOrWhiteSpace(markdown))
            {
                var empty = new TextBlock
                {
                    Text = "（空内容）",
                    Foreground = new SolidColorBrush(Color.FromArgb(128, 128, 128, 128)),
                    FontFamily = fontFamily,
                    FontSize = baseFontSize,
                    TextWrapping = TextWrapping.Wrap
                };
                elements.Add(empty);
                return elements;
            }

            try
            {
                var mdDoc = Markdown.Parse(markdown, Pipeline);
                foreach (var block in mdDoc)
                {
                    var el = ConvertBlock(block, foreground, baseFontSize, fontFamily);
                    if (el != null)
                        elements.Add(el);
                }
            }
            catch
            {
                var fallback = new TextBlock
                {
                    Text = markdown,
                    Foreground = foreground,
                    FontFamily = fontFamily,
                    FontSize = baseFontSize,
                    TextWrapping = TextWrapping.Wrap
                };
                elements.Add(fallback);
            }

            return elements;
        }

        private static UIElement ConvertBlock(Markdig.Syntax.Block block, Brush foreground,
            double baseFontSize, FontFamily fontFamily)
        {
            switch (block)
            {
                case HeadingBlock heading:
                {
                    var tb = MakeTextBlock(foreground, baseFontSize, fontFamily);
                    tb.Margin = new Thickness(0, 6, 0, 2);
                    switch (heading.Level)
                    {
                        case 1:
                            tb.FontSize = baseFontSize * 1.6;
                            tb.FontWeight = FontWeights.Bold;
                            break;
                        case 2:
                            tb.FontSize = baseFontSize * 1.3;
                            tb.FontWeight = FontWeights.Bold;
                            break;
                        default:
                            tb.FontSize = baseFontSize * 1.1;
                            tb.FontWeight = FontWeights.SemiBold;
                            break;
                    }
                    AddInlines(tb, heading.Inline, foreground, baseFontSize);
                    return tb;
                }

                case ParagraphBlock paragraphBlock:
                {
                    var tb = MakeTextBlock(foreground, baseFontSize, fontFamily);
                    tb.Margin = new Thickness(0, 2, 0, 2);
                    AddInlines(tb, paragraphBlock.Inline, foreground, baseFontSize);
                    return tb;
                }

                case ListBlock listBlock:
                {
                    var panel = new StackPanel { Margin = new Thickness(8, 2, 0, 2) };
                    int index = 1;
                    foreach (var item in listBlock)
                    {
                        if (item is ListItemBlock listItem)
                        {
                            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };

                            // Bullet / number prefix
                            string prefix;
                            if (listBlock.IsOrdered)
                                prefix = $"{index++}. ";
                            else
                                prefix = "• ";

                            // Check for task list
                            bool isTask = false;
                            bool isChecked = false;
                            foreach (var itemBlock in listItem)
                            {
                                if (itemBlock is ParagraphBlock pb)
                                {
                                    var firstInline = pb.Inline?.FirstChild;
                                    if (firstInline is TaskList taskInline)
                                    {
                                        isTask = true;
                                        isChecked = taskInline.Checked;
                                    }
                                }
                            }

                            if (isTask)
                            {
                                prefix = isChecked ? "☑ " : "☐ ";
                            }

                            var prefixTb = new TextBlock
                            {
                                Text = prefix,
                                Foreground = isTask && isChecked
                                    ? new SolidColorBrush(Color.FromRgb(60, 140, 60))
                                    : foreground,
                                FontFamily = fontFamily,
                                FontSize = baseFontSize,
                                MinWidth = 16
                            };
                            row.Children.Add(prefixTb);

                            // Content
                            var contentTb = MakeTextBlock(foreground, baseFontSize, fontFamily);
                            contentTb.Margin = new Thickness(0);
                            if (isTask && isChecked)
                                contentTb.Foreground = new SolidColorBrush(Color.FromArgb(160, 128, 128, 128));

                            bool skippedTaskMarker = false;
                            foreach (var itemBlock in listItem)
                            {
                                if (itemBlock is ParagraphBlock pb)
                                {
                                    foreach (var inline in pb.Inline ?? Enumerable.Empty<Markdig.Syntax.Inlines.Inline>())
                                    {
                                        if (!skippedTaskMarker && inline is TaskList)
                                        {
                                            skippedTaskMarker = true;
                                            continue;
                                        }
                                        AddInlineToTextBlock(contentTb, inline, foreground, baseFontSize);
                                    }
                                }
                            }
                            row.Children.Add(contentTb);
                            panel.Children.Add(row);
                        }
                    }
                    return panel;
                }

                case FencedCodeBlock codeBlock:
                {
                    var codeText = codeBlock.Lines.ToString();
                    var tb = new TextBlock
                    {
                        Text = codeText.TrimEnd(),
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontSize = baseFontSize * 0.9,
                        Foreground = foreground,
                        TextWrapping = TextWrapping.Wrap,
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(0),
                        Margin = new Thickness(0, 4, 0, 4),
                        Child = tb
                    };
                    return border;
                }

                case QuoteBlock quoteBlock:
                {
                    var panel = new StackPanel { Margin = new Thickness(4, 2, 0, 2) };
                    foreach (var quoteItem in quoteBlock)
                    {
                        if (quoteItem is ParagraphBlock qp)
                        {
                            var tb = MakeTextBlock(foreground, baseFontSize, fontFamily);
                            tb.FontStyle = FontStyles.Italic;
                            tb.Margin = new Thickness(0, 1, 0, 1);
                            AddInlines(tb, qp.Inline, foreground, baseFontSize);
                            panel.Children.Add(tb);
                        }
                    }
                    var quoteBorder = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromArgb(180, 100, 100, 100)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(8, 2, 0, 2),
                        Margin = new Thickness(0, 2, 0, 2),
                        Child = panel
                    };
                    return quoteBorder;
                }

                case ThematicBreakBlock:
                {
                    return new Border
                    {
                        BorderBrush = foreground,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Opacity = 0.4,
                        Margin = new Thickness(0, 8, 0, 8)
                    };
                }

                default:
                    return null;
            }
        }

        private static TextBlock MakeTextBlock(Brush foreground, double baseFontSize, FontFamily fontFamily)
        {
            return new TextBlock
            {
                Foreground = foreground,
                FontSize = baseFontSize,
                FontFamily = fontFamily,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static void AddInlines(TextBlock tb, ContainerInline inlines, Brush foreground, double baseFontSize)
        {
            if (inlines == null) return;

            foreach (var inline in inlines)
            {
                AddInlineToTextBlock(tb, inline, foreground, baseFontSize);
            }
        }

        private static void AddInlineToTextBlock(TextBlock tb, Markdig.Syntax.Inlines.Inline inline,
            Brush foreground, double baseFontSize)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    tb.Inlines.Add(new Run(literal.Content.ToString()));
                    break;

                case EmphasisInline emphasis:
                    foreach (var child in emphasis)
                    {
                        if (child is LiteralInline lit)
                        {
                            var run = new Run(lit.Content.ToString());
                            if (emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_')
                            {
                                if (emphasis.DelimiterCount >= 2)
                                    run.FontWeight = FontWeights.Bold;
                                else
                                    run.FontStyle = FontStyles.Italic;
                            }
                            else if (emphasis.DelimiterChar == '~')
                            {
                                run.TextDecorations = TextDecorations.Strikethrough;
                            }
                            tb.Inlines.Add(run);
                        }
                    }
                    break;

                case CodeInline code:
                {
                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(25, 0, 0, 0)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(3, 0, 3, 0)
                    };
                    var codeRun = new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontSize = baseFontSize * 0.9
                    };
                    // InlineUIContainer to embed Border in TextBlock
                    var container = new InlineUIContainer { Child = border };
                    border.Child = new TextBlock
                    {
                        Text = code.Content,
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontSize = baseFontSize * 0.9,
                        Foreground = foreground
                    };
                    tb.Inlines.Add(container);
                    break;
                }

                case LineBreakInline:
                    tb.Inlines.Add(new LineBreak());
                    break;

                case LinkInline link:
                {
                    var text = link.FirstChild is LiteralInline linkLit ? linkLit.Content.ToString() : link.Url;
                    var linkRun = new Run(text)
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204)),
                        TextDecorations = TextDecorations.Underline
                    };
                    tb.Inlines.Add(linkRun);
                    break;
                }

                case TaskList:
                    // Handled at list level, skip here
                    break;

                default:
                    if (inline is ContainerInline container2)
                    {
                        foreach (var child in container2)
                        {
                            if (child is LiteralInline childLit)
                                tb.Inlines.Add(new Run(childLit.Content.ToString()));
                        }
                    }
                    break;
            }
        }
    }
}
