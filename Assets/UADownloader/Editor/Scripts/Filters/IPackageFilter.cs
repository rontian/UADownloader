using System.Collections.Generic;

namespace UADownloader.Filters
{
    /// <summary>
    /// 资源包过滤器接口
    /// </summary>
    public interface IPackageFilter
    {
        /// <summary>
        /// 获取过滤器名称
        /// </summary>
        string GetName();
        
        /// <summary>
        /// 获取过滤器描述
        /// </summary>
        string GetDescription();
        
        /// <summary>
        /// 判断是否应该过滤此资源包
        /// </summary>
        /// <param name="package">资源包信息（包含详情）</param>
        /// <returns>true=过滤掉（跳过下载），false=不过滤</returns>
        bool ShouldFilter(UADownloader.PackageInfo package);
        
        /// <summary>
        /// 缓存被过滤的资源包
        /// </summary>
        /// <param name="package">被过滤的资源包</param>
        void CacheFiltered(UADownloader.PackageInfo package);
        
        /// <summary>
        /// 获取过滤器参数定义
        /// </summary>
        /// <returns>参数名 -> 参数类型映射</returns>
        Dictionary<string, FilterParamType> GetParamDefinitions();
        
        /// <summary>
        /// 设置过滤器参数值
        /// </summary>
        /// <param name="paramValues">参数名 -> 参数值映射</param>
        void SetParams(Dictionary<string, object> paramValues);
        
        /// <summary>
        /// 获取当前参数值
        /// </summary>
        Dictionary<string, object> GetParams();
        
        /// <summary>
        /// 获取过滤理由
        /// </summary>
        /// <param name="package">资源包</param>
        /// <returns>过滤理由描述</returns>
        string GetFilterReason(UADownloader.PackageInfo package);
    }
    
    /// <summary>
    /// 过滤器参数类型
    /// </summary>
    public enum FilterParamType
    {
        String,
        Int,
        Float,
        Bool
    }
}
