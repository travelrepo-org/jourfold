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
    private static UIViewController? Top()
    {
        var controller = Scene()?.Windows.FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
        while (controller?.PresentedViewController is { } presented) controller = presented;
        return controller;
    }

    /// <summary>On an iPad, sheets appear as a popover; this one points at the middle of the screen.</summary>
    private static void Anchor(UIViewController sheet, UIViewController top)
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
        var share = new UIActivityViewController([new NSString(text)], null);
        Anchor(share, top);
        top.PresentViewController(share, true, null);
        return Task.CompletedTask;
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
