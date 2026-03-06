using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace UADownloader.Filters
{
    public class FilterManager
    {
        private readonly string _exportPath;
        private readonly List<IPackageFilter> _activeFilters = new List<IPackageFilter>();
        private FilteredData _filteredData = new FilteredData();
        
        private string FilteredDataPath => Path.Combine(_exportPath, "FilteredPackages.json");
        
        public FilterManager(string exportPath)
        {
            _exportPath = exportPath;
            LoadFilteredData();
        }
        
        public void AddFilter(IPackageFilter filter)
        {
            if (!_activeFilters.Contains(filter))
            {
                _activeFilters.Add(filter);
            }
        }
        
        public void RemoveFilter(IPackageFilter filter)
        {
            _activeFilters.Remove(filter);
        }
        
        public void ClearFilters()
        {
            _activeFilters.Clear();
        }
        
        public List<IPackageFilter> GetActiveFilters()
        {
            return new List<IPackageFilter>(_activeFilters);
        }
        
        public List<IPackageFilter> GetFilters()
        {
            return _activeFilters;
        }
        
        public bool ShouldFilter(UADownloader.PackageInfo package, out string filterName, out string filterReason)
        {
            foreach (var filter in _activeFilters)
            {
                if (filter.ShouldFilter(package))
                {
                    filterName = filter.GetName();
                    filterReason = filter.GetFilterReason(package);
                    
                    filter.CacheFiltered(package);
                    
                    var record = new FilteredRecord
                    {
                        packageId = package.id,
                        packageName = package.name,
                        version = package.version,
                        filterName = filterName,
                        filterReason = filterReason,
                        filteredTime = DateTime.Now.ToString("o")
                    };
                    
                    _filteredData.records.Add(record);
                    SaveFilteredData();
                    
                    return true;
                }
            }
            
            filterName = null;
            filterReason = null;
            return false;
        }
        
        public int GetFilteredCount()
        {
            return _filteredData.records.Count;
        }
        
        public List<FilteredRecord> GetFilteredRecords()
        {
            return new List<FilteredRecord>(_filteredData.records);
        }
        
        public void ClearFilteredData()
        {
            _filteredData.records.Clear();
            SaveFilteredData();
        }
        
        private void LoadFilteredData()
        {
            if (string.IsNullOrEmpty(_exportPath)) return;
            
            if (File.Exists(FilteredDataPath))
            {
                try
                {
                    string json = File.ReadAllText(FilteredDataPath);
                    _filteredData = JsonConvert.DeserializeObject<FilteredData>(json) ?? new FilteredData();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FilterManager] 加载过滤记录失败: {e.Message}");
                    _filteredData = new FilteredData();
                }
            }
        }
        
        private void SaveFilteredData()
        {
            if (string.IsNullOrEmpty(_exportPath)) return;
            
            try
            {
                string json = JsonConvert.SerializeObject(_filteredData, Formatting.Indented);
                File.WriteAllText(FilteredDataPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FilterManager] 保存过滤记录失败: {e.Message}");
            }
        }
    }
}
