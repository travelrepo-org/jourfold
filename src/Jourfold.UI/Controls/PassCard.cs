using Jourfold.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TravelRepo.Documents;

namespace Jourfold.UI.Controls;

/// <summary>
/// A Wallet pass as the gate needs it: the barcode large on white, its code in text below, then the pass's fields,
/// the most important first. It works offline and without a wallet app, on the phone and on the desktop.
/// </summary>
public static class PassCard
{
    public static Control Build(WalletPass pass)
    {
        var page = Ui.V(14);
        if (pass.Organization is { Length: > 0 } organization) page.Children.Add(Ui.Text(organization, "overline"));
        var primary = pass.Fields.Where(f => f.Group == "primary").ToArray();
        if (primary.Length > 0)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', primary.Select(_ => "*"))) };
            for (var i = 0; i < primary.Length; i++) row.Children.Add(Ui.V(0, Ui.Text(primary[i].Label ?? "", "caption"), Ui.Text(primary[i].Value, "h1")).Col(i));
            page.Children.Add(row);
        }
        if (pass.Barcode is { } code)
        {
            Control barcode;
            try { barcode = new BarcodeView(code.Message, code.Format, code.Encoding); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ZXing.WriterException) { barcode = Ui.Text(code.Message, "strong"); }
            page.Children.Add(new Border { Child = barcode, Background = Brushes.White, Padding = new Thickness(12), CornerRadius = new CornerRadius(12) });
            if (code.AltText is { Length: > 0 } alt) page.Children.Add(Ui.Text(alt, "strong").Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
        }
        var details = pass.Fields.Where(f => f.Group is "header" or "secondary" or "auxiliary").ToArray();
        if (details.Length > 0)
        {
            var flow = Ui.Flow(20);
            foreach (var field in details) flow.Children.Add(Ui.V(0, Ui.Text(field.Label ?? "", "caption"), new SelectableTextBlock { Text = field.Value, FontWeight = FontWeight.SemiBold }));
            page.Children.Add(flow);
        }
        foreach (var field in pass.Fields.Where(f => f.Group == "back"))
            page.Children.Add(Ui.V(2, Ui.Text(field.Label ?? "", "caption"), new SelectableTextBlock { Text = field.Value, TextWrapping = TextWrapping.Wrap }));
        return page;
    }
}
