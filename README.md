# Unity Asset Store 批量下载器

## 功能说明

这是一个独立的Unity编辑器工具，用于批量下载Asset Store购买的资源包。

### 主要特性

- 获取Asset Store购买列表快照
- 批量下载资源包到导出目录
- 支持暂停/恢复下载
- 支持取消下载
- 自动保存下载进度
- 文件名格式：`资源包名_版本.unitypackage`

## 使用步骤

### 1. 打开工具窗口

在Unity编辑器菜单中选择：`Tools → UA Downloader → Package Batch Downloader`

### 2. 设置导出目录

1. 在"导出目录"字段输入路径，或点击"浏览"按钮选择目录
2. 导出目录会自动保存，下次打开窗口时会自动加载

### 3. 获取资源列表快照

1. 确保已在Unity中登录Asset Store账号
2. 点击"获取资源列表快照"按钮
3. 等待获取完成，工具会显示获取到的资源包数量
4. 快照会保存为 `packages.json` 文件到导出目录

### 4. 开始批量下载

1. 点击"开始批量下载"按钮
2. 工具会：
   - 先下载到Unity的缓存目录
   - 然后复制到导出目录，文件名格式为：`资源包名_版本.unitypackage`
3. 下载进度会保存为 `index.json` 文件

### 5. 暂停/恢复下载

- **暂停**：点击"暂停"按钮，当前下载会停止，进度已保存
- **恢复**：重新点击"开始批量下载"，会从上次停止的位置继续

### 6. 取消下载

- 点击"取消"按钮会停止下载并删除进度文件
- 下次开始下载会从头开始

### 7. 配置过滤器（可选）

过滤器用于在下载前自动跳过不需要的资源包，被过滤的资源包会标记为"已下载"状态。

#### 7.1 添加过滤器

1. 在"过滤器设置"区域点击"+ 添加过滤器"按钮
2. 在弹出窗口中选择需要的过滤器类型
3. 如果过滤器有参数，填写参数值（如最大文件大小）
4. 点击"添加"完成配置

#### 7.2 可用过滤器类型

**历史下载过滤器**
- **功能**：跳过历史下载记录中已存在的资源包，支持版本检测
- **判断依据**：检查 `history_indexes/` 目录下所有 `index_*.json` 文件的 `downloaded` 数组和 `downloadedVersions` 字典
- **版本检测**：
  - 如果检测到版本更新（downloadedVersions中记录的版本与当前版本不同），则允许重新下载
  - 对于旧的历史记录（无版本信息），按原逻辑处理（视为已下载）
- **参数**：无
- **使用场景**：多次运行下载器时，自动跳过已下载的资源包，但允许下载已更新的资源包

**大文件过滤器**
- **功能**：跳过大于指定大小的资源包
- **判断依据**：资源包的 `size` 字段（字节）
- **参数**：
  - `maxSizeMB`（整数）：最大文件大小（MB），默认值 500MB
- **使用场景**：避免下载超大文件，节省磁盘空间和下载时间

#### 7.3 管理过滤器

- **查看已添加的过滤器**：过滤器列表会显示名称、参数和描述
- **删除过滤器**：点击过滤器右侧的"×"按钮
- **过滤记录**：被过滤的资源包会记录到 `FilteredPackages.json` 文件

#### 7.4 过滤统计

- 下载完成后会显示"过滤:N"统计数据
- 被过滤的资源包不会被下载，但会标记为"已下载"状态

## 文件说明

### packages.json

存储获取的资源包列表快照：

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

存储当前下载会话的进度（下载完成后会归档到 `history_indexes/` 目录）：

```json
{
  "currentIndex": 5,
  "totalCount": 100,
  "downloaded": [12345, 67890],
  "downloadedVersions": {
    "12345": "1.2.3",
    "67890": "2.0.1"
  },
  "failed": [11111],
  "lastUpdate": "2026-03-04T01:30:00Z",
  "currentDownloadingId": "12345"
}
```

### history_indexes/index_*.json

存储历史下载会话的归档索引，每次下载完成后自动创建：

**文件命名格式**：`index_YYYYMMDD_HHmmss.json`（例如：`index_20240305_143022.json`）

**目录位置**：`{导出目录}/history_indexes/`

**文件格式**：与 `index.json` 相同

**用途**：
- 历史下载过滤器读取所有历史索引文件，避免重复下载
- 支持版本检测：如果资源包有更新（版本号不同），会自动重新下载
- 每个文件代表一次完整的下载会话
- 自动去重合并所有 `downloaded` 数组中的ID和版本信息

### FilteredPackages.json

存储被过滤器过滤的资源包记录：

```json
{
  "filtered": [
    {
      "packageId": 12345,
      "packageName": "Asset Name",
      "version": "1.2.3",
      "filterName": "历史下载过滤器",
      "filterReason": "已在历史下载记录中找到 (ID:12345)",
      "filteredTime": "2026-03-05T10:30:00Z"
    },
    {
      "packageId": 67890,
      "packageName": "Large Asset",
      "version": "2.0.0",
      "filterName": "大文件过滤器",
      "filterReason": "文件大小 (1024.00 MB) 超过限制 (500 MB)",
      "filteredTime": "2026-03-05T10:31:00Z"
    }
  ]
}
```

## 目录结构示例

下载完成后，导出目录结构如下：

```
导出目录/
├── packages.json                      # 资源包快照
├── index.json                         # 当前下载进度（下载中）
├── history_indexes/                   # 历史下载索引目录
│   ├── index_20240301_143022.json    # 第1次下载归档
│   ├── index_20240302_091045.json    # 第2次下载归档
│   └── index_20240305_102314.json    # 第3次下载归档
├── FilteredPackages.json              # 过滤记录
├── AssetName_1.2.3.unitypackage      # 已下载的资源包
├── OtherAsset_2.0.1.unitypackage
└── ...
```

## 技术实现

### 依赖项

- Unity Editor (2019.4或更高版本推荐)
- AssetInventory插件的工具类（AssetUtils, IOUtils, ThreadUtils, AI）
- Newtonsoft.Json

### 核心组件

1. **PackageData.cs** - 数据模型定义
2. **AssetStoreAPI.cs** - Asset Store API封装
3. **DownloadManager.cs** - 下载管理器
4. **PackageBatchDownloader.cs** - 主UI窗口
5. **过滤器系统**：
   - **IPackageFilter.cs** - 过滤器接口定义
   - **FilterManager.cs** - 过滤器管理器
   - **FilterRegistry.cs** - 过滤器注册表
   - **DownloadedFilter.cs** - 历史下载过滤器实现
   - **SizeFilter.cs** - 大文件过滤器实现
   - **FilterSelectionWindow.cs** - 过滤器选择窗口

### 下载流程

1. 通过Asset Store API获取购买列表
2. 对每个资源包：
   - 获取详细信息（版本、大小）
   - **应用过滤器检查**：遍历所有激活的过滤器，如果匹配则跳过下载并标记为"已下载"
   - 获取下载信息（URL、Key）
   - 调用Unity内部下载API（通过反射）
   - 等待文件出现在Unity缓存目录
   - 复制到导出目录
3. 下载完成后，将 `index.json` 归档到 `history_indexes/index_YYYYMMDD_HHmmss.json`

### 过滤器工作原理

1. **注册阶段**：在 `FilterRegistry` 中注册所有可用的过滤器类型
2. **初始化阶段**：用户通过UI添加过滤器，`FilterManager` 管理激活的过滤器列表
3. **过滤阶段**：在获取资源包详情后、开始下载前，`FilterManager.ShouldFilter()` 依次调用每个过滤器
4. **记录阶段**：被过滤的资源包记录到 `FilteredPackages.json`，并标记为"已下载"状态
5. **统计阶段**：下载完成后显示过滤数量统计

### 扩展自定义过滤器

要添加新的过滤器类型，需要：

1. **实现 `IPackageFilter` 接口**：
   ```csharp
   public class MyFilter : IPackageFilter
   {
       public string GetName() => "我的过滤器";
       public string GetDescription() => "过滤器描述";
       public bool ShouldFilter(PackageInfo package) { /* 过滤逻辑 */ }
       public Dictionary<string, FilterParamType> GetParamDefinitions() { /* 参数定义 */ }
       // ... 其他接口方法
   }
   ```

2. **在 `FilterRegistry` 中注册**：
   ```csharp
   FilterRegistry.RegisterFilter("MyFilter", exportPath => new MyFilter());
   ```

## 注意事项

1. **登录要求**：必须在Unity中登录Asset Store账号
2. **网络要求**：需要稳定的网络连接
3. **磁盘空间**：确保有足够的磁盘空间（Unity缓存 + 导出目录）
4. **下载超时**：单个资源包下载超时时间为10分钟
5. **并发限制**：当前版本不支持并发下载，按顺序逐个下载
6. **过滤器限制**：
   - 历史下载过滤器需要 `history_indexes/` 目录存在且包含历史索引文件
   - 首次运行时没有历史记录，历史下载过滤器不会生效
   - 过滤器按添加顺序依次检查，第一个匹配的过滤器决定是否跳过

## 故障排除

### 问题：提示"未登录Asset Store"

**解决方案**：在Unity编辑器中打开 Window → Asset Store，登录你的Unity账号

### 问题：下载一直卡在"等待下载"

**解决方案**：
1. 检查网络连接
2. 尝试暂停后重新开始
3. 检查Unity Console是否有错误日志

### 问题：文件复制失败

**解决方案**：
1. 检查导出目录是否有写入权限
2. 检查磁盘空间是否充足
3. 确保文件名不包含非法字符

### 问题：过滤器不生效

**解决方案**：
1. **历史下载过滤器**：检查 `{导出目录}/history_indexes/` 目录是否存在且包含 `index_*.json` 文件
2. **大文件过滤器**：确认参数设置正确，检查 `FilteredPackages.json` 查看过滤记录
3. 查看Unity Console日志，搜索 `[DownloadedFilter]` 或 `[FilterManager]` 标签

### 问题：history_indexes目录未自动创建

**解决方案**：
- `history_indexes/` 目录仅在下载完成后才会创建
- 首次运行时需要完整下载一次后才会生成历史记录
- 如果下载被取消（而非暂停），历史记录不会保存

## 开发者信息

### 构建

项目通过Unity编辑器自动编译，无需手动构建步骤。

### 测试

使用Unity Test Runner进行测试：Window → General → Test Runner

### 代码风格

详见项目根目录的 `AGENTS.md` 文件。

## 许可

本项目基于AssetInventory插件的Asset Store功能提取而来，遵循相应的许可协议。
