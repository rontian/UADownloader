# 过滤器系统测试指南

## 测试前提条件

1. Unity编辑器已打开UADownloader项目
2. 已登录Asset Store账号
3. 已设置导出目录

## 测试项 1：UI界面完整性

### 操作步骤
1. 打开窗口：`Tools → UA Downloader → Package Batch Downloader`
2. 检查界面结构

### 预期结果
- ✅ "导出设置"区域显示正常
- ✅ **"过滤器设置"区域显示**（新增）
  - 显示"+ 添加过滤器"按钮
  - 显示"暂无激活的过滤器"提示
- ✅ "概览"区域显示5个统计项：总数、已下载、失败、**已过滤**、待下载
- ✅ "操作按钮"区域显示正常

---

## 测试项 2：添加大文件过滤器

### 操作步骤
1. 点击"+ 添加过滤器"按钮
2. 弹窗显示2个可用过滤器：
   - ✓ 已下载过滤器
   - ○ 大文件过滤器
3. 选择"大文件过滤器"（单选）
4. 输入参数：`maxSizeMB = 100`
5. 点击"确定"

### 预期结果
- ✅ 弹窗关闭
- ✅ 过滤器列表显示：
  ```
  ✓  大文件过滤器  (maxSizeMB=100)  跳过大于指定大小的资源包  [×]
  ```
- ✅ 参数正确显示（100MB）

---

## 测试项 3：添加已下载过滤器

### 前提条件
在导出目录创建测试用的 `downloaded.json`：
```json
{
  "packages": [
    {
      "id": 12345,
      "name": "Test Asset",
      "version": "1.2.3"
    }
  ]
}
```

### 操作步骤
1. 点击"+ 添加过滤器"按钮
2. 选择"已下载过滤器"
3. 点击"确定"（无参数）

### 预期结果
- ✅ 过滤器列表显示：
  ```
  ✓  大文件过滤器  (maxSizeMB=100)  跳过大于指定大小的资源包  [×]
  ✓  已下载过滤器  跳过已存在于downloaded.json的相同版本资源包  [×]
  ```

---

## 测试项 4：删除过滤器

### 操作步骤
1. 点击"大文件过滤器"行末尾的"×"按钮

### 预期结果
- ✅ "大文件过滤器"从列表中消失
- ✅ 只剩下"已下载过滤器"

---

## 测试项 5：过滤逻辑验证（大文件过滤）

### 操作步骤
1. 删除所有过滤器
2. 添加"大文件过滤器"，设置 `maxSizeMB = 50`
3. 点击"获取资源列表快照"
4. 点击"开始批量下载"
5. 观察Console日志

### 预期结果
- ✅ 日志显示：`[BatchDownloader] 已过滤: XXX - 过滤器:大文件过滤器, 理由:文件过大 (800MB > 50MB)`
- ✅ 被过滤的资源包不会下载
- ✅ 被过滤的资源包标记为"已下载"（`downloaded.Add(id)`）
- ✅ 概览显示"已过滤"数量递增
- ✅ 资源包列表中被过滤项显示绿色"✓"图标

---

## 测试项 6：过滤逻辑验证（已下载过滤）

### 前提条件
确保 `downloaded.json` 包含以下内容：
```json
{
  "packages": [
    {
      "id": 12345,
      "name": "Asset Name",
      "version": "1.2.3"
    }
  ]
}
```

### 操作步骤
1. 删除所有过滤器
2. 添加"已下载过滤器"
3. 开始批量下载
4. 当下载到ID=12345的资源包时，观察行为

### 预期结果
- ✅ 如果 `packages.json` 中ID=12345的版本也是1.2.3：
  - 日志显示：`[BatchDownloader] 已过滤: Asset Name - 过滤器:已下载过滤器, 理由:已下载相同版本 (v1.2.3)`
  - 跳过下载
- ✅ 如果版本不同（如1.2.4）：
  - 不过滤，继续下载

---

## 测试项 7：过滤记录持久化

### 操作步骤
1. 使用任意过滤器完成一次下载（至少过滤1个资源包）
2. 检查导出目录中的 `FilteredPackages.json` 文件

### 预期结果
- ✅ 文件存在
- ✅ 内容格式正确：
  ```json
  {
    "records": [
      {
        "packageId": 12345,
        "packageName": "Asset Name",
        "version": "1.2.3",
        "filterName": "大文件过滤器",
        "filterReason": "文件过大 (800MB > 50MB)",
        "filteredTime": "2026-03-05T10:30:00Z"
      }
    ]
  }
  ```

---

## 测试项 8：概览统计准确性

### 操作步骤
1. 添加"大文件过滤器"（maxSizeMB=100）
2. 获取资源列表快照（假设10个资源包）
3. 开始下载

### 预期结果
下载完成后，概览显示：
- ✅ `资源包总数: 10`
- ✅ `已下载: 7`（实际下载）
- ✅ `失败: 0`
- ✅ `已过滤: 3`（被过滤跳过）
- ✅ `待下载: 0`
- ✅ 验证：`7 + 0 + 3 = 10` ✓

---

## 测试项 9：完成消息验证

### 操作步骤
下载完成后，检查状态消息

### 预期结果
- ✅ 状态消息格式：
  ```
  批量下载完成 | 成功:7 | 失败:0 | 跳过:0 | 过滤:3 | 总大小:1.2 GB | 总耗时:5分30秒
  ```
- ✅ "过滤:3"正确显示

---

## 测试项 10：多过滤器组合

### 操作步骤
1. 同时添加"已下载过滤器"和"大文件过滤器"
2. 开始下载

### 预期结果
- ✅ 任意一个过滤器匹配即过滤（OR逻辑）
- ✅ 日志显示第一个匹配的过滤器名称和理由
- ✅ 过滤记录保存正确的filterName

---

## 编译验证

### 操作步骤
```bash
Unity菜单 → Assets → Reimport All
检查Console无编译错误
```

### 预期结果
- ✅ 无编译错误
- ✅ 无编译警告

---

## 代码完整性检查

### 文件列表验证
```bash
cd /Volumes/MY_Spaces/Unity/UADownloader/Assets/UADownloader/Editor/Scripts/Filters
ls -la *.cs
```

预期输出（7个文件）：
```
DownloadedFilter.cs      (97行)
FilteredRecord.cs        (22行)
FilterManager.cs         (131行)
FilterRegistry.cs        (51行)
FilterSelectionWindow.cs (215行)
IPackageFilter.cs        (69行)
SizeFilter.cs            (76行)
```

### 主窗口修改点验证
```bash
grep -n "using UADownloader.Filters" PackageBatchDownloader.cs
grep -n "DrawFilterSection" PackageBatchDownloader.cs
grep -n "ShouldFilter" PackageBatchDownloader.cs
grep -n "已过滤" PackageBatchDownloader.cs
```

预期输出：
```
11:using UADownloader.Filters;
88:            DrawFilterSection();
223:        private void DrawFilterSection()
489:                if (_filterManager != null && _filterManager.ShouldFilter(...)
135:            EditorGUILayout.LabelField($"已过滤: {filteredCount}", ...)
```

---

## 故障排除

### 问题1：弹窗不显示
**可能原因**：_exportPath为空
**解决方案**：先设置导出目录

### 问题2：过滤器不生效
**可能原因**：_filterManager未初始化
**检查**：
```csharp
// PackageBatchDownloader.cs Line 67-71
if (!string.IsNullOrEmpty(_exportPath))
{
    _filterManager = new FilterManager(_exportPath);
}
```

### 问题3：已下载过滤器不工作
**可能原因**：downloaded.json不存在或格式错误
**解决方案**：确保导出目录下有正确格式的downloaded.json

### 问题4：编译错误 - PackageInfo未定义
**可能原因**：命名空间引用错误
**检查**：所有过滤器文件中应使用 `UADownloader.PackageInfo`

---

## 成功标准

所有10个测试项全部通过 ✅

## 已知限制

1. 过滤器是OR逻辑（任意一个匹配即过滤），不支持AND逻辑
2. 被过滤的项直接标记为"已下载"，与真实下载的项无法区分
3. 过滤器无法在下载过程中动态修改（需要取消重新开始）
4. FilteredPackages.json只增不减（不会自动清理历史记录）

---

## 下一步增强建议

1. 添加更多过滤器类型：
   - 类别过滤器（按category过滤）
   - 发布者过滤器（按publisher过滤）
   - 日期过滤器（按publishedDate过滤）
   - 评分过滤器（按ratingAverage过滤）

2. 增强过滤记录管理：
   - 显示过滤历史列表
   - 清空过滤记录按钮
   - 导出过滤报告

3. 优化UI交互：
   - 过滤器拖拽排序（决定匹配优先级）
   - 过滤器启用/禁用开关（不删除，只临时关闭）
   - 预览模式（显示将被过滤的资源包列表）

4. 增强过滤逻辑：
   - 支持AND/OR组合逻辑
   - 支持NOT逻辑（排除过滤）
   - 自定义脚本过滤器（用户编写C#表达式）
