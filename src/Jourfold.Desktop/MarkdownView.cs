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
            else if (block is ContainerBlock container)
            {
                var child = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 0, 0) };
                if (block is ListItemBlock) child.Children.Add(new TextBlock { Text = "•" });
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
