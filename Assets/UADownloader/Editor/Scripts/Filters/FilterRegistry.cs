using System;
using System.Collections.Generic;

namespace UADownloader.Filters
{
    public static class FilterRegistry
    {
        private static readonly Dictionary<string, Func<string, IPackageFilter>> _filterFactories 
            = new Dictionary<string, Func<string, IPackageFilter>>();
        
        static FilterRegistry()
        {
            RegisterFilter("DownloadedFilter", exportPath => new DownloadedFilter(exportPath));
            RegisterFilter("SizeFilter", exportPath => new SizeFilter());
        }
        
        public static void RegisterFilter(string typeName, Func<string, IPackageFilter> factory)
        {
            _filterFactories[typeName] = factory;
        }
        
        public static IPackageFilter CreateFilter(string typeName, string exportPath)
        {
            if (_filterFactories.TryGetValue(typeName, out var factory))
            {
                return factory(exportPath);
            }
            return null;
        }
        
        public static List<string> GetAvailableFilterTypes()
        {
            return new List<string>(_filterFactories.Keys);
        }
        
        public static List<IPackageFilter> GetAllAvailableFilters(string exportPath)
        {
            var filters = new List<IPackageFilter>();
            foreach (var typeName in _filterFactories.Keys)
            {
                var filter = CreateFilter(typeName, exportPath);
                if (filter != null)
                {
                    filters.Add(filter);
                }
            }
            return filters;
        }
    }
}
