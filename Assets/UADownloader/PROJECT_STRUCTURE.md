# UA Downloader - 项目结构

```
Assets/UADownloader/
│
├── Editor/                                    # 编辑器专用代码
│   ├── Scripts/                              # C# 脚本文件
│   │   ├── PackageData.cs                    # 数据模型定义（108行）
│   │   ├── AssetStoreAPI.cs                  # Asset Store API 封装（170行）
│   │   ├── DownloadManager.cs                # 下载管理器（214行）
│   │   └── PackageBatchDownloader.cs         # 主窗口 UI（460+行）
│   │
│   ├── Resources/                            # 编辑器资源
│   │   └── (图标等资源，可选)
│   │
│   └── UADownloader.Editor.asmdef            # Assembly Definition
│
├── README.md                                  # 完整使用文档（~250行）
├── QUICKSTART.md                              # 快速启动指南（~200行）
├── TESTING_CHECKLIST.md                       # 测试清单（~400行）
├── IMPLEMENTATION_SUMMARY.md                  # 实现总结
└── PROJECT_STRUCTURE.md                       # 本文件

根目录:
└── AGENTS.md                                  # AI 编码代理指南（~200行）
```

## 文件说明

### 核心代码文件（950+ 行代码）

#### PackageData.cs
**用途**：数据模型和 JSON 序列化类
**包含**：
- `PackageInfo` - 资源包基本信息
- `PackageList` - 资源包列表容器
- `DownloadIndex` - 下载进度索引
- `AssetPurchases`, `AssetPurchase` - API 购买记录响应
- `AssetDetails` - API 资源详情响应
- `DownloadInfoResult`, `DownloadDetails` - API 下载信息响应

#### AssetStoreAPI.cs
**用途**：Asset Store API 交互层
**核心方法**：
- `GetToken()` - 获取 Unity 登录 Token
- `FetchPurchasesAsync()` - 获取购买列表（分页+并行）
- `FetchAssetDetailsAsync(PackageInfo)` - 获取资源详情
- `FetchDownloadInfoAsync(int id)` - 获取下载信息

#### DownloadManager.cs
**用途**：下载流程编排和文件管理
**核心方法**：
- `DownloadPackageAsync(PackageInfo, cachePath)` - 完整下载流程
- `StartUnityDownloadAsync(...)` - Unity 反射下载调用
- `CopyToExportAsync(...)` - 文件复制到导出目录
**事件**：
- `OnProgress(id, name, progress)` - 下载进度
- `OnCompleted(id, name)` - 下载完成
- `OnFailed(id, error)` - 下载失败

#### PackageBatchDownloader.cs
**用途**：主 EditorWindow 窗口
**核心功能**：
- UI 渲染（OnGUI）
- 状态管理（UIState 状态机）
- 文件持久化（packages.json, index.json）
- 异步任务协调
- 用户交互响应

### 配置文件

#### UADownloader.Editor.asmdef
**用途**：Unity Assembly Definition
**配置**：
- 引用 `AssetInventory.Editor`
- 仅 Editor 平台
- 命名空间：`UADownloader`

### 文档文件（1050+ 行文档）

#### AGENTS.md
**位置**：项目根目录
**用途**：AI 编码代理指南
**内容**：
- 构建/测试/运行命令
- 代码风格指南
- Asset Store API 使用约定
- 下载流程实现要点

#### README.md
**用途**：完整使用文档
**内容**：
- 功能说明
- 使用步骤（6步）
- 文件格式说明（packages.json, index.json）
- 技术实现细节
- 故障排除

#### QUICKSTART.md
**用途**：快速启动指南
**内容**：
- 准备工作
- 首次使用（6步）
- 常用操作
- 故障排除（5个常见问题）
- 高级用法

#### TESTING_CHECKLIST.md
**用途**：详细测试清单
**内容**：
- 编译测试
- 功能测试（约80个测试项）
- 边界条件测试
- 性能测试
- 代码质量检查

#### IMPLEMENTATION_SUMMARY.md
**用途**：实现总结
**内容**：
- 完成工作清单
- 架构设计图
- 核心功能实现
- 技术亮点
- 测试状态
- 后续优化建议

## 依赖关系

### 外部依赖

```
UADownloader
    ├─> Unity Editor (2019.4+)
    ├─> Newtonsoft.Json
    └─> AssetInventory Plugin
            ├─> AssetInventory.AI
            ├─> AssetInventory.AssetUtils
            ├─> AssetInventory.ThreadUtils
            └─> AssetInventory.IOUtils
```

### 内部依赖

```
PackageBatchDownloader.cs (UI 层)
    └─> DownloadManager.cs (业务逻辑层)
            ├─> AssetStoreAPI.cs (API 层)
            │       └─> PackageData.cs (数据层)
            └─> PackageData.cs (数据层)
```

## 数据流

### 获取资源列表流程

```
用户点击"获取资源列表快照"
    │
    ▼
PackageBatchDownloader.FetchPackageListAsync()
    │
    ▼
AssetStoreAPI.FetchPurchasesAsync()
    │
    ├─> 第一页（PAGE_SIZE=100）
    │   └─> AssetUtils.FetchAPIData<AssetPurchases>(...)
    │
    ├─> 剩余页（并行）
    │   └─> Task.WhenAll(多个 FetchAPIData 调用)
    │
    └─> 隐藏购买项
        └─> AssetUtils.FetchAPIData<AssetPurchases>(...?status=hidden)
    │
    ▼
合并去重，转换为 PackageInfo 列表
    │
    ▼
保存到 packages.json
    │
    ▼
更新 UI（显示资源列表）
```

### 下载资源流程

```
用户点击"开始批量下载"
    │
    ▼
PackageBatchDownloader.StartBatchDownloadAsync()
    │
    ▼
遍历 _packageList.packages（从 currentIndex 开始）
    │
    ├─> AssetStoreAPI.FetchAssetDetailsAsync(package)
    │       └─> 获取版本和文件大小
    │
    └─> DownloadManager.DownloadPackageAsync(package)
            │
            ├─> AssetStoreAPI.FetchDownloadInfoAsync(id)
            │       └─> 获取下载 URL 和 Key
            │
            ├─> 检查 Unity 缓存是否已存在
            │   └─> 如果存在，直接复制
            │
            ├─> StartUnityDownloadAsync(...)
            │   │
            │   └─> 反射调用 UnityEditor.AssetStoreUtils.Download()
            │       └─> ThreadUtils.InvokeOnMainThread(...)
            │
            ├─> 轮询等待文件出现（最多 600 秒）
            │   └─> File.Exists(expectedPath)
            │
            └─> CopyToExportAsync(...)
                └─> File.Copy(source, destination)
    │
    ▼
更新 index.json（downloaded, failed, currentIndex）
    │
    ▼
继续下一个资源包
```

## 状态持久化

### packages.json
**位置**：`{导出目录}/packages.json`
**格式**：
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

### index.json
**位置**：`{导出目录}/index.json`
**格式**：
```json
{
  "currentIndex": 5,
  "totalCount": 100,
  "downloaded": [12345, 67890],
  "failed": [11111],
  "lastUpdate": "2026-03-04T01:30:00Z",
  "currentDownloadingId": "12345"
}
```

### EditorPrefs
**键**：`UADownloader.ExportPath`
**值**：导出目录路径（string）

## Unity 缓存目录

### 探测优先级
1. `Config.assetCachePath`（用户自定义）
2. `UnityEditorInternal.AssetStoreCachePathManager.GetConfig().path`（Unity 内部 API）
3. `ASSETSTORE_CACHE_PATH` 环境变量
4. 平台默认路径：
   - Windows: `%AppData%/Unity/Asset Store`
   - macOS: `~/Library/Unity/Asset Store`
   - Linux: `~/.local/share/unity3d/Asset Store`

### 文件路径格式
```
{Unity缓存目录}/
    {Publisher}/
        {Category}/
            {PackageName}.unitypackage
```

## 菜单项

### MenuItem 路径
`Tools → UA Downloader → Package Batch Downloader`

### 实现代码
```csharp
[MenuItem("Tools/UA Downloader/Package Batch Downloader")]
public static void ShowWindow()
{
    var window = GetWindow<PackageBatchDownloader>("批量资源包下载器");
    window.minSize = new Vector2(800, 600);
    window.Show();
}
```

## 事件流

### 下载进度事件
```
DownloadManager.OnProgress
    │
    ▼
PackageBatchDownloader._currentProgress 更新
    │
    ▼
Repaint() 触发 UI 刷新
```

### 下载完成事件
```
DownloadManager.OnCompleted
    │
    ▼
PackageBatchDownloader._downloadIndex.downloaded.Add(id)
    │
    ▼
SaveDownloadIndex()
    │
    ▼
Repaint() 触发 UI 刷新
```

### 下载失败事件
```
DownloadManager.OnFailed
    │
    ▼
Debug.LogError(...)
    │
    ▼
PackageBatchDownloader._downloadIndex.failed.Add(id)（在主循环中）
```

## 线程模型

### 主线程（Unity Main Thread）
- UI 渲染（OnGUI）
- Unity API 调用（通过 ThreadUtils.InvokeOnMainThread）
- EditorPrefs 读写
- Repaint() 调用

### 后台线程（Task.Run）
- HTTP 请求（通过 AssetUtils.FetchAPIData）
- 文件复制（File.Copy）
- JSON 序列化/反序列化

### 线程切换
```
后台线程（网络请求完成）
    │
    └─> 需要调用 Unity API
            │
            ▼
        ThreadUtils.InvokeOnMainThread(...)
            │
            ▼
        SynchronizationContext.Post(...)
            │
            ▼
        主线程执行
```

## 错误处理策略

### 网络错误
- **捕获**：在 AssetStoreAPI 层
- **处理**：返回 null，调用者判断
- **日志**：Debug.LogWarning
- **用户反馈**：状态消息显示

### 文件I/O错误
- **捕获**：在 DownloadManager 层
- **处理**：返回 false，标记失败
- **日志**：Debug.LogError
- **继续**：不影响后续下载

### Unity API 错误
- **捕获**：在 StartUnityDownloadAsync
- **处理**：返回 false
- **日志**：Debug.LogError
- **用户反馈**：资源标记为失败

### 超时处理
- **检测**：轮询计数器（waitCount < 600）
- **处理**：返回 false，OnFailed 事件
- **日志**：Debug.LogError("下载超时")
- **继续**：下载下一个资源

## 扩展点

### 添加新的数据字段
1. 在 `PackageData.cs` 中添加字段
2. 在 `AssetStoreAPI.cs` 中解析字段
3. 在 `PackageBatchDownloader.cs` 中显示字段

### 添加新的 UI 功能
1. 在 `PackageBatchDownloader.cs` 中添加状态变量
2. 在 `DrawXxxSection()` 方法中添加 UI 元素
3. 添加对应的事件处理方法

### 添加新的下载策略
1. 在 `DownloadManager.cs` 中添加新方法
2. 实现下载逻辑
3. 触发相应的事件（OnProgress, OnCompleted, OnFailed）
4. 在 `PackageBatchDownloader` 中调用

## 版本信息

- **版本号**：v1.0.0
- **发布日期**：2026-03-04
- **最低 Unity 版本**：2019.4（推荐）
- **依赖版本**：
  - AssetInventory: 任意版本（需要包含工具类）
  - Newtonsoft.Json: Unity 内置版本

## 许可和版权

本项目基于 AssetInventory 插件的 Asset Store 功能提取而来。
使用时请遵守相应的许可协议。

---

**维护者备注**：
此项目结构文档反映了 v1.0.0 的实现状态。
如有修改，请同步更新本文档。
