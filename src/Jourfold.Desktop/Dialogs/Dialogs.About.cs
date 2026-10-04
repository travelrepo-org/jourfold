using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Jourfold.Infrastructure;

namespace Jourfold.Desktop;

public sealed partial class Dialogs
{
    /// <summary>The classic About window: logo, versions, licenses, source links and loaded plugins.</summary>
    public async Task AboutAsync(MainViewModel model)
    {
        var s = model.Strings; var info = model.AboutInfo();

        var mark = new Image { Source = owner.FindResource("Brand.Mark") as IImage, Width = 64, Height = 64, VerticalAlignment = VerticalAlignment.Center };
        var version = string.Format(s["AboutVersion"], info.Version) + (info.Commit is null ? "" : ", " + string.Format(s["AboutBuild"], info.Commit));
        var header = Ui.Columns("Auto,*", mark, Ui.V(2, Ui.Text("Jourfold", "h1"), new SelectableTextBlock { Text = version }.Classed("muted"), Ui.Text(s["WelcomeTitle"], "caption")).Also(v => { v.Margin = new Thickness(18, 0, 0, 0); v.VerticalAlignment = VerticalAlignment.Center; }));

        Button LicenseButton(string title, Func<string> text) { var b = Ui.Button(s["ViewLicense"], "file-text", "ghost", "compact"); b.Click += async (_, _) => await ReadTextAsync(string.Format(s["LicenseTitle"], title), text()); return b; }
        Button LinkButton(string key, Uri uri) { var b = Ui.Button(s[key], "external-link", "ghost", "compact"); b.Click += (_, _) => Open(uri.AbsoluteUri); ToolTip.SetTip(b, uri.AbsoluteUri); return b; }
        Control Actions(params Control?[] buttons) => Ui.H(4, buttons).Also(h => h.Margin = new Thickness(-8, 4, 0, 0));
        Control Section(string title, string? detail, string body, string? meta, Control actions) => Ui.Card(Ui.V(4,
            Ui.H(8, Ui.Text(title, "strong"), detail is null ? null : Ui.Text(detail, "caption").Also(t => t.VerticalAlignment = VerticalAlignment.Center)),
            body.Length == 0 ? null : Ui.Text(body, "muted").Also(t => t.TextWrapping = TextWrapping.Wrap),
            meta is null ? null : Ui.Text(meta, "caption").Also(t => t.TextWrapping = TextWrapping.Wrap),
            actions), 14);

        var body = Ui.V(12, header.Margin(0, 0, 0, 6));
        body.Children.Add(Section("Jourfold", null, s["AboutJourfoldLicense"], null,
            Actions(LicenseButton("GPL-3.0-or-later", () => MainViewModel.BundledText("Jourfold.LICENSE")), info.Source is null ? null : LinkButton("SourceCode", info.Source))));
        body.Children.Add(Section("TravelRepo", string.Format(s["AboutTravelRepoVersion"], info.TravelRepoVersion, info.FormatVersion), s["AboutTravelRepo"], s["AboutTravelRepoLicense"],
            Actions(LicenseButton("Apache-2.0", () => MainViewModel.BundledText("TravelRepo.LICENSE")), info.TravelRepoSource is null ? null : LinkButton("SourceCode", info.TravelRepoSource))));

        body.Children.Add(Ui.Text(s["Plugins"], "h3").Margin(0, 8, 0, 0));
        if (info.Plugins.Count == 0) body.Children.Add(Ui.Text(s["AboutPluginsEmpty"], "muted").Also(t => t.TextWrapping = TextWrapping.Wrap));
        foreach (var plugin in info.Plugins) body.Children.Add(PluginSection(plugin));

        body.Children.Add(Ui.Text(s["OpenSourceComponents"], "h3").Margin(0, 8, 0, 0));
        var notices = Ui.Button(s["ViewNotices"], "file-text", "ghost", "compact");
        notices.Click += async (_, _) => await ReadTextAsync(s["OpenSourceComponents"], MainViewModel.BundledText("Jourfold.NOTICES"), markdown: true);
        var folder = Path.Combine(AppContext.BaseDirectory, "licenses");
        var files = Directory.Exists(folder) ? Ui.Button(s["LicenseFiles"], "folder-open", "ghost", "compact").Also(b => b.Click += (_, _) => Open(folder)) : null;
        body.Children.Add(Ui.V(4, Ui.Text(s["OpenSourceComponentsHint"], "muted").Also(t => t.TextWrapping = TextWrapping.Wrap), Actions(notices, files)));
        body.Children.Add(new SelectableTextBlock { Text = info.Platform, TextWrapping = TextWrapping.Wrap }.Classed("caption").Margin(0, 8, 0, 0));

        Control PluginSection(KnownPlugin plugin)
        {
            var manifest = plugin.Manifest;
            var meta = string.Join("  ·  ", new[] { manifest.Id, string.IsNullOrWhiteSpace(manifest.Authors) ? null : string.Format(s["PluginBy"], manifest.Authors.Trim()), string.IsNullOrWhiteSpace(manifest.License) ? s["LicenseNotStated"] : manifest.License.Trim() }.OfType<string>());
            var description = string.IsNullOrWhiteSpace(manifest.Description) ? "" : manifest.Description.Trim();
            if (description.Length > 300) description = description[..300].TrimEnd() + "…";
            var license = plugin.LicenseFile is { } path ? LicenseButton(manifest.License ?? manifest.DisplayName, () => ReadLimited(path)) : null;
            return Section(manifest.DisplayName, manifest.Version, description, meta, Actions(license, manifest.HomepageUri is { } home ? LinkButton("Website", home) : null));
        }

        var copy = Ui.Button(s["CopyDetails"], "copy", "ghost"); var close = Action("Close", true);
        var sheet = new DialogSheet(s["About"], Ui.V(12, body, Footer(copy, close)), 600);
        copy.Click += async (_, _) => { await CopyAsync(model.AboutText()); Toast(s["Copied"]); };
        close.Click += (_, _) => sheet.Close();
        await sheet.ShowDialog(owner);
    }

    /// <summary>Shows a license or notice text in a scrollable sheet above the current one.</summary>
    private async Task ReadTextAsync(string title, string text, bool markdown = false)
    {
        Control content = markdown ? new MarkdownView(text) : new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontFamily = FontFamily.Parse("monospace"), FontSize = 12 };
        var close = Action("Close", true); var copy = Ui.Button(S["Copy"], "copy", "ghost");
        var sheet = new DialogSheet(title, Ui.V(16, new ScrollViewer { MaxHeight = 520, Content = content.Margin(0, 0, 12, 0) }, Footer(copy, close)), 760);
        close.Click += (_, _) => sheet.Close(); copy.Click += async (_, _) => { await CopyAsync(text); Toast(S["Copied"]); };
        await sheet.ShowDialog(owner);
    }

    private static string ReadLimited(string path)
    {
        using var reader = new StreamReader(path); var buffer = new char[256 * 1024];
        var count = reader.ReadBlock(buffer, 0, buffer.Length); return new string(buffer, 0, count);
    }
}
