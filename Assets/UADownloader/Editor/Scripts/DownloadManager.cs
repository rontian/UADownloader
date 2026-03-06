using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using AssetInventory;

namespace UADownloader
{
    public class DownloadManager
    {
        private readonly string _exportPath;
        private CancellationTokenSource _cts;
        
        private const int MAX_RETRY_COUNT = 3;
        private const int RETRY_DELAY_MS = 2000;

        public event Action<int, string, long, long, double> OnProgress; // id, name, downloadedBytes, totalBytes, speedBytesPerSec
        public event Action<int, string> OnCompleted;
        public event Action<int, string> OnFailed;

        public DownloadManager(string exportPath)
        {
            _exportPath = exportPath;
        }

        public void StartDownload(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public async Task<bool> DownloadPackageAsync(PackageInfo package, string unityCachePath)
        {
            if (_cts?.Token.IsCancellationRequested == true)
            {
                Debug.Log($"[DownloadManager] 下载已取消: {package.name}");
                return false;
            }

            // 实现重试逻辑
            for (int attempt = 1; attempt <= MAX_RETRY_COUNT; attempt++)
            {
                try
                {
                    if (attempt > 1)
                    {
                        Debug.Log($"[DownloadManager] 第 {attempt}/{MAX_RETRY_COUNT} 次尝试下载: {package.name}");
                        await Task.Delay(RETRY_DELAY_MS * (attempt - 1)); // 递增延迟
                    }
                    
                    bool result = await DownloadPackageInternalAsync(package, unityCachePath);
                    
                    if (result)
                    {
                        return true;
                    }
                    
                    // 如果是最后一次尝试，直接失败
                    if (attempt == MAX_RETRY_COUNT)
                    {
                        Debug.LogError($"[DownloadManager] 下载失败，已重试 {MAX_RETRY_COUNT} 次: {package.name}");
                        OnFailed?.Invoke(package.id, $"下载失败（已重试{MAX_RETRY_COUNT}次）");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DownloadManager] 第 {attempt} 次尝试失败: {e.Message}");
                    
                    if (e.Message.Contains("is not a valid directory name") || 
                        e.Message.Contains("Illegal characters in path"))
                    {
                        Debug.LogError($"[DownloadManager] 发布商/分类名称包含非法字符，无法下载: {package.name} (Publisher: {package.publisher})");
                        OnFailed?.Invoke(package.id, "发布商/分类名称包含非法字符");
                        return false;
                    }
                    
                    if (attempt == MAX_RETRY_COUNT)
                    {
                        Debug.LogError($"[DownloadManager] 下载异常，已重试 {MAX_RETRY_COUNT} 次: {package.name} - {e.Message}");
                        OnFailed?.Invoke(package.id, $"下载异常: {e.Message}");
                        return false;
                    }
                }
            }
            
            return false;
        }
        
        private async Task<bool> DownloadPackageInternalAsync(PackageInfo package, string unityCachePath)
        {
            try
            {
                var downloadInfo = await AssetStoreAPI.FetchDownloadInfoAsync(package.id);
                if (downloadInfo == null)
                {
                    Debug.LogWarning($"[DownloadManager] 无法获取下载信息: {package.name}");
                    OnFailed?.Invoke(package.id, "无法获取下载信息");
                    return false;
                }

                // 优先使用API返回的safe字段（已经过服务器安全处理），仅在缺失时才用SanitizeFileName()
                string safePublisher = !string.IsNullOrEmpty(downloadInfo.filename_safe_publisher_name)
                    ? downloadInfo.filename_safe_publisher_name
                    : SanitizeFileName(package.publisher);
                    
                string safeCategory = !string.IsNullOrEmpty(downloadInfo.filename_safe_category_name)
                    ? downloadInfo.filename_safe_category_name
                    : SanitizeFileName(package.category);
                    
                string safeName = !string.IsNullOrEmpty(downloadInfo.filename_safe_package_name)
                    ? downloadInfo.filename_safe_package_name
                    : SanitizeFileName(package.name);

                string expectedPath = Path.Combine(unityCachePath, safePublisher, safeCategory, $"{safeName}.unitypackage");

                if (File.Exists(expectedPath))
                {
                    await CopyToExportAsync(expectedPath, safeName, package.version);
                    OnCompleted?.Invoke(package.id, package.name);
                    return true;
                }

                try
                {
                    string categoryDir = Path.Combine(unityCachePath, safePublisher, safeCategory);
                    if (!Directory.Exists(categoryDir))
                    {
                        Directory.CreateDirectory(categoryDir);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DownloadManager] 创建缓存目录失败: {package.name} (Publisher: {package.publisher})\n错误: {e.Message}");
                    OnFailed?.Invoke(package.id, $"无法创建缓存目录 (发布商名称包含非法字符)");
                    return false;
                }

                bool downloadSuccess = await StartUnityDownloadAsync(
                    package.id,
                    downloadInfo.url,
                    downloadInfo.key,
                    safePublisher,
                    safeCategory,
                    safeName
                );

                if (downloadSuccess)
                {
                    
                    int waitCount = 0;
                    long lastBytes = 0;
                    DateTime lastChange = DateTime.Now;
                    bool downloadStarted = false;
                    
                    long lastSpeedBytes = 0;
                    DateTime lastSpeedTime = DateTime.MinValue;
                    
                    string cacheFolder = Path.GetDirectoryName(expectedPath);
                    string tmpFile1 = Path.Combine(cacheFolder, $".{safeName}-{package.id}.tmp");
                    string tmpFile2 = Path.Combine(cacheFolder, $".{safeName}-content__{package.id}.tmp");
                    
                    while (waitCount < 600)
                    {
                        if (_cts?.Token.IsCancellationRequested == true)
                        {
                            Debug.Log($"[DownloadManager] 等待过程中被取消");
                            return false;
                        }

                        await Task.Delay(1000);
                        waitCount++;

                        // 检查Unity是否还在下载
                        bool isDownloading = IsUnityDownloading(
                            package.id,
                            downloadInfo.url,
                            downloadInfo.key,
                            safePublisher,
                            safeCategory,
                            safeName
                        );

                        if (isDownloading)
                        {
                            if (!downloadStarted)
                            {
                                downloadStarted = true;
                            }

                            string actualTmpFile = null;
                            FileInfo tmpFileInfo = null;
                            
                            if (File.Exists(tmpFile1))
                            {
                                FileInfo info1 = new FileInfo(tmpFile1);
                                if (File.Exists(tmpFile2))
                                {
                                    FileInfo info2 = new FileInfo(tmpFile2);
                                    // 选择最新修改的文件
                                    tmpFileInfo = info1.LastWriteTime > info2.LastWriteTime ? info1 : info2;
                                    actualTmpFile = tmpFileInfo.FullName;
                                }
                                else
                                {
                                    tmpFileInfo = info1;
                                    actualTmpFile = tmpFile1;
                                }
                            }
                            else if (File.Exists(tmpFile2))
                            {
                                tmpFileInfo = new FileInfo(tmpFile2);
                                actualTmpFile = tmpFile2;
                            }

                            if (tmpFileInfo != null)
                            {
                                try
                                {
                                    tmpFileInfo.Refresh();
                                    long currentBytes = tmpFileInfo.Length;
                                    long totalBytes = package.size;

                                    if (currentBytes != lastBytes)
                                    {
                                        lastBytes = currentBytes;
                                        DateTime now = DateTime.Now;
                                        lastChange = now;
                                        
                                        double speedBps = 0;
                                        if (lastSpeedTime != DateTime.MinValue)
                                        {
                                            double deltaSec = (now - lastSpeedTime).TotalSeconds;
                                            if (deltaSec > 0)
                                            {
                                                long deltaBytes = currentBytes - lastSpeedBytes;
                                                speedBps = deltaBytes / deltaSec;
                                            }
                                        }
                                    lastSpeedBytes = currentBytes;
                                    lastSpeedTime = now;
                                    
                                    OnProgress?.Invoke(package.id, package.name, currentBytes, totalBytes, speedBps);
                                }
                            }
                            catch (IOException)
                            {
                            }
                            catch (UnauthorizedAccessException)
                            {
                            }
                        }
                    }
                    else
                    {
                        if (downloadStarted)
                        {
                            if (File.Exists(expectedPath))
                            {
                                await Task.Delay(500);
                                await CopyToExportAsync(expectedPath, safeName, package.version);
                                OnCompleted?.Invoke(package.id, package.name);
                                return true;
                            }
                        }
                    }

                    if (File.Exists(expectedPath))
                    {
                        await Task.Delay(500);
                        await CopyToExportAsync(expectedPath, safeName, package.version);
                        OnCompleted?.Invoke(package.id, package.name);
                        return true;
                    }

                         if (downloadStarted && lastBytes > 0 && (DateTime.Now - lastChange).TotalSeconds > 120)
                        {
                            Debug.LogWarning($"[DownloadManager] 下载卡住超过120秒");
                            return false;
                        }
                    }

                    Debug.LogWarning($"[DownloadManager] 下载超时（600秒）");
                    return false;
                }
                else
                {
                    Debug.LogWarning($"[DownloadManager] 启动下载失败");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadManager] 下载 {package.name} 失败: {e.Message}\n{e.StackTrace}");
                throw; // 重新抛出异常，让外层重试逻辑处理
            }
        }

        private async Task<bool> StartUnityDownloadAsync(
            int foreignId,
            string url,
            string key,
            string safePublisher,
            string safeCategory,
            string safeName)
        {
            try
            {
                Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
                Type assetStoreUtils = assembly.GetType("UnityEditor.AssetStoreUtils");
                if (assetStoreUtils == null)
                {
                    Debug.LogError("[DownloadManager] 无法找到 UnityEditor.AssetStoreUtils");
                    return false;
                }

                MethodInfo downloadMethod = assetStoreUtils.GetMethod("Download", BindingFlags.Public | BindingFlags.Static);
                if (downloadMethod == null)
                {
                    Debug.LogError("[DownloadManager] 无法找到 Download 方法");
                    return false;
                }

                DownloadState dls = new DownloadState
                {
                    download = new DownloadStateDetails
                    {
                        url = url,
                        key = key
                    }
                };

                string json = JsonConvert.SerializeObject(dls);
                string keyStr = foreignId.ToString();

                await Task.Run(() =>
                {
                    ThreadUtils.InvokeOnMainThread(downloadMethod, null, new object[]
                    {
                        keyStr,
                        url,
                        new[] { safePublisher, safeCategory, safeName },
                        key,
                        json,
                        false,
                        null
                    });
                });

                Debug.Log($"[DownloadManager] InvokeOnMainThread调用完成");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadManager] 启动Unity下载失败: {e.Message}\n{e.StackTrace}");
                return false;
            }
        }

        private DownloadState CheckDownloadProgress(
            int foreignId,
            string url,
            string key,
            string safePublisher,
            string safeCategory,
            string safeName)
        {
            try
            {
                Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
                Type assetStoreUtils = assembly.GetType("UnityEditor.AssetStoreUtils");
                MethodInfo checkDownloadMethod = assetStoreUtils?.GetMethod("CheckDownload", BindingFlags.Public | BindingFlags.Static);

                if (checkDownloadMethod == null) return null;

                string keyStr = foreignId.ToString();
                string result = (string)checkDownloadMethod.Invoke(null, new object[]
                {
                    keyStr,
                    url,
                    new[] { safePublisher, safeCategory, safeName },
                    key
                });

                if (string.IsNullOrEmpty(result)) return null;

                return JsonConvert.DeserializeObject<DownloadState>(result);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DownloadManager] CheckDownload失败: {e.Message}");
                return null;
            }
        }

        private bool IsUnityDownloading(
            int foreignId,
            string url,
            string key,
            string safePublisher,
            string safeCategory,
            string safeName)
        {
            var state = CheckDownloadProgress(foreignId, url, key, safePublisher, safeCategory, safeName);
            return state != null && state.inProgress;
        }

        private async Task CopyToExportAsync(string sourcePath, string packageName, string version)
        {
            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"源文件不存在: {sourcePath}");
                return;
            }

            if (!Directory.Exists(_exportPath))
            {
                Directory.CreateDirectory(_exportPath);
            }

            string fileName = $"{SanitizeFileName(packageName)}_v{version}.unitypackage";
            string destPath = Path.Combine(_exportPath, fileName);

            try
            {
                await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: true));
                Debug.Log($"已导出: {fileName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"复制文件到导出目录失败: {e.Message}");
                throw;
            }
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "Unknown";
            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            
            fileName = fileName.Replace('.', '_');
            fileName = fileName.Replace(',', '_');
            fileName = fileName.Replace(';', '_');
            fileName = fileName.Replace(':', '_');
            
            if (fileName.Length > 200)
            {
                fileName = fileName.Substring(0, 200);
            }
            
            return fileName.Trim();
        }
    }
}
