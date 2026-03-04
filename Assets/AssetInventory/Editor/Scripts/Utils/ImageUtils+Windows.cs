#if UNITY_2021_2_OR_NEWER && UNITY_EDITOR_WIN && NET_4_6
using System;
using System.IO;
using UnityEngine;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Graphics = System.Drawing.Graphics;
using Color = UnityEngine.Color;
using Object = UnityEngine.Object;

namespace AssetInventory
{
    public static partial class ImageUtils
    {
        public static bool HasDominantColor(Bitmap bmp, Color target, float marginPercent = 0.02f, float coverageThreshold = 0.3f)
        {
            int w = bmp.Width, h = bmp.Height;
            int total = w * h;
            int matchCount = 0;
            int marginR = (int)Math.Ceiling(target.r * marginPercent);
            int marginG = (int)Math.Ceiling(target.g * marginPercent);
            int marginB = (int)Math.Ceiling(target.b * marginPercent);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    System.Drawing.Color p = bmp.GetPixel(x, y);
                    if (Math.Abs(p.R - target.r) <= marginR &&
                        Math.Abs(p.G - target.g) <= marginG &&
                        Math.Abs(p.B - target.b) <= marginB)
                    {
                        matchCount++;
                    }
                }
            }

            return matchCount > total * coverageThreshold;
        }

        public static bool IsErrorPreview(Bitmap bmp, float requiredRatio = 0.02f, byte tol = 10)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            int total = w * h;
            int magentaCount = 0;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    System.Drawing.Color c = bmp.GetPixel(x, y);
                    if (Math.Abs(c.R - 255) <= tol && c.G <= tol && Math.Abs(c.B - 255) <= tol)
                    {
                        magentaCount++;
                    }
                }
            }

            float ratio = (float)magentaCount / total;
            return ratio >= requiredRatio;
        }

        public static ulong ComputePerceptualHash(Bitmap bitmap, int hashSize = 8)
        {
            using Bitmap resized = new Bitmap(hashSize, hashSize);
            using Graphics g = Graphics.FromImage(resized);
            g.DrawImage(bitmap, 0, 0, hashSize, hashSize);

            double sum = 0.0;
            double[] pixels = new double[hashSize * hashSize];
            int idx = 0;

            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    System.Drawing.Color c = resized.GetPixel(x, y);
                    double l = (c.R * 0.299) + (c.G * 0.587) + (c.B * 0.114);
                    pixels[idx++] = l;
                    sum += l;
                }
            }

            double avg = sum / pixels.Length;
            ulong hash = 0UL;

            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] > avg)
                {
                    hash |= 1UL << i;
                }
            }

            return hash;
        }

        public static bool AreSimilar(Bitmap imgA, ulong hashB, double maxFractionDifferent = 0.15)
        {
            ulong hashA = ComputePerceptualHash(imgA);
            int dist = HammingDistance(hashA, hashB);
            double fraction = (double)dist / (8 * 8);
            return fraction <= maxFractionDifferent;
        }

        public static bool ResizeImage(string originalFile, string outputFile, int maxSize, bool scaleBeyondSize = true, ImageFormat format = null)
        {
            Image originalImage; // leave here as otherwise temp files will be created by FromFile() for yet unknown reasons 
            try
            {
                using (originalImage = Image.FromFile(IOUtils.ToLongPath(originalFile)))
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

                    // Create a new empty image with the new dimensions
                    using (Bitmap newImage = new Bitmap(newWidth, newHeight))
                    {
                        using (Graphics graphics = Graphics.FromImage(newImage))
                        {
                            graphics.CompositingQuality = CompositingQuality.HighQuality;
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = SmoothingMode.HighQuality;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            graphics.DrawImage(originalImage, 0, 0, newWidth, newHeight);
                        }

                        // Save the resized image
                        string dir = Path.GetDirectoryName(outputFile);
                        Directory.CreateDirectory(dir);
                        newImage.Save(IOUtils.ToLongPath(outputFile), format != null ? format : ImageFormat.Png); // Adjust the format based on your needs
                    }
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
        
        public static Bitmap ToImage(this Texture2D source)
        {
            int width = source.width;
            int height = source.height;
        
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            UnityEngine.Graphics.Blit(source, rt);
        
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
        
            Texture2D temp = new Texture2D(width, height, source.format, false, true);
            temp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            temp.Apply();
        
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        
            byte[] png = temp.EncodeToPNG();
            Object.DestroyImmediate(temp);
        
            using MemoryStream ms = new MemoryStream(png);
            return new Bitmap(ms);
        }        
    }
}
#endif