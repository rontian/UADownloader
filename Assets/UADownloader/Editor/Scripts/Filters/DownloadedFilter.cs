using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace UADownloader.Filters
{
    /// <summary>
    /// 已下载过滤器 - 根据历史下载记录过滤已下载的资源包
    /// </summary>
    public class DownloadedFilter : IPackageFilter
    {
        private readonly string _exportPath;
        private HashSet<int> _historicalDownloadedIds;
        private Dictionary<int, string> _historicalDownloadedVersions;
        
        public DownloadedFilter(string exportPath)
        {
            _exportPath = exportPath;
            _historicalDownloadedVersions = new Dictionary<int, string>();
            LoadHistoricalDownloads();
        }
        
        public string GetName()
        {
            return "历史下载过滤器";
        }
        
        public string GetDescription()
        {
            return "跳过历史下载记录中已存在的资源包 (检查history_indexes目录)";
        }
        
        public bool ShouldFilter(UADownloader.PackageInfo package)
        {
            if (_historicalDownloadedIds == null || !_historicalDownloadedIds.Contains(package.id))
            {
                return false;
            }
            
            if (_historicalDownloadedVersions != null && 
                _historicalDownloadedVersions.TryGetValue(package.id, out string historicalVersion))
            {
                if (!string.IsNullOrEmpty(package.version) && 
                    !string.IsNullOrEmpty(historicalVersion))
                {
                    if (package.version != historicalVersion)
                    {
                        Debug.Log($"[DownloadedFilter] 版本更新检测: {package.name} ({historicalVersion} → {package.version})");
                        return false;
                    }
                }
            }
            
            return true;
        }
        
        public void CacheFiltered(UADownloader.PackageInfo package)
        {
            // 不需要额外缓存，已在下载完成时自动记录到index.json
        }
        
        public Dictionary<string, FilterParamType> GetParamDefinitions()
        {
            return new Dictionary<string, FilterParamType>();
        }
        
        public void SetParams(Dictionary<string, object> paramValues)
        {
            // 无参数
        }
        
        public Dictionary<string, object> GetParams()
        {
            return new Dictionary<string, object>();
        }
        
        public string GetFilterReason(UADownloader.PackageInfo package)
        {
            if (_historicalDownloadedVersions != null && 
                _historicalDownloadedVersions.TryGetValue(package.id, out string historicalVersion))
            {
                return $"已在历史下载记录中找到 (ID:{package.id}, 版本:{historicalVersion})";
            }
            
            return $"已在历史下载记录中找到 (ID:{package.id})";
        }
        
        /// <summary>
        /// 从history_indexes目录加载所有历史下载记录
        /// </summary>
        private void LoadHistoricalDownloads()
        {
            _historicalDownloadedIds = new HashSet<int>();
            
            if (string.IsNullOrEmpty(_exportPath))
            {
                Debug.LogWarning("[DownloadedFilter] 导出路径为空，无法加载历史记录");
                return;
            }
            
            string historyDir = Path.Combine(_exportPath, "history_indexes");
            if (!Directory.Exists(historyDir))
            {
                Debug.Log($"[DownloadedFilter] 历史记录目录不存在: {historyDir}");
                return;
            }
            
            string[] indexFiles = Directory.GetFiles(historyDir, "index_*.json");
            if (indexFiles.Length == 0)
            {
                Debug.Log("[DownloadedFilter] 未找到历史索引文件");
                return;
            }
            
            Debug.Log($"[DownloadedFilter] 找到 {indexFiles.Length} 个历史索引文件");
            
            foreach (var file in indexFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var index = JsonConvert.DeserializeObject<UADownloader.DownloadIndex>(json);
                    
                    if (index?.downloaded != null)
                    {
                        int beforeCount = _historicalDownloadedIds.Count;
                        foreach (var id in index.downloaded)
                        {
                            _historicalDownloadedIds.Add(id);
                        }
                        
                        if (index.downloadedVersions != null)
                        {
                            foreach (var kvp in index.downloadedVersions)
                            {
                                _historicalDownloadedVersions[kvp.Key] = kvp.Value;
                            }
                        }
                        
                        int addedCount = _historicalDownloadedIds.Count - beforeCount;
                        Debug.Log($"[DownloadedFilter] {Path.GetFileName(file)}: 添加 {addedCount} 个下载记录 (总:{index.downloaded.Count}, 去重后新增:{addedCount})");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DownloadedFilter] 加载历史索引失败 {Path.GetFileName(file)}: {e.Message}");
                }
            }
            
            Debug.Log($"[DownloadedFilter] 历史下载记录加载完成，共 {_historicalDownloadedIds.Count} 个已下载资源包");
        }
    }
}
