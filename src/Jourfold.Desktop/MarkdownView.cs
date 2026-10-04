using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace Jourfold.Desktop;
/// <summary>Local CommonMark rendering. Embedded HTML is displayed as text; it cannot execute.</summary>
public sealed class MarkdownView : StackPanel
{
    public MarkdownView(string markdown) { Spacing = 8; RenderBlocks(Markdown.Parse(markdown), this); }
    private static void RenderBlocks(ContainerBlock blocks, StackPanel panel)
    {
        foreach (var block in blocks)
        {
            if (block is LeafBlock leaf)
            {
                var text = new SelectableTextBlock { Text = leaf.Inline is null ? leaf.Lines.ToString() : InlineText(leaf.Inline), TextWrapping = TextWrapping.Wrap };
                if (block is HeadingBlock heading) { text.FontSize = 24 - Math.Min(heading.Level, 5) * 2; text.FontWeight = FontWeight.SemiBold; }
                if (block is CodeBlock) text.FontFamily = FontFamily.Parse("monospace");
                panel.Children.Add(text);
            }
            else if (block is ListBlock list)
            {
                // Each item is a row: the bullet or number beside the item's own blocks.
                var items = new StackPanel { Spacing = 4, Margin = new Thickness(4, 0, 0, 0) };
                var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var content = new StackPanel { Spacing = 4 }; RenderBlocks(item, content);
                    var marker = new TextBlock { Text = list.IsOrdered ? number++ + "." : "•", MinWidth = 18, Margin = new Thickness(0, 0, 4, 0) };
                    var row = new DockPanel(); DockPanel.SetDock(marker, Dock.Left); row.Children.Add(marker); row.Children.Add(content);
                    items.Children.Add(row);
                }
                panel.Children.Add(items);
            }
            else if (block is ContainerBlock container)
            {
                var child = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 0, 0) };
                RenderBlocks(container, child); panel.Children.Add(child);
            }
        }
    }
    private static string InlineText(ContainerInline container)
    {
        var text = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            if (inline is LiteralInline literal) text.Append(literal.Content);
            else if (inline is CodeInline code) text.Append(code.Content);
            else if (inline is LineBreakInline) text.Append('\n');
            else if (inline is ContainerInline nested) { text.Append(InlineText(nested)); if (inline is LinkInline link && !link.IsImage) text.Append(" (" + link.Url + ")"); }
        }
        return text.ToString();
    }
}
