# Unity Asset Downloader - AGENTS.md

本文档为AI编码代理提供项目规范和最佳实践指导。

## 项目概述

这是一个Unity编辑器项目，目标是从AssetInventory插件中提取Asset Store相关功能，实现独立的批量资源包下载器(PackageBatchDownloader)。

## 构建、测试、运行命令

### 构建
- 项目通过Unity编辑器自动编译
- 手动触发编译：Unity菜单 → Assets → Reimport All

### 测试
- 单元测试：Unity Test Runner (Window → General → Test Runner)
- 运行单个测试：在Test Runner中右键测试名 → Run

### 运行
- 打开PackageBatchDownloader窗口：Unity菜单 → Tools → UA Downloader → Package Batch Downloader

## 代码风格指南

### 1. Using语句组织

**排序规则**（严格遵循）：
```csharp
// 1. System 与 .NET 命名空间
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// 2. 第三方库
using Newtonsoft.Json;

// 3. Unity 编辑器与运行时命名空间
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

// 4. 项目内部命名空间
using AssetInventory;

// 5. using static 和别名（可选，放在末尾）
using static AssetInventory.AssetTreeViewControl;
using Debug = UnityEngine.Debug;
```

### 2. 命名约定

#### 类型、方法、属性、枚举
- **使用PascalCase**
```csharp
public class PackageBatchDownloader : EditorWindow
public void StartDownload()
public string ExportPath { get; set; }
public enum DownloadState { Pending, Downloading, Completed }
```

#### 私有字段
- **使用_camelCase前缀**
```csharp
private AssetInfo _asset;
private GridControl _pgrid;
private bool _requireAssetTreeRebuild;
```

#### 序列化字段
- **Unity序列化字段使用_camelCase或m_prefix（兼容历史代码）**
```csharp
[SerializeField] private string _exportPath;          // 推荐
[SerializeField] private MultiColumnHeaderState assetMchState;  // 可接受
[SerializeField] protected int m_ID;                  // 历史兼容
```

#### 常量
- **使用UPPER_SNAKE_CASE**
```csharp
private const int PAGE_SIZE = 100;
private const string CACHE_FOLDER_NAME = "AssetStoreCache";
```

### 3. 类组织

#### 典型EditorWindow结构
```csharp
public sealed class PackageBatchDownloader : EditorWindow
{
    // 1. 常量
    private const int WINDOW_WIDTH = 800;
    
    // 2. 静态字段
    private static PackageBatchDownloader _instance;
    
    // 3. 序列化字段
    [SerializeField] private string _exportPath;
    
    // 4. 私有实例字段
    private List<PackageInfo> _packages;
    private bool _isDownloading;
    
    // 5. 属性
    public string ExportPath 
    { 
        get => _exportPath;
        set => _exportPath = value;
    }
    
    // 6. Unity生命周期方法
    [MenuItem("Tools/UA Downloader/Package Batch Downloader")]
    public static void ShowWindow()
    {
        _instance = GetWindow<PackageBatchDownloader>("Package Batch Downloader");
        _instance.Show();
    }
    
    private void OnEnable() { }
    private void OnDisable() { }
    private void OnGUI() { }
    
    // 7. 公共方法
    public void StartDownload() { }
    
    // 8. 私有方法
    private void UpdateProgress() { }
}
```

#### 大型类使用partial分割
```csharp
// IndexUI.cs - 主类定义
public partial class IndexUI : EditorWindow { }

// IndexUI+Packages.cs - 包管理相关
public partial class IndexUI { }

// IndexUI+Search.cs - 搜索相关
public partial class IndexUI { }
```

### 4. 错误处理

#### 文件I/O错误处理
```csharp
try
{
    if (File.Exists(filePath))
    {
        string content = IOUtils.ReadAllTextWithShare(filePath);
        // 处理内容
    }
}
catch (Exception e)
{
    Debug.LogWarning($"Error reading file '{filePath}': {e.Message}");
    // 设置状态为Unknown或返回默认值
    return null;
}
```

#### UI层错误显示
```csharp
if (string.IsNullOrEmpty(_exportPath))
{
    EditorGUILayout.HelpBox("请先设置导出目录", MessageType.Warning);
    if (GUILayout.Button("选择目录", GUILayout.Width(100)))
    {
        _exportPath = EditorUtility.OpenFolderPanel("选择导出目录", "", "");
    }
    return;
}
```

#### API调用错误处理
```csharp
// 捕获但不向上抛出，避免编辑器崩溃
try
{
    var result = await AssetStore.RetrieveAssetDetails(id);
    if (result == null)
    {
        Debug.LogWarning($"Asset {id} not found or not modified");
        return;
    }
}
catch (Exception e)
{
    Debug.LogError($"Failed to retrieve asset details for {id}: {e.Message}");
    // 不要throw，返回或设置错误状态
    return;
}
```

### 5. 异步编程模式

#### 异步方法签名
```csharp
// 优先使用 async Task
private async Task CheckForUpdates()
{
    await Task.Delay(1000);
    // 异步操作
}

// 仅在事件处理器中使用 async void
private async void OnDownloadCompleted(object sender, EventArgs e)
{
    await ProcessDownloadResult();
}
```

#### Fire-and-forget启动
```csharp
private void Init()
{
    // 不阻塞初始化
    _ = CheckForToolUpdates();
    _ = CheckForAssetUpdates();
}
```

#### CancellationToken管理
```csharp
public class PackageBatchDownloader : EditorWindow
{
    private CancellationTokenSource _downloadCts;
    
    private void OnEnable()
    {
        _downloadCts = new CancellationTokenSource();
    }
    
    private void OnDisable()
    {
        // 取消并清理
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
    }
    
    private async Task DownloadAsync()
    {
        try
        {
            await Task.Delay(1000, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("Download cancelled");
        }
    }
}
```

#### 主线程调用
```csharp
// 从后台线程调用Unity API时使用
ThreadUtils.InvokeOnMainThread(methodInfo, null, new object[] { param1, param2 });
```

### 6. JSON序列化

**统一使用Newtonsoft.Json**
```csharp
using Newtonsoft.Json;

// 序列化
string json = JsonConvert.SerializeObject(data, Formatting.Indented);
File.WriteAllText(path, json);

// 反序列化
string json = IOUtils.ReadAllTextWithShare(path);
var data = JsonConvert.DeserializeObject<MyType>(json);
```

### 7. 文件I/O最佳实践

```csharp
// 使用共享读取避免文件锁
string content = IOUtils.ReadAllTextWithShare(filePath);
byte[] data = IOUtils.ReadAllBytesWithShare(filePath);

// 写入临时文件
string tempPath = Path.Combine(tempFolder, ".temp_" + fileName);
File.WriteAllText(tempPath, content);

// 检查磁盘空间
long freeSpace = IOUtils.GetFreeSpace(folder);
if (freeSpace >= 0 && freeSpace < requiredSpace)
{
    Debug.LogError($"磁盘空间不足。需要: {requiredSpace}, 可用: {freeSpace}");
    return;
}
```

## Asset Store API 使用约定

### 1. 授权认证

**获取Token**
```csharp
using UnityEditor;

string token = CloudProjectSettings.accessToken;
if (string.IsNullOrEmpty(token))
{
    Debug.LogError("未登录Asset Store，请先在Unity中登录");
    return;
}
```

**设置Authorization头**
```csharp
// 在UnityWebRequest中
uwr.SetRequestHeader("Authorization", $"Bearer {token}");
```

### 2. 获取购买列表（Purchases）

**API端点**
```
GET https://packages-v2.unity.com/-/api/purchases?offset={offset}&limit={limit}
```

**实现模式**
```csharp
private const int PAGE_SIZE = 100;
private const string URL_PURCHASES = "https://packages-v2.unity.com/-/api/purchases";

public async Task<List<AssetPurchase>> FetchPurchases()
{
    var allPurchases = new List<AssetPurchase>();
    int offset = 0;
    
    // 第一页
    string url = $"{URL_PURCHASES}?offset={offset}&limit={PAGE_SIZE}";
    var result = await FetchAPIData<AssetPurchases>(url, "GET", null, token);
    
    if (result?.results != null)
    {
        allPurchases.AddRange(result.results);
        
        // 并行获取剩余页
        if (result.total > PAGE_SIZE)
        {
            var tasks = new List<Task<AssetPurchases>>();
            for (offset = PAGE_SIZE; offset < result.total; offset += PAGE_SIZE)
            {
                string pageUrl = $"{URL_PURCHASES}?offset={offset}&limit={PAGE_SIZE}";
                tasks.Add(FetchAPIData<AssetPurchases>(pageUrl, "GET", null, token));
            }
            
            var results = await Task.WhenAll(tasks);
            foreach (var pageResult in results)
            {
                if (pageResult?.results != null)
                    allPurchases.AddRange(pageResult.results);
            }
        }
    }
    
    // 获取隐藏的购买项
    url = $"{URL_PURCHASES}?status=hidden&offset=0&limit=1000";
    var hiddenResult = await FetchAPIData<AssetPurchases>(url, "GET", null, token);
    if (hiddenResult?.results != null)
    {
        // 去重合并
        var existingIds = new HashSet<string>(allPurchases.Select(p => p.packageId));
        allPurchases.AddRange(hiddenResult.results.Where(p => !existingIds.Contains(p.packageId)));
    }
    
    return allPurchases;
}
```

### 3. 获取资源详情（Asset Details）

**API端点**
```
GET https://packages-v2.unity.com/-/api/product/{id}
```

**ETag缓存支持**
```csharp
private const string URL_ASSET_DETAILS = "https://packages-v2.unity.com/-/api/product";

public async Task<AssetDetails> FetchAssetDetails(int id, string cachedETag = null)
{
    string newETag = null;
    string url = $"{URL_ASSET_DETAILS}/{id}";
    
    var result = await FetchAPIData<AssetDetails>(
        url, 
        "GET", 
        null, 
        token, 
        cachedETag,                              // If-None-Match
        eTag => newETag = eTag,                  // ETag回调
        retries: 1
    );
    
    if (result == null)
    {
        // 304 Not Modified - 使用缓存
        Debug.Log($"Asset {id} unchanged (ETag match)");
        return null;
    }
    
    // 保存新的ETag供下次使用
    if (!string.IsNullOrEmpty(newETag))
    {
        SaveETag(id, newETag);
    }
    
    return result;
}
```

### 4. 获取下载信息（Download Info）

**API端点**
```
GET https://packages-v2.unity.com/-/api/legacy-package-download-info/{id}
```

**实现模式**
```csharp
private const string URL_ASSET_DOWNLOAD = "https://packages-v2.unity.com/-/api/legacy-package-download-info";

public async Task<DownloadInfo> FetchDownloadInfo(int id)
{
    string url = $"{URL_ASSET_DOWNLOAD}/{id}";
    var result = await FetchAPIData<DownloadInfoResult>(url, "GET", null, token);
    
    if (result?.result?.download == null)
    {
        Debug.LogWarning($"No download info for asset {id}");
        return null;
    }
    
    var download = result.result.download;
    
    // 清理filename_safe字段（Unity API返回可能包含无效字符）
    if (!string.IsNullOrEmpty(download.filename_safe_package_name))
    {
        download.filename_safe_package_name = download.filename_safe_package_name.Replace(".", "_");
    }
    
    return new DownloadInfo
    {
        Url = download.url,
        Key = download.key,
        SafePublisher = download.filename_safe_publisher_name,
        SafeCategory = download.filename_safe_category_name,
        SafeName = download.filename_safe_package_name,
        Size = long.Parse(download.size ?? "0")
    };
}
```

### 5. UnityWebRequest通用封装

```csharp
public async Task<T> FetchAPIData<T>(
    string uri, 
    string method = "GET", 
    string postContent = null, 
    string token = null,
    string etag = null,
    Action<string> eTagCallback = null,
    int retries = 1)
{
    Restart:
    
    using (var uwr = new UnityWebRequest(uri, method))
    {
        // 设置handlers
        if (!string.IsNullOrEmpty(postContent))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(postContent);
            uwr.uploadHandler = new UploadHandlerRaw(bodyRaw);
            uwr.SetRequestHeader("Content-Type", "application/json");
        }
        
        uwr.downloadHandler = new DownloadHandlerBuffer();
        
        // 设置请求头
        if (!string.IsNullOrEmpty(token))
        {
            uwr.SetRequestHeader("Authorization", $"Bearer {token}");
        }
        
        if (!string.IsNullOrEmpty(etag))
        {
            uwr.SetRequestHeader("If-None-Match", etag);
        }
        
        uwr.SetRequestHeader("User-Agent", $"UnityEditor/{Application.unityVersion}");
        
        // 发送请求
        var op = uwr.SendWebRequest();
        while (!op.isDone)
        {
            await Task.Yield();
        }
        
        // 检查结果
#if UNITY_2020_1_OR_NEWER
        if (uwr.result == UnityWebRequest.Result.ConnectionError)
#else
        if (uwr.isNetworkError)
#endif
        {
            if (retries > 0)
            {
                Debug.LogWarning($"Network error, retrying... ({retries} left)");
                retries--;
                await Task.Delay(1000);
                goto Restart;
            }
            
            Debug.LogError($"Network error: {uwr.error}");
            return default(T);
        }
        
#if UNITY_2020_1_OR_NEWER
        if (uwr.result == UnityWebRequest.Result.ProtocolError)
#else
        if (uwr.isHttpError)
#endif
        {
            if (uwr.responseCode == 304)
            {
                // Not Modified - 返回null表示使用缓存
                return default(T);
            }
            
            Debug.LogError($"HTTP error {uwr.responseCode}: {uwr.error}");
            return default(T);
        }
        
        // 读取ETag
        string newETag = uwr.GetResponseHeader("ETag");
        if (!string.IsNullOrEmpty(newETag))
        {
            eTagCallback?.Invoke(newETag);
        }
        
        // 解析JSON
        string responseText = uwr.downloadHandler.text;
        if (typeof(T) == typeof(string))
        {
            return (T)(object)responseText;
        }
        
        return JsonConvert.DeserializeObject<T>(responseText);
    }
}
```

### 6. Unity缓存目录处理

**缓存路径探测优先级**
```csharp
public static string GetAssetCacheFolder()
{
    // 1. 用户自定义路径
    if (!string.IsNullOrEmpty(Config.assetCachePath))
    {
        return Config.assetCachePath;
    }
    
    // 2. Unity内部API（通过反射）
    try
    {
        Assembly assembly = Assembly.Load("UnityEditor.CoreModule");
        Type managerType = assembly.GetType("UnityEditorInternal.AssetStoreCachePathManager");
        if (managerType != null)
        {
            MethodInfo getConfig = managerType.GetMethod("GetConfig", BindingFlags.Public | BindingFlags.Static);
            if (getConfig != null)
            {
                object config = getConfig.Invoke(null, null);
                PropertyInfo pathProp = config.GetType().GetProperty("path");
                string path = (string)pathProp.GetValue(config);
                if (!string.IsNullOrEmpty(path))
                {
                    return Path.Combine(path, "AssetStore");
                }
            }
        }
    }
    catch (Exception e)
    {
        Debug.LogWarning($"Failed to get Unity cache path: {e.Message}");
    }
    
    // 3. 环境变量
    string envPath = Environment.GetEnvironmentVariable("ASSETSTORE_CACHE_PATH");
    if (!string.IsNullOrEmpty(envPath))
    {
        return envPath;
    }
    
    // 4. 平台默认路径
#if UNITY_EDITOR_WIN
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store");
#elif UNITY_EDITOR_OSX
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Unity", "Asset Store");
#else
    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "share", "unity3d", "Asset Store");
#endif
}
```

### 7. 下载状态与临时文件约定

**临时文件命名规则**
```csharp
string cacheFolder = GetAssetCacheFolder();
string safeName = SanitizeFileName(packageName);
int foreignId = asset.ForeignId;

// 主下载文件
string downloadFile = Path.Combine(cacheFolder, $".{safeName}-{foreignId}.tmp");

// 重新下载文件
string reDownloadFile = Path.Combine(cacheFolder, $".{safeName}-content__{foreignId}.tmp");

// 下载状态JSON
string stateFile = downloadFile + ".json";
```

**DownloadState JSON格式**
```csharp
public class DownloadState
{
    public string url { get; set; }
    public string key { get; set; }
    public bool inProgress { get; set; }
    public long downloadedBytes { get; set; }
    public long totalBytes { get; set; }
    public string error { get; set; }
}

// 保存状态
var state = new DownloadState 
{ 
    url = downloadInfo.Url,
    key = downloadInfo.Key,
    inProgress = true,
    downloadedBytes = 0,
    totalBytes = downloadInfo.Size
};
string json = JsonConvert.SerializeObject(state);
File.WriteAllText(stateFile, json);

// 读取状态（支持断点续传）
if (File.Exists(stateFile))
{
    string json = IOUtils.ReadAllTextWithShare(stateFile);
    var state = JsonConvert.DeserializeObject<DownloadState>(json);
    // 从state.downloadedBytes恢复下载
}
```

### 8. 下载完成后的文件复制

```csharp
private async Task CopyToExportFolder(string sourcePath, string packageName, string version)
{
    if (!File.Exists(sourcePath))
    {
        Debug.LogError($"Source file not found: {sourcePath}");
        return;
    }
    
    // 确保导出目录存在
    if (!Directory.Exists(_exportPath))
    {
        Directory.CreateDirectory(_exportPath);
    }
    
    // 文件名格式: 资源包名_版本.unitypackage
    string fileName = $"{SanitizeFileName(packageName)}_{version}.unitypackage";
    string destPath = Path.Combine(_exportPath, fileName);
    
    try
    {
        // 检查目标文件是否已存在
        if (File.Exists(destPath))
        {
            Debug.LogWarning($"File already exists, overwriting: {fileName}");
        }
        
        // 复制文件
        await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: true));
        
        Debug.Log($"Exported: {fileName}");
    }
    catch (Exception e)
    {
        Debug.LogError($"Failed to copy file to export folder: {e.Message}");
    }
}

private string SanitizeFileName(string fileName)
{
    // 移除或替换非法文件名字符
    char[] invalidChars = Path.GetInvalidFileNameChars();
    foreach (char c in invalidChars)
    {
        fileName = fileName.Replace(c, '_');
    }
    return fileName;
}
```

### 9. 并发控制与重试策略

**并发限制**
```csharp
private const int MAX_CONCURRENT_DOWNLOADS = 3;
private SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(MAX_CONCURRENT_DOWNLOADS);

private async Task DownloadPackagesAsync(List<PackageInfo> packages)
{
    var tasks = new List<Task>();
    
    foreach (var package in packages)
    {
        tasks.Add(Task.Run(async () =>
        {
            await _downloadSemaphore.WaitAsync();
            try
            {
                await DownloadSinglePackageAsync(package);
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }));
    }
    
    await Task.WhenAll(tasks);
}
```

**重试策略**
```csharp
private async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxRetries = 3, int delayMs = 1000)
{
    int attempt = 0;
    
    while (true)
    {
        try
        {
            return await action();
        }
        catch (Exception e)
        {
            attempt++;
            if (attempt >= maxRetries)
            {
                Debug.LogError($"Failed after {maxRetries} attempts: {e.Message}");
                throw;
            }
            
            Debug.LogWarning($"Attempt {attempt} failed, retrying in {delayMs}ms...");
            await Task.Delay(delayMs);
        }
    }
}
```

## PackageBatchDownloader实现要点

### packages.json格式
```json
{
  "packages": [
    {
      "id": 12345,
      "name": "Asset Name",
      "publisher": "Publisher Name",
      "category": "Category",
      "version": "1.2.3",
      "size": 10485760,
      "fetchTime": "2026-03-04T00:00:00Z"
    }
  ]
}
```

### index.json格式
```json
{
  "currentIndex": 5,
  "totalCount": 100,
  "downloaded": [12345, 67890],
  "failed": [11111],
  "lastUpdate": "2026-03-04T01:30:00Z"
}
```

### UI状态管理
```csharp
private enum UIState
{
    Idle,           // 初始状态，可以获取快照和开始下载
    Fetching,       // 正在获取快照
    Ready,          // 快照已获取，可以开始下载
    Downloading,    // 下载中
    Paused,         // 已暂停
    Cancelled       // 已取消
}
```

## 注意事项

1. **永远不要使用类型抑制**
   - 禁止使用 `as any`, `@ts-ignore`, `@ts-expect-error`
   - C#中禁止不必要的类型转换和反射调用

2. **不要主动提交代码**
   - 除非用户明确要求，否则不要执行git commit

3. **保持最小修改原则**
   - 修复bug时只修改必要的代码，不要重构
   - 新功能应该是增量式的，不要大规模重写

4. **EditorWindow生命周期**
   - 在OnEnable中初始化资源
   - 在OnDisable中清理资源（取消异步任务、释放CancellationToken）
   - 避免在OnGUI中执行耗时操作

5. **Unity主线程限制**
   - Unity API只能在主线程调用
   - 文件I/O和网络请求可以在后台线程
   - 使用ThreadUtils.InvokeOnMainThread从后台线程调用Unity API

6. **错误恢复策略**
   - 网络错误应该重试
   - 文件I/O错误应该记录并继续
   - 关键错误应该通过EditorUtility.DisplayDialog通知用户

## 参考文件

- **核心API实现**: `Assets/AssetInventory/Editor/Scripts/Features/AssetStore.cs`
- **下载逻辑**: `Assets/AssetInventory/Editor/Scripts/Importers/AssetDownloader.cs`
- **网络请求封装**: `Assets/AssetInventory/Editor/Scripts/Features/AssetUtils.cs`
- **文件工具**: `Assets/AssetInventory/Editor/Scripts/Utils/IOUtils.cs`
- **UI示例**: `Assets/AssetInventory/Editor/Scripts/GUI/IndexUI+Packages.cs`
