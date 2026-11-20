using System.Globalization;
using CoreGraphics;
using Foundation;
using Jourfold.Mobile;
using UIKit;

namespace Jourfold.iOS;

/// <summary>Small services of the system: the share sheet, showing a file, the camera and the screen's brightness.</summary>
internal static class IosSystem
{
    private static UIDocumentInteractionController? shown;
    private static Preview? preview;
    private static nfloat? brightness;

    private static UIWindowScene? Scene() => UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
        .OrderBy(s => s.ActivationState == UISceneActivationState.ForegroundActive ? 0 : 1).FirstOrDefault();

    /// <summary>The screen on top, from which the system's sheets are presented.</summary>
    internal static UIViewController? Top()
    {
        var controller = Scene()?.Windows.FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
        while (controller?.PresentedViewController is { } presented) controller = presented;
        return controller;
    }

    /// <summary>On an iPad, sheets appear as a popover; this one points at the middle of the screen.</summary>
    internal static void Anchor(UIViewController sheet, UIViewController top)
    {
        if (sheet.PopoverPresentationController is not { } popover || top.View is not { } view) return;
        popover.SourceView = view;
        popover.SourceRect = new CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0);
        popover.PermittedArrowDirections = 0;
    }

    /// <summary>Offers a text, such as a day of the plan, to a messenger or mail app through the share sheet.</summary>
    public static Task ShareTextAsync(string title, string text)
    {
        if (Top() is not { } top) return Task.CompletedTask;
        var share = new UIActivityViewController([new SharedText(title, text)], null);
        Anchor(share, top);
        top.PresentViewController(share, true, null);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens a problem report in the Mail app's compose sheet, with the attachments. Without a mail account there is no
    /// sheet; the report then opens as a mailto: link in whichever mail app is set, without attachments.
    /// </summary>
    public static async Task<bool> SendMailAsync(ProblemMail mail)
    {
        if (!MessageUI.MFMailComposeViewController.CanSendMail || Top() is not { } top)
        {
            var link = "mailto:" + Uri.EscapeDataString(mail.To ?? "").Replace("%40", "@") + "?subject=" + Uri.EscapeDataString(mail.Subject) + "&body=" + Uri.EscapeDataString(mail.Body);
            return NSUrl.FromString(link) is { } url && await UIApplication.SharedApplication.OpenUrlAsync(url, new UIApplicationOpenUrlOptions());
        }
        var compose = new MessageUI.MFMailComposeViewController();
        if (mail.To is { } to) compose.SetToRecipients([to]);
        compose.SetSubject(mail.Subject);
        compose.SetMessageBody(mail.Body, false);
        foreach (var path in mail.Attachments) if (NSData.FromFile(path) is { } data) compose.AddAttachmentData(data, "text/plain", Path.GetFileName(path));
        compose.Finished += (_, e) => e.Controller.DismissViewController(true, null);
        top.PresentViewController(compose, true, null);
        return true;
    }

    /// <summary>Whether an installed app takes the link; iOS answers only for the schemes in LSApplicationQueriesSchemes.</summary>
    public static bool CanOpen(Uri link) => NSUrl.FromString(link.AbsoluteUri) is { } url && UIApplication.SharedApplication.CanOpenUrl(url);

    /// <summary>
    /// What a problem report says about the device: facts any app may read, without a permission. The model is Apple's
    /// identifier, such as "iPhone17,1", since UIDevice only says "iPhone"; the simulator names the device it plays.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value)> SystemInfo()
    {
        var device = UIDevice.CurrentDevice; var screen = UIScreen.MainScreen;
        var model = Environment.GetEnvironmentVariable("SIMULATOR_MODEL_IDENTIFIER") is { Length: > 0 } simulated ? simulated + " (Simulator)" : Sysctl("hw.machine");
        return
        [
            ("ReportInfoDevice", string.IsNullOrEmpty(model) ? device.Model : device.Model + " (" + model + ")"),
            ("ReportInfoSystem", device.SystemName + " " + device.SystemVersion),
            ("ReportInfoBuild", Sysctl("kern.osversion") ?? ""),
            ("ReportInfoScreen", (int)screen.NativeBounds.Width + " × " + (int)screen.NativeBounds.Height + " px, " + screen.NativeScale.ToString("0.#", CultureInfo.InvariantCulture) + "×"),
            ("ReportInfoProcessor", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant())
        ];
    }

    /// <summary>A text the system tells any app, such as the model or the system's build; null when it does not.</summary>
    private static string? Sysctl(string name)
    {
        nuint size = 0;
        if (SysctlByName(name, IntPtr.Zero, ref size, IntPtr.Zero, 0) != 0 || size == 0) return null;
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal((nint)size);
        try { return SysctlByName(name, buffer, ref size, IntPtr.Zero, 0) == 0 ? System.Runtime.InteropServices.Marshal.PtrToStringUTF8(buffer) : null; }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib", EntryPoint = "sysctlbyname")]
    private static extern int SysctlByName([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string name, IntPtr value, ref nuint size, IntPtr newValue, nuint newSize);

    /// <summary>A shared text with its title, which mail apps take as the subject, as on Android.</summary>
    private sealed class SharedText(string title, string text) : UIActivityItemSource
    {
        public override NSObject GetPlaceholderData(UIActivityViewController activityViewController) => new NSString(text);
        public override NSObject GetItemForActivity(UIActivityViewController activityViewController, NSString? activityType) => new NSString(text);
        public override string GetSubjectForActivity(UIActivityViewController activityViewController, NSString? activityType) => title;
    }

    /// <summary>
    /// Shows a file, such as a PDF or a photo, in the system's preview, which can also print it or pass it on. Files
    /// the preview cannot show are offered to other apps.
    /// </summary>
    public static Task<bool> OpenFileAsync(string path, string mediaType)
    {
        if (Top() is not { } top || top.View is not { } view) return Task.FromResult(false);
        // Kept while showing: the controller and its delegate are only weakly held by the system.
        preview = new Preview(top);
        shown = UIDocumentInteractionController.FromUrl(NSUrl.FromFilename(path));
        shown.Delegate = preview;
        return Task.FromResult(shown.PresentPreview(true) || shown.PresentOpenInMenu(view.Bounds, view, true));
    }

    private sealed class Preview(UIViewController top) : UIDocumentInteractionControllerDelegate
    {
        public override UIViewController ViewControllerForPreview(UIDocumentInteractionController controller) => top;
    }

    /// <summary>Full brightness while a ticket is on screen, so a scanner reads it; back to what it was after.</summary>
    public static void Bright(bool on)
    {
        if (Scene()?.Screen is not { } screen) return;
        if (on) { brightness ??= screen.Brightness; screen.Brightness = 1; }
        else if (brightness is { } before) { screen.Brightness = before; brightness = null; }
    }

    public static bool HasCamera => UIImagePickerController.IsSourceTypeAvailable(UIImagePickerControllerSourceType.Camera);

    /// <summary>
    /// Takes a photo with the camera, such as of a receipt, and makes it small enough for the trip: at most 1600
    /// pixels on the longer side, upright, as JPEG. Null when cancelled.
    /// </summary>
    public static async Task<SharedFile?> TakePhotoAsync(CancellationToken ct)
    {
        if (!HasCamera || Top() is not { } top) return null;
        var answer = new TaskCompletionSource<UIImage?>();
        var picker = new UIImagePickerController { SourceType = UIImagePickerControllerSourceType.Camera };
        picker.FinishedPickingMedia += (_, e) => { answer.TrySetResult(e.OriginalImage); picker.DismissViewController(true, null); };
        picker.Canceled += (_, _) => { answer.TrySetResult(null); picker.DismissViewController(true, null); };
        top.PresentViewController(picker, true, null);
        using var cancel = ct.Register(() => { answer.TrySetResult(null); picker.DismissViewController(true, null); });
        if (await answer.Task is not { } image) return null;
        using (image) return new SharedFile(DateTime.Now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture) + ".jpg", "image/jpeg", Shrink(image));
    }

    private static byte[] Shrink(UIImage image)
    {
        var scale = Math.Min(1, 1600 / Math.Max(image.Size.Width, image.Size.Height));
        var size = new CGSize(Math.Round(image.Size.Width * scale), Math.Round(image.Size.Height * scale));
        var format = UIGraphicsImageRendererFormat.DefaultFormat; format.Scale = 1;
        using var renderer = new UIGraphicsImageRenderer(size, format);
        // Drawing the picture applies the way the phone was held, so it reads upright everywhere.
        using var upright = renderer.CreateImage(_ => image.Draw(new CGRect(CGPoint.Empty, size)));
        using var jpeg = upright.AsJPEG(0.8f) ?? throw new IOException("The photo could not be saved as JPEG.");
        return jpeg.ToArray();
    }
}
