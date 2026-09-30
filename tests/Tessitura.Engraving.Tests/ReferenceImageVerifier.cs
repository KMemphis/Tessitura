using SkiaSharp;

namespace Tessitura.Engraving.Tests;

internal readonly record struct ReferenceImageResult(bool Matches, int DifferentPixels);

internal static class ReferenceImageVerifier
{
    public static void SavePng(SKSurface surface, string path)
    {
        using SKImage image = surface.Snapshot();
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(path);
        png.SaveTo(output);
    }

    public static ReferenceImageResult Compare(
        string approvedPath, string candidatePath, string differencePath,
        byte tolerance, int maximumDifferentPixels = 0)
    {
        using SKBitmap approved = SKBitmap.Decode(approvedPath)
            ?? throw new InvalidDataException($"Cannot decode approved image: {approvedPath}");
        using SKBitmap candidate = SKBitmap.Decode(candidatePath)
            ?? throw new InvalidDataException($"Cannot decode candidate image: {candidatePath}");
        int width = Math.Max(approved.Width, candidate.Width);
        int height = Math.Max(approved.Height, candidate.Height);
        using SKBitmap difference = new(width, height);
        int differentPixels = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool missing = x >= approved.Width || y >= approved.Height ||
                    x >= candidate.Width || y >= candidate.Height;
                if (!missing)
                {
                    SKColor expected = approved.GetPixel(x, y);
                    SKColor actual = candidate.GetPixel(x, y);
                    missing = Math.Abs(expected.Red - actual.Red) > tolerance ||
                        Math.Abs(expected.Green - actual.Green) > tolerance ||
                        Math.Abs(expected.Blue - actual.Blue) > tolerance ||
                        Math.Abs(expected.Alpha - actual.Alpha) > tolerance;
                }

                if (missing)
                {
                    differentPixels++;
                    difference.SetPixel(x, y, SKColors.Red);
                }
                else
                {
                    difference.SetPixel(x, y, SKColors.Transparent);
                }
            }
        }

        if (differentPixels > 0)
        {
            using SKImage image = SKImage.FromBitmap(difference);
            using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream output = File.Create(differencePath);
            png.SaveTo(output);
        }
        else
        {
            File.Delete(differencePath);
        }

        return new ReferenceImageResult(differentPixels <= maximumDifferentPixels, differentPixels);
    }

    public static void Approve(string candidatePath, string approvedPath) =>
        File.Copy(candidatePath, approvedPath, overwrite: true);
}
