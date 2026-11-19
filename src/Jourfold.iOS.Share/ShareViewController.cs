using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace Jourfold.iOS;

/// <summary>
/// Jourfold in another app's share sheet. It keeps what is shared in the inbox that the app and the extension share
/// (<see cref="SharedInbox"/>): files as they are, a link or text, such as a place from a maps app, as text. Jourfold
/// takes it the next time it comes on screen and asks, as on Android, where it belongs. The extension itself only
/// says that it kept it; it does not read, send or change anything else.
/// </summary>
[Register("ShareViewController")]
public sealed class ShareViewController : UIViewController
{
    private readonly UILabel message = new() { Lines = 0, TextAlignment = UITextAlignment.Center, AdjustsFontForContentSizeCategory = true };
    private readonly UIButton done = UIButton.FromType(UIButtonType.System);
    private readonly UIActivityIndicatorView working = new(UIActivityIndicatorViewStyle.Medium);

    /// <summary>Jourfold's accent colour, the Accent token of the light and the dark theme.</summary>
    private static readonly UIColor Accent = UIColor.FromDynamicProvider(t => t.UserInterfaceStyle == UIUserInterfaceStyle.Dark
        ? UIColor.FromRGB(0x3E, 0xC6, 0xB0) : UIColor.FromRGB(0x0F, 0x7B, 0x7A));

    private static string Text(string key) => NSBundle.MainBundle.GetLocalizedString(key, null, null);

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.Black.ColorWithAlpha(0.3f);
        var card = new UIView { BackgroundColor = UIColor.SystemBackground, TranslatesAutoresizingMaskIntoConstraints = false };
        card.Layer.CornerRadius = 16;
        message.Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body)!;
        message.TextColor = UIColor.Label;
        message.Text = Text("Saving");
        done.SetTitle(Text("Done"), UIControlState.Normal);
        done.TitleLabel!.Font = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Headline)!;
        done.TitleLabel.AdjustsFontForContentSizeCategory = true;
        done.TintColor = Accent;
        done.Hidden = true;
        done.TouchUpInside += (_, _) => ExtensionContext?.CompleteRequest([], null);
        working.StartAnimating();
        var stack = new UIStackView([working, message, done]) { Axis = UILayoutConstraintAxis.Vertical, Spacing = 16, Alignment = UIStackViewAlignment.Center, TranslatesAutoresizingMaskIntoConstraints = false };
        card.AddSubview(stack); View.AddSubview(card);
        NSLayoutConstraint.ActivateConstraints([
            card.CenterYAnchor.ConstraintEqualTo(View.CenterYAnchor),
            card.LeadingAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.LeadingAnchor, 8),
            card.TrailingAnchor.ConstraintEqualTo(View.LayoutMarginsGuide.TrailingAnchor, -8),
            card.WidthAnchor.ConstraintLessThanOrEqualTo(420),
            stack.TopAnchor.ConstraintEqualTo(card.TopAnchor, 24),
            stack.BottomAnchor.ConstraintEqualTo(card.BottomAnchor, -20),
            stack.LeadingAnchor.ConstraintEqualTo(card.LeadingAnchor, 20),
            stack.TrailingAnchor.ConstraintEqualTo(card.TrailingAnchor, -20)
        ]);
        _ = KeepAsync();
    }

    /// <summary>Writes the share to the inbox and says so; with nothing Jourfold can take, says that instead.</summary>
    private async Task KeepAsync()
    {
        string key;
        try { key = await WriteAsync() ? "Kept" : "Nothing"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NSErrorException) { key = "Failed"; }
        working.StopAnimating(); working.Hidden = true;
        message.Text = Text(key);
        done.Hidden = false;
        UIAccessibility.PostNotification(UIAccessibilityPostNotification.ScreenChanged, message);
    }

    private async Task<bool> WriteAsync()
    {
        var group = NSFileManager.DefaultManager.GetContainerUrl(SharedInbox.Group(NSBundle.MainBundle.BundleIdentifier ?? ""))?.Path
            ?? throw new UnauthorizedAccessException("The app group is not available.");
        var share = Path.Combine(SharedInbox.Folder(group), DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]);
        var files = Path.Combine(share, SharedInbox.FilesFolder);
        Directory.CreateDirectory(files);
        var texts = new List<string>(); var count = 0;
        foreach (var item in ExtensionContext?.InputItems ?? [])
        {
            // Some apps give the subject or a place's name as the item's text, apart from the link.
            if (item.AttributedContentText?.Value is { Length: > 0 } content) texts.Add(content);
            foreach (var provider in item.Attachments ?? [])
            {
                if (FileType(provider) is { } type && await CopyAsync(provider, type, files, count)) { count++; continue; }
                if (await LoadTextAsync(provider) is { Length: > 0 } text) texts.Add(text);
            }
        }
        var lines = texts.SelectMany(t => t.Split('\n')).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
        if (count == 0 && lines.Count == 0) { Directory.Delete(share, true); return false; }
        if (lines.Count > 0) await File.WriteAllTextAsync(Path.Combine(share, SharedInbox.TextFile), string.Join('\n', lines));
        await File.WriteAllTextAsync(Path.Combine(share, SharedInbox.DoneFile), "");
        return true;
    }

    /// <summary>The type a file is offered as, such as a PDF or an image; null for a link or text.</summary>
    private static string? FileType(NSItemProvider provider) => provider.RegisteredTypeIdentifiers.FirstOrDefault(id =>
        UTType.CreateFromIdentifier(id) is { } type && type.ConformsTo(UTTypes.Data) && !type.ConformsTo(UTTypes.Text) && !type.ConformsTo(UTTypes.Url) && !type.ConformsTo(UTTypes.VCard));

    /// <summary>Copies an offered file into the share. The system's copy is only there while the handler runs.</summary>
    private static Task<bool> CopyAsync(NSItemProvider provider, string type, string folder, int index)
    {
        var copied = new TaskCompletionSource<bool>();
        provider.LoadFileRepresentation(type, (url, error) =>
        {
            try
            {
                if (url?.Path is not { } path) { copied.TrySetResult(false); return; }
                var name = SharedInbox.SafeName(provider.SuggestedName is { Length: > 0 } suggested && Path.GetExtension(suggested).Length == 0 ? suggested + Path.GetExtension(path) : provider.SuggestedName ?? Path.GetFileName(path), index);
                File.Copy(path, Path.Combine(folder, name), overwrite: true);
                copied.TrySetResult(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { copied.TrySetResult(false); }
        });
        return copied.Task;
    }

    /// <summary>A link or text as text: a maps app's place, a web page's address, a note.</summary>
    private static async Task<string?> LoadTextAsync(NSItemProvider provider)
    {
        foreach (var type in new[] { UTTypes.Url.Identifier, UTTypes.PlainText.Identifier, UTTypes.Text.Identifier })
        {
            if (!provider.HasItemConformingTo(type)) continue;
            try
            {
                switch (await provider.LoadItemAsync(type, null))
                {
                    case NSUrl url when !url.IsFileUrl: return url.AbsoluteString;
                    case NSString text: return text.ToString();
                    case NSAttributedString text: return text.Value;
                    case NSData data: return data.ToString(NSStringEncoding.UTF8);
                }
            }
            catch (NSErrorException) { }
        }
        return null;
    }
}
