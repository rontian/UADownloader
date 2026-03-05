using System.Collections.Generic;

namespace UADownloader.Filters
{
    public class SizeFilter : IPackageFilter
    {
        private const string PARAM_MAX_SIZE_MB = "maxSizeMB";
        private long _maxSizeBytes = 500 * 1024 * 1024;
        
        public string GetName()
        {
            return "大文件过滤器";
        }
        
        public string GetDescription()
        {
            return "跳过大于指定大小的资源包";
        }
        
        public bool ShouldFilter(UADownloader.PackageInfo package)
        {
            if (package.size <= 0)
            {
                return false;
            }
            
            return package.size > _maxSizeBytes;
        }
        
        public void CacheFiltered(UADownloader.PackageInfo package)
        {
        }
        
        public Dictionary<string, FilterParamType> GetParamDefinitions()
        {
            return new Dictionary<string, FilterParamType>
            {
                { PARAM_MAX_SIZE_MB, FilterParamType.Int }
            };
        }
        
        public void SetParams(Dictionary<string, object> paramValues)
        {
            if (paramValues != null && paramValues.ContainsKey(PARAM_MAX_SIZE_MB))
            {
                if (paramValues[PARAM_MAX_SIZE_MB] is int maxSizeMB)
                {
                    _maxSizeBytes = maxSizeMB * 1024L * 1024L;
                }
                else if (paramValues[PARAM_MAX_SIZE_MB] is string strValue)
                {
                    if (int.TryParse(strValue, out int parsedValue))
                    {
                        _maxSizeBytes = parsedValue * 1024L * 1024L;
                    }
                }
            }
        }
        
        public Dictionary<string, object> GetParams()
        {
            return new Dictionary<string, object>
            {
                { PARAM_MAX_SIZE_MB, (int)(_maxSizeBytes / 1024 / 1024) }
            };
        }
        
        public string GetFilterReason(UADownloader.PackageInfo package)
        {
            long maxSizeMB = _maxSizeBytes / 1024 / 1024;
            long packageSizeMB = package.size / 1024 / 1024;
            return $"文件过大 ({packageSizeMB}MB > {maxSizeMB}MB)";
        }
    }
}
