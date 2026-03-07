using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace UADownloader
{
    public class ImageDownloader
    {
        private readonly string _exportPath;
        private CancellationTokenSource _cts;

        public event Action<int, string, long, long, double> OnProgress;
        public event Action<int, string> OnCompleted;
        public event Action<int, string> OnFailed;

        public ImageDownloader(string exportPath)
        {
            _exportPath = exportPath;
        }

        public void StartDownload(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public async Task<bool> DownloadImagesAsync(PackageInfo package)
        {
            if (_cts?.Token.IsCancellationRequested == true)
            {
                Debug.Log($"[ImageDownloader] 下载已取消: {package.name}");
                return false;
            }

            if (package.images == null || package.images.Length == 0)
            {
                Debug.LogWarning($"[ImageDownloader] 资源包没有图片信息: {package.name}");
                OnCompleted?.Invoke(package.id, package.name);
                return true;
            }

            Debug.Log($"[ImageDownloader] 资源包共有 {package.images.Length} 张图片: {package.name}");
            Debug.Log($"[ImageDownloader] 导出路径: {_exportPath}");

            string imageFolder = Path.Combine(_exportPath, "images");
            Debug.Log($"[ImageDownloader] 目标图片文件夹: {imageFolder}");
            
            if (!Directory.Exists(imageFolder))
            {
                try
                {
                    Directory.CreateDirectory(imageFolder);
                    Debug.Log($"[ImageDownloader] 成功创建图片目录: {imageFolder}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ImageDownloader] 创建图片目录失败: {imageFolder} - {e.Message}");
                    OnFailed?.Invoke(package.id, $"创建目录失败: {e.Message}");
                    return false;
                }
            }
            else
            {
                Debug.Log($"[ImageDownloader] 图片目录已存在: {imageFolder}");
            }

            int successCount = 0;
            int failCount = 0;
            long totalDownloaded = 0;
            int downloadedCount = 0;
            int screenshotCount = 0;
            const int MAX_IMAGES = 5;

            for (int i = 0; i < package.images.Length; i++)
            {
                if (_cts?.Token.IsCancellationRequested == true)
                {
                    Debug.Log($"[ImageDownloader] 图片下载被取消: {package.name}");
                    return false;
                }

                if (downloadedCount >= MAX_IMAGES)
                {
                    Debug.Log($"[ImageDownloader] 已达到最大下载数量({MAX_IMAGES})，停止下载: {package.name}");
                    break;
                }

                var imageInfo = package.images[i];
                
                if (imageInfo.type != "screenshot")
                {
                    Debug.Log($"[ImageDownloader] 跳过非截图类型: type={imageInfo.type}, index={i}");
                    continue;
                }

                screenshotCount++;

                string imageUrl = string.IsNullOrEmpty(imageInfo.webpUrl) ? imageInfo.imageUrl : imageInfo.webpUrl;

                if (string.IsNullOrEmpty(imageUrl))
                {
                    Debug.LogWarning($"[ImageDownloader] 截图 {screenshotCount} 的URL为空: {package.name}");
                    continue;
                }

                string extension = GetImageExtension(imageUrl);
                string sanitizedName = SanitizeFileName(package.name);
                string fileName = $"{sanitizedName}_{downloadedCount}{extension}";
                string filePath = Path.Combine(imageFolder, fileName);
                
                Debug.Log($"[ImageDownloader] 检查图片是否存在: {fileName}");

                if (File.Exists(filePath))
                {
                    Debug.Log($"[ImageDownloader] 图片已存在，跳过下载: {fileName}");
                    successCount++;
                    downloadedCount++;
                    continue;
                }
                
                Debug.Log($"[ImageDownloader] 准备下载: URL={imageUrl}");
                Debug.Log($"[ImageDownloader] 保存路径: {filePath}");

                Debug.Log($"[ImageDownloader] 开始下载截图 [{downloadedCount + 1}/{MAX_IMAGES}]: {package.name}");
                bool success = await DownloadImageAsync(imageUrl, filePath, package.id, package.name, downloadedCount, MAX_IMAGES);
                if (success)
                {
                    successCount++;
                    downloadedCount++;
                    FileInfo fi = new FileInfo(filePath);
                    totalDownloaded += fi.Length;
                    Debug.Log($"[ImageDownloader] 截图已保存: {fileName} ({FormatBytes(fi.Length)})");
                }
                else
                {
                    failCount++;
                    Debug.LogWarning($"[ImageDownloader] 截图下载失败: {fileName}");
                }
            }

            Debug.Log($"[ImageDownloader] 图片下载完成: {package.name} - 总数:{package.images.Length}, 截图:{screenshotCount}, 成功:{successCount}, 失败:{failCount}");
            
            if (successCount > 0)
            {
                OnCompleted?.Invoke(package.id, package.name);
                return true;
            }
            else if (failCount > 0)
            {
                OnFailed?.Invoke(package.id, $"所有图片下载失败");
                return false;
            }
            else
            {
                OnCompleted?.Invoke(package.id, package.name);
                return true;
            }
        }

        private async Task<bool> DownloadImageAsync(string url, string savePath, int packageId, string packageName, int imageIndex, int totalImages)
        {
            try
            {
                using (var uwr = UnityWebRequest.Get(url))
                {
                    var op = uwr.SendWebRequest();
                    
                    long lastBytes = 0;
                    DateTime lastTime = DateTime.Now;
                    
                    while (!op.isDone)
                    {
                        if (_cts?.Token.IsCancellationRequested == true)
                        {
                            uwr.Abort();
                            return false;
                        }

                        long currentBytes = (long)uwr.downloadedBytes;
                        DateTime now = DateTime.Now;
                        double speed = 0;
                        
                        if (currentBytes != lastBytes)
                        {
                            double deltaSec = (now - lastTime).TotalSeconds;
                            if (deltaSec > 0)
                            {
                                speed = (currentBytes - lastBytes) / deltaSec;
                            }
                            lastBytes = currentBytes;
                            lastTime = now;
                        }

                        long totalBytes = 0;
                        string contentLength = uwr.GetResponseHeader("Content-Length");
                        if (!string.IsNullOrEmpty(contentLength))
                        {
                            long.TryParse(contentLength, out totalBytes);
                        }

                        OnProgress?.Invoke(packageId, $"{packageName} [{imageIndex + 1}/{totalImages}]", currentBytes, totalBytes, speed);
                        
                        await Task.Yield();
                    }

#if UNITY_2020_1_OR_NEWER
                    if (uwr.result == UnityWebRequest.Result.ConnectionError || 
                        uwr.result == UnityWebRequest.Result.ProtocolError)
#else
                    if (uwr.isNetworkError || uwr.isHttpError)
#endif
                    {
                        Debug.LogError($"[ImageDownloader] 下载失败: {url} - {uwr.error}");
                        OnFailed?.Invoke(packageId, $"图片下载失败: {uwr.error}");
                        return false;
                    }

                    byte[] data = uwr.downloadHandler.data;
                    await Task.Run(() => File.WriteAllBytes(savePath, data));
                    
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImageDownloader] 下载图片异常: {url} - {e.Message}");
                OnFailed?.Invoke(packageId, $"图片下载异常: {e.Message}");
                return false;
            }
        }

        private string GetImageExtension(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string path = uri.AbsolutePath;
                string ext = Path.GetExtension(path);
                
                if (!string.IsNullOrEmpty(ext) && ext.Length <= 5)
                {
                    return ext;
                }
            }
            catch
            {
            }
            
            return ".jpg";
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Unknown";
            
            fileName = fileName.Replace(' ', '_');
            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            
            char[] additionalChars = { '.', ',', ';', ':', '+', '-', '=', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '[', ']', '{', '}', '\'', '"', '`', '~', '|', '\\', '<', '>', '?', '/' };
            foreach (char c in additionalChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            
            while (fileName.Contains("__"))
            {
                fileName = fileName.Replace("__", "_");
            }
            
            if (fileName.Length > 200)
            {
                fileName = fileName.Substring(0, 200);
            }
            
            return fileName.Trim('_');
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F2} {sizes[order]}";
        }
    }
}
