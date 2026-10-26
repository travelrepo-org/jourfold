#!/usr/bin/env dotnet
// Generates assets/branding/jourfold.icns, the macOS application icon, from assets/branding/mark.svg.
//
//   dotnet run eng/macos-icon.cs -- assets/branding/mark.svg assets/branding/jourfold.icns
//
// The mark sits on a white rounded square with a soft shadow, sized like other macOS icons (an 824 px body on a
// 1024 px canvas). The .icns file holds PNG images at the sizes macOS asks for.
#:package SkiaSharp@3.119.4
#:package SkiaSharp.NativeAssets.Linux@3.119.4
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SkiaSharp;

var source = XDocument.Load(args[0]);
var ns = (XNamespace)"http://www.w3.org/2000/svg";
var box = float.Parse(source.Root!.Attribute("viewBox")!.Value.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture);
var paths = source.Root.Descendants(ns + "path").Select(p => (Path: SKPath.ParseSvgPathData(p.Attribute("d")!.Value), Fill: p.Attribute("fill")?.Value ?? "#000", EvenOdd: p.Attribute("fill-rule")?.Value == "evenodd")).ToArray();

byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas; canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / 1024f);
    var body = new SKRect(100, 100, 924, 924);
    using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 70), IsAntialias = true, ImageFilter = SKImageFilter.CreateBlur(14, 14) })
        canvas.DrawRoundRect(new SKRect(body.Left, body.Top + 12, body.Right, body.Bottom + 12), 185, 185, shadow);
    using (var white = new SKPaint { Color = SKColors.White, IsAntialias = true }) canvas.DrawRoundRect(body, 185, 185, white);
    // The mark fills about two thirds of the body, centred.
    var mark = 560f; canvas.Translate(512 - mark / 2, 512 - mark / 2); canvas.Scale(mark / box);
    foreach (var (path, fill, evenOdd) in paths)
    {
        path.FillType = evenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        var hex = fill.TrimStart('#'); if (hex.Length == 3) hex = Regex.Replace(hex, "(.)", "$1$1");
        using var paint = new SKPaint { Color = SKColor.Parse(hex), IsAntialias = true };
        canvas.DrawPath(path, paint);
    }
    using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}

// Icon types holding PNG data: 16, 32, 64 (32@2x), 128, 256, 512 and 1024 (512@2x) pixels.
var entries = new (string Type, int Size)[] { ("icp4", 16), ("icp5", 32), ("ic11", 32), ("icp6", 64), ("ic12", 64), ("ic07", 128), ("ic08", 256), ("ic13", 256), ("ic09", 512), ("ic14", 512), ("ic10", 1024) };
var chunks = entries.Select(e => (e.Type, Data: Render(e.Size))).ToArray();
using var output = new MemoryStream();
void Write(string type, int length) { output.Write(System.Text.Encoding.ASCII.GetBytes(type)); output.Write([(byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]); }
Write("icns", 8 + chunks.Sum(c => 8 + c.Data.Length));
foreach (var (type, data) in chunks) { Write(type, 8 + data.Length); output.Write(data); }
File.WriteAllBytes(args[1], output.ToArray());
Console.WriteLine(args[1]);
