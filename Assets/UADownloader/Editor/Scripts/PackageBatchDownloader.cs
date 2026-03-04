using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using AssetInventory;

namespace UADownloader
{
    public sealed class PackageBatchDownloader : EditorWindow
    {
        private const int WINDOW_WIDTH = 1200;
        private const int WINDOW_HEIGHT = 600;

        [MenuItem("Tools/UA Downloader/Package Batch Downloader")]
        public static void ShowWindow()
        {
            var window = GetWindow<PackageBatchDownloader>("批量资源包下载器");
            window.minSize = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            window.Show();
        }

        private enum UIState
        {
            Idle,
            Fetching,
            Ready,
            Downloading,
            Paused
        }

        [SerializeField] private string _exportPath;
        
        private UIState _currentState = UIState.Idle;
        private PackageList _packageList;
        private DownloadIndex _downloadIndex;
        private DownloadManager _downloadManager;
        private CancellationTokenSource _downloadCts;
        private Vector2 _scrollPos;
        private string _statusMessage;
        private int _currentDownloadingIndex = -1;
        private string _currentDownloadingName;
        private float _currentProgress;
        private long _currentDownloadedBytes;
        private long _currentTotalBytes;
        private double _currentSpeedBps;
        
        private DateTime _downloadStartTime;
        private long _totalDownloadedSize;
        private int _skippedCount;

        private void OnEnable()
        {
            ThreadUtils.Initialize();
            LoadExportPath();
            LoadPackageList();
            LoadDownloadIndex();
        }

        private void OnDisable()
        {
            SaveExportPath();
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            DrawExportPathSection();
            EditorGUILayout.Space(10);

            DrawOverviewSection();
            EditorGUILayout.Space(10);

            DrawActionButtons();
            EditorGUILayout.Space(10);

            DrawProgressSection();
            EditorGUILayout.Space(10);

            DrawPackageList();
        }

        private void DrawExportPathSection()
        {
            EditorGUILayout.LabelField("导出设置", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("导出目录:", GUILayout.Width(70));
            _exportPath = EditorGUILayout.TextField(_exportPath);
            if (GUILayout.Button("浏览", GUILayout.Width(60)))
            {
                string selected = EditorUtility.OpenFolderPanel("选择导出目录", _exportPath ?? "", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    _exportPath = selected;
                    SaveExportPath();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverviewSection()
        {
            EditorGUILayout.LabelField("概览", EditorStyles.boldLabel);
            
            int totalCount = _packageList?.packages?.Count ?? 0;
            int downloadedCount = _downloadIndex?.downloaded?.Count ?? 0;
            int failedCount = _downloadIndex?.failed?.Count ?? 0;
            int remainingCount = totalCount - downloadedCount - failedCount;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"资源包总数: {totalCount}", GUILayout.Width(150));
            EditorGUILayout.LabelField($"已下载: {downloadedCount}", GUILayout.Width(150));
            EditorGUILayout.LabelField($"失败: {failedCount}", GUILayout.Width(150));
            EditorGUILayout.LabelField($"待下载: {remainingCount}");
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        private void DrawActionButtons()
        {
            if (string.IsNullOrEmpty(_exportPath))
            {
                EditorGUILayout.HelpBox("请先设置导出目录", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            GUI.enabled = _currentState == UIState.Idle || _currentState == UIState.Ready;
            if (GUILayout.Button("获取资源列表快照", GUILayout.Height(30)))
            {
                _ = FetchPackageListAsync();
            }

            GUI.enabled = (_currentState == UIState.Ready || _currentState == UIState.Paused) && 
                          _packageList?.packages?.Count > 0;
            if (GUILayout.Button("开始批量下载", GUILayout.Height(30)))
            {
                _ = StartBatchDownloadAsync();
            }

            GUI.enabled = _currentState == UIState.Downloading;
            if (GUILayout.Button("暂停", GUILayout.Height(30)))
            {
                PauseDownload();
            }

            if (GUILayout.Button("取消", GUILayout.Height(30)))
            {
                CancelDownload();
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawProgressSection()
        {
            if (_currentState == UIState.Downloading && _currentDownloadingIndex >= 0)
            {
                EditorGUILayout.LabelField("下载进度", EditorStyles.boldLabel);
                
                int totalCount = _packageList?.packages?.Count ?? 0;
                int downloadedCount = _downloadIndex?.downloaded?.Count ?? 0;
                float overallProgress = totalCount > 0 ? (float)downloadedCount / totalCount : 0f;
                
                EditorGUILayout.LabelField($"总体进度: {downloadedCount}/{totalCount}");
                Rect overallRect = EditorGUILayout.GetControlRect(false, 20);
                EditorGUI.ProgressBar(overallRect, overallProgress, 
                    $"{downloadedCount}/{totalCount} ({(int)(overallProgress * 100)}%)");
                
                EditorGUILayout.Space(8);
                
                EditorGUILayout.LabelField($"当前资源包: {_currentDownloadingName}");
                
                if (_currentTotalBytes > 0)
                {
                    string fileText = $"{FormatBytes(_currentDownloadedBytes)} / {FormatBytes(_currentTotalBytes)} ({(int)(_currentProgress * 100)}%)";
                    Rect fileRect = EditorGUILayout.GetControlRect(false, 20);
                    EditorGUI.ProgressBar(fileRect, Mathf.Clamp01(_currentProgress), fileText);
                    
                    EditorGUILayout.BeginHorizontal();
                    string speedText = _currentSpeedBps > 0 ? $"{FormatBytes((long)_currentSpeedBps)}/s" : "计算中...";
                    EditorGUILayout.LabelField($"速度: {speedText}", GUILayout.Width(200));
                    
                    if (_currentSpeedBps > 0 && _currentTotalBytes > 0)
                    {
                        double remainingBytes = Math.Max(0, _currentTotalBytes - _currentDownloadedBytes);
                        double etaSec = remainingBytes / _currentSpeedBps;
                        string etaText = FormatDuration(etaSec);
                        EditorGUILayout.LabelField($"剩余时间: {etaText}", GUILayout.Width(200));
                    }
                    
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.LabelField("等待获取文件大小...");
                    Rect fileRect = EditorGUILayout.GetControlRect(false, 20);
                    EditorGUI.ProgressBar(fileRect, 0, "准备中...");
                }
                
                EditorGUILayout.Space(8);
                
                TimeSpan elapsed = DateTime.Now - _downloadStartTime;
                string elapsedStr = FormatDuration(elapsed.TotalSeconds);
                string totalSizeStr = FormatBytes(_totalDownloadedSize);
                int failedCount = _downloadIndex?.failed?.Count ?? 0;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"已下载: {downloadedCount}个", GUILayout.Width(150));
                EditorGUILayout.LabelField($"失败: {failedCount}个", GUILayout.Width(100));
                EditorGUILayout.LabelField($"跳过: {_skippedCount}个", GUILayout.Width(100));
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"总大小: {totalSizeStr}", GUILayout.Width(200));
                EditorGUILayout.LabelField($"总耗时: {elapsedStr}", GUILayout.Width(200));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawPackageList()
        {
            if (_packageList?.packages == null || _packageList.packages.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("资源包列表", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0; i < _packageList.packages.Count; i++)
            {
                var package = _packageList.packages[i];
                
                bool isDownloading = (_currentState == UIState.Downloading && _currentDownloadingIndex == i);
                bool isDownloaded = _downloadIndex?.downloaded?.Contains(package.id) ?? false;
                bool isFailed = _downloadIndex?.failed?.Contains(package.id) ?? false;

                Color bgColor = isDownloading ? new Color(0.3f, 0.5f, 0.8f, 0.3f) : Color.clear;
                
                GUIStyle boxStyle = new GUIStyle("box");
                if (isDownloading)
                {
                    boxStyle.normal.background = MakeTex(2, 2, new Color(0.3f, 0.5f, 0.8f, 0.3f));
                }
                
                EditorGUILayout.BeginHorizontal(boxStyle);

                if (isDownloading)
                {
                    GUI.color = new Color(0.4f, 0.8f, 1f);
                    EditorGUILayout.LabelField("⬇", GUILayout.Width(20));
                }
                else if (isDownloaded)
                {
                    GUI.color = Color.green;
                    EditorGUILayout.LabelField("✓", GUILayout.Width(20));
                }
                else if (isFailed)
                {
                    GUI.color = Color.red;
                    EditorGUILayout.LabelField("✗", GUILayout.Width(20));
                }
                else
                {
                    GUI.color = Color.white;
                    EditorGUILayout.LabelField("○", GUILayout.Width(20));
                }
                GUI.color = Color.white;

                string displayName = package.name;
                if (!string.IsNullOrEmpty(package.version))
                {
                    displayName += $"  version:{package.version}";
                }
                if (package.size > 0)
                {
                    displayName += $"  size:{FormatBytes(package.size)}";
                }

                GUIStyle labelStyle = isDownloading ? EditorStyles.boldLabel : EditorStyles.label;
                if (isDownloading)
                {
                    GUI.color = new Color(0.4f, 0.8f, 1f);
                }
                
                EditorGUILayout.LabelField(displayName, labelStyle, GUILayout.Width(750));
                EditorGUILayout.LabelField(package.publisher, labelStyle, GUILayout.Width(200));
                
                GUI.color = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private async Task FetchPackageListAsync()
        {
            _currentState = UIState.Fetching;
            _statusMessage = "正在获取资源列表...";
            Repaint();

            try
            {
                var packages = await AssetStoreAPI.FetchPurchasesAsync();
                if (packages != null && packages.Count > 0)
                {
                    _packageList = new PackageList { packages = packages };
                    SavePackageList();
                    _statusMessage = $"成功获取 {packages.Count} 个资源包";
                    _currentState = UIState.Ready;
                }
                else
                {
                    _statusMessage = "未找到任何购买的资源包";
                    _currentState = UIState.Idle;
                }
            }
            catch (Exception e)
            {
                _statusMessage = $"获取列表失败: {e.Message}";
                _currentState = UIState.Idle;
                Debug.LogError(e);
            }

            Repaint();
        }

        private async Task StartBatchDownloadAsync()
        {
            if (_packageList?.packages == null || _packageList.packages.Count == 0)
            {
                EditorUtility.DisplayDialog("错误", "没有可下载的资源包", "确定");
                return;
            }

            Debug.Log($"[BatchDownloader] 开始批量下载，共 {_packageList.packages.Count} 个资源包");

            _currentState = UIState.Downloading;
            _downloadCts = new CancellationTokenSource();

            if (_downloadIndex == null)
            {
                _downloadIndex = new DownloadIndex
                {
                    currentIndex = 0,
                    totalCount = _packageList.packages.Count,
                    downloaded = new List<int>(),
                    failed = new List<int>()
                };
            }
            else
            {
                Debug.Log($"[BatchDownloader] 从索引 {_downloadIndex.currentIndex} 恢复下载");
            }

            _downloadManager = new DownloadManager(_exportPath);
            _downloadManager.OnProgress += OnDownloadProgress;
            _downloadManager.OnCompleted += OnDownloadCompleted;
            _downloadManager.OnFailed += OnDownloadFailed;
            _downloadManager.StartDownload(_downloadCts);

            _downloadStartTime = DateTime.Now;
            _totalDownloadedSize = 0;
            _skippedCount = 0;

            string unityCachePath = AI.GetAssetCacheFolder();
            SaveDownloadIndex();

            for (int i = _downloadIndex.currentIndex; i < _packageList.packages.Count; i++)
            {
                if (_downloadCts.Token.IsCancellationRequested)
                {
                    Debug.Log("[BatchDownloader] 下载已取消");
                    break;
                }

                var package = _packageList.packages[i];
                
                if (_downloadIndex.downloaded.Contains(package.id))
                {
                    Debug.Log($"[BatchDownloader] 跳过已下载: {package.name} (ID:{package.id})");
                    continue;
                }

                Debug.Log($"[BatchDownloader] [{i+1}/{_packageList.packages.Count}] 开始处理: {package.name} (ID:{package.id})");

                _currentDownloadingIndex = i;
                _currentDownloadingName = package.name;
                _downloadIndex.currentIndex = i;
                _downloadIndex.currentDownloadingId = package.id.ToString();
                SaveDownloadIndex();
                Repaint();

                bool detailsFetched = await AssetStoreAPI.FetchAssetDetailsAsync(package);
                if (detailsFetched)
                {
                    SavePackageList();
                }
                else
                {
                    Debug.LogWarning($"[BatchDownloader] 详情获取失败: {package.name}");
                }

                var downloadInfo = await AssetStoreAPI.FetchDownloadInfoAsync(package.id);
                if (downloadInfo != null)
                {
                    string safeName = SanitizeFileName(downloadInfo.filename_safe_package_name ?? package.name);
                    string expectedFileName = $"{safeName}_v{package.version}.unitypackage";
                    string expectedFilePath = Path.Combine(_exportPath, expectedFileName);
                    
                    if (File.Exists(expectedFilePath))
                    {
                        Debug.Log($"[BatchDownloader] 文件已存在，跳过下载: {expectedFileName}");
                        
                        _skippedCount++;
                        
                        if (!_downloadIndex.downloaded.Contains(package.id))
                        {
                            _downloadIndex.downloaded.Add(package.id);
                        }
                        
                        SaveDownloadIndex();
                        Repaint();
                        continue;
                    }
                }

                bool success = await _downloadManager.DownloadPackageAsync(package, unityCachePath);

                if (success)
                {
                    _totalDownloadedSize += package.size;
                }
                else
                {
                    Debug.LogWarning($"[BatchDownloader] 下载失败: {package.name}");
                    if (!_downloadIndex.failed.Contains(package.id))
                    {
                        _downloadIndex.failed.Add(package.id);
                    }
                }

                SaveDownloadIndex();
                SavePackageList();
                Repaint();
            }

            _currentState = UIState.Ready;
            _currentDownloadingIndex = -1;
            
            TimeSpan elapsed = DateTime.Now - _downloadStartTime;
            string elapsedStr = FormatDuration(elapsed.TotalSeconds);
            string totalSizeStr = FormatBytes(_totalDownloadedSize);
            
            _statusMessage = $"批量下载完成 | 成功:{_downloadIndex.downloaded.Count} | 失败:{_downloadIndex.failed.Count} | 跳过:{_skippedCount} | 总大小:{totalSizeStr} | 总耗时:{elapsedStr}";
            Debug.Log($"[BatchDownloader] {_statusMessage}");
            Repaint();
        }

        private void PauseDownload()
        {
            _downloadCts?.Cancel();
            _currentState = UIState.Paused;
            _statusMessage = "已暂停下载";
            SaveDownloadIndex();
            Repaint();
        }

        private void CancelDownload()
        {
            _downloadCts?.Cancel();
            _downloadIndex = null;
            DeleteDownloadIndex();
            _currentState = UIState.Ready;
            _currentDownloadingIndex = -1;
            _statusMessage = "已取消下载";
            Repaint();
        }

        private void OnDownloadProgress(int id, string name, long downloadedBytes, long totalBytes, double speedBps)
        {
            _currentDownloadingName = name;
            _currentDownloadedBytes = downloadedBytes;
            _currentTotalBytes = totalBytes;
            _currentSpeedBps = speedBps;
            _currentProgress = totalBytes > 0 ? (float)downloadedBytes / totalBytes : 0;
            Repaint();
        }

        private void OnDownloadCompleted(int id, string name)
        {
            if (_downloadIndex != null && !_downloadIndex.downloaded.Contains(id))
            {
                _downloadIndex.downloaded.Add(id);
                _downloadIndex.lastUpdate = DateTime.Now.ToString("o");
            }
        }

        private void OnDownloadFailed(int id, string error)
        {
            Debug.LogError($"下载失败 (ID: {id}): {error}");
        }

        private void LoadExportPath()
        {
            _exportPath = EditorPrefs.GetString("UADownloader.ExportPath", "");
        }

        private void SaveExportPath()
        {
            if (!string.IsNullOrEmpty(_exportPath))
            {
                EditorPrefs.SetString("UADownloader.ExportPath", _exportPath);
            }
        }

        private void LoadPackageList()
        {
            if (string.IsNullOrEmpty(_exportPath)) return;

            string packagesPath = Path.Combine(_exportPath, "packages.json");
            if (File.Exists(packagesPath))
            {
                try
                {
                    string json = File.ReadAllText(packagesPath);
                    _packageList = JsonConvert.DeserializeObject<PackageList>(json);
                    _currentState = UIState.Ready;
                }
                catch (Exception e)
                {
                    Debug.LogError($"加载packages.json失败: {e.Message}");
                }
            }
        }

        private void SavePackageList()
        {
            if (string.IsNullOrEmpty(_exportPath) || _packageList == null) return;

            string packagesPath = Path.Combine(_exportPath, "packages.json");
            try
            {
                string json = JsonConvert.SerializeObject(_packageList, Formatting.Indented);
                File.WriteAllText(packagesPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"保存packages.json失败: {e.Message}");
            }
        }

        private void LoadDownloadIndex()
        {
            if (string.IsNullOrEmpty(_exportPath)) return;

            string indexPath = Path.Combine(_exportPath, "index.json");
            if (File.Exists(indexPath))
            {
                try
                {
                    string json = File.ReadAllText(indexPath);
                    _downloadIndex = JsonConvert.DeserializeObject<DownloadIndex>(json);
                    _currentState = UIState.Paused;
                }
                catch (Exception e)
                {
                    Debug.LogError($"加载index.json失败: {e.Message}");
                }
            }
        }

        private void SaveDownloadIndex()
        {
            if (string.IsNullOrEmpty(_exportPath) || _downloadIndex == null) return;

            string indexPath = Path.Combine(_exportPath, "index.json");
            try
            {
                _downloadIndex.lastUpdate = DateTime.Now.ToString("o");
                string json = JsonConvert.SerializeObject(_downloadIndex, Formatting.Indented);
                File.WriteAllText(indexPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"保存index.json失败: {e.Message}");
            }
        }

        private void DeleteDownloadIndex()
        {
            if (string.IsNullOrEmpty(_exportPath)) return;

            string indexPath = Path.Combine(_exportPath, "index.json");
            try
            {
                if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"删除index.json失败: {e.Message}");
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:F2} {sizes[order]}";
        }

        private static string FormatDuration(double seconds)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds < 0)
                return "--:--";
            
            if (seconds < 60)
                return $"{(int)seconds}秒";
            
            if (seconds < 3600)
            {
                int mins = (int)(seconds / 60);
                int secs = (int)(seconds % 60);
                return $"{mins}分{secs}秒";
            }
            
            int hours = (int)(seconds / 3600);
            int minutes = (int)((seconds % 3600) / 60);
            return $"{hours}小时{minutes}分";
        }

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
        
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;
            
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
    }
}
