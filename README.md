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

存储下载进度：

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

### 下载流程

1. 通过Asset Store API获取购买列表
2. 对每个资源包：
   - 获取详细信息（版本、大小）
   - 获取下载信息（URL、Key）
   - 调用Unity内部下载API（通过反射）
   - 等待文件出现在Unity缓存目录
   - 复制到导出目录

## 注意事项

1. **登录要求**：必须在Unity中登录Asset Store账号
2. **网络要求**：需要稳定的网络连接
3. **磁盘空间**：确保有足够的磁盘空间（Unity缓存 + 导出目录）
4. **下载超时**：单个资源包下载超时时间为10分钟
5. **并发限制**：当前版本不支持并发下载，按顺序逐个下载

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

## 开发者信息

### 构建

项目通过Unity编辑器自动编译，无需手动构建步骤。

### 测试

使用Unity Test Runner进行测试：Window → General → Test Runner

### 代码风格

详见项目根目录的 `AGENTS.md` 文件。

## 许可

本项目基于AssetInventory插件的Asset Store功能提取而来，遵循相应的许可协议。
