#if UNITY_2021_2_OR_NEWER && (!UNITY_EDITOR_WIN || !NET_4_6)
using System;
using System.IO;
using UnityEngine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Color = UnityEngine.Color;

namespace AssetInventory
{
    public static partial class ImageUtils
    {
        public static bool HasDominantColor(Image<Rgba32> image, Color target, float marginPercent = 0.02f, float coverageThreshold = 0.3f)
        {
            int width = image.Width;
            int height = image.Height;
            int total = width * height;
            int matchCount = 0;
            int marginR = (int)Math.Ceiling(target.r * marginPercent);
            int marginG = (int)Math.Ceiling(target.g * marginPercent);
            int marginB = (int)Math.Ceiling(target.b * marginPercent);

            image.ProcessPixelRows(pixelAccessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = pixelAccessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        Rgba32 p = row[x];
                        if (Math.Abs(p.R - target.r) <= marginR &&
                            Math.Abs(p.G - target.g) <= marginG &&
                            Math.Abs(p.B - target.b) <= marginB)
                        {
                            matchCount++;
                        }
                    }
                }
            });

            return matchCount > total * coverageThreshold;
        }

        public static bool IsErrorPreview(Image<Rgba32> image, float requiredRatio = 0.02f, byte tolerance = 10)
        {
            int width = image.Width;
            int height = image.Height;
            int total = width * height;
            int magentaCount = 0;

            image.ProcessPixelRows(pixelAccessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    Span<Rgba32> row = pixelAccessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        Rgba32 c = row[x];
                        if (Math.Abs(c.R - 255) <= tolerance &&
                            c.G <= tolerance &&
                            Math.Abs(c.B - 255) <= tolerance)
                        {
                            magentaCount++;
                        }
                    }
                }
            });

            return ((float)magentaCount / total) >= requiredRatio;
        }

        public static ulong ComputePerceptualHash(Image<Rgba32> image, int hashSize = 8)
        {
            using Image<Rgba32> clone = image.Clone(ctx => ctx.Resize(hashSize, hashSize).Grayscale());
            ulong hash = 0UL;
            double sum = 0.0;
            double[] pixels = new double[hashSize * hashSize];
            int idx = 0;
            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    double l = clone[x, y].R;
                    pixels[idx++] = l;
                    sum += l;
                }
            }
            double avg = sum / pixels.Length;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] > avg) hash |= 1UL << i;
            }
            return hash;
        }

        public static bool AreSimilar(string fileA, string fileB, double maxFractionDifferent = 0.15)
        {
            using Image<Rgba32> imgA = Image.Load<Rgba32>(fileA);
            using Image<Rgba32> imgB = Image.Load<Rgba32>(fileB);

            return AreSimilar(imgA, imgB, maxFractionDifferent);
        }

        public static bool AreSimilar(Image<Rgba32> imgA, Image<Rgba32> imgB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(imgA);
            ulong hashB = ComputePerceptualHash(imgB);
            int dist = HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }

        public static bool AreSimilar(Image<Rgba32> imgA, ulong hashB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(imgA);
            int dist = HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }

        public static Image<Rgba32> ToImage(this Texture2D tex)
        {
            byte[] pngData = tex.EncodeToPNG();
            Image<Rgba32> img = Image.Load<Rgba32>(pngData);
            return img;
        }

        public static bool ResizeImage(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize = true)
        {
            try
            {
                using (Image originalImage = Image.Load(IOUtils.ToLongPath(originalFile)))
                {
                    int originalWidth = originalImage.Width;
                    int originalHeight = originalImage.Height;

                    // Calculate the scaling
                    double ratioX = (double)maxSize / originalWidth;
                    double ratioY = (double)maxSize / originalHeight;
                    double ratio = Math.Min(ratioX, ratioY);

                    int newWidth = Math.Max(1, (int)(originalWidth * ratio));
                    int newHeight = Math.Max(1, (int)(originalHeight * ratio));

                    if (!scaleBeyondSize && (newWidth > originalWidth || newHeight > originalHeight))
                    {
                        newWidth = originalWidth;
                        newHeight = originalHeight;
                    }

                    // Save the resized image
                    string dir = Path.GetDirectoryName(outputFile);
                    Directory.CreateDirectory(dir);

                    originalImage.Mutate(x => x.Resize(newWidth, newHeight));
                    originalImage.SaveAsPng(IOUtils.ToLongPath(outputFile));
                }
            }
            catch (Exception e)
            {
                if (AI.Config.LogImageExtraction)
                {
                    Debug.LogWarning($"Could not resize image '{originalFile}': {e.Message}");
                }
                return false;
            }
            return true;
        }
    }
}
#endif
