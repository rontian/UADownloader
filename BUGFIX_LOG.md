# UA Downloader - Bug 修复日志

## Bug #1: JSON 反序列化错误 - id 字段类型不匹配

### 报告时间
2026-03-04

### 错误信息
```
Error parsing API data from https://packages-v2.unity.com/-/api/product/171146: 
Could not convert string to integer: 277216674336. 
Path 'id', line 1, position 126.
```

### 问题分析

**根本原因**：
- Asset Store API 返回的 `AssetDetails.id` 字段是字符串类型
- 某些资源的 `id` 值超出 `int` 范围（如 `277216674336`）
- 我们的 `AssetDetails` 类中定义的是 `int id`，导致 JSON 反序列化失败

**API 响应示例**：
```json
{
  "id": "277216674336",
  "packageId": "171146",
  "slug": "100-special-skills-effects-pack",
  ...
}
```

### 修复方案

**修改文件**：`Assets/UADownloader/Editor/Scripts/PackageData.cs`

**修改内容**：
```csharp
// 修改前
public class AssetDetails
{
    public int id;  // ❌ 错误：无法容纳大数值
    public string version;
    public PackageVersion packageVersion;
}

// 修改后
public class AssetDetails
{
    public string id;  // ✅ 正确：使用 string 类型
    public string version;
    public PackageVersion packageVersion;
}
```

### 影响范围

**受影响的代码**：
- `AssetStoreAPI.FetchAssetDetailsAsync()` - 反序列化 `AssetDetails`

**不受影响的代码**：
- 其他地方使用的 `package.id` 仍然是 `int` 类型（正确）
- 我们实际上不使用 `AssetDetails.id` 字段，只使用 `packageVersion`

### 验证步骤

1. 重新编译项目（Unity 自动重新编译）
2. 尝试下载之前失败的资源包（ID: 171146）
3. 确认不再出现 JSON 解析错误
4. 验证版本和大小信息正确获取

### 后续预防

**教训**：
- Asset Store API 返回的数据类型不一致（有些是字符串，有些是数字）
- 大数值应使用 `long` 或 `string` 类型
- JSON 模型应该基于实际 API 响应定义，而不是假设

**改进建议**：
1. 添加 API 响应日志（调试模式）
2. 使用更健壮的 JSON 解析（容错处理）
3. 添加单元测试验证 JSON 反序列化

---

## 总结

- **Bug 类型**：JSON 反序列化类型不匹配
- **严重程度**：高（导致下载功能完全失败）
- **修复难度**：低（单字段类型修改）
- **修复时间**：5 分钟
- **状态**：✅ 已修复，等待验证

---

**下一次遇到类似问题的排查步骤**：
1. 查看完整的 API 响应 JSON
2. 检查数据模型字段类型是否匹配
3. 注意大数值（超出 `int` 范围）
4. 注意字符串和数字的区别（`"123"` vs `123`）
