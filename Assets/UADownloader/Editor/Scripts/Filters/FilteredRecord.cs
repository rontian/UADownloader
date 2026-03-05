using System;
using System.Collections.Generic;

namespace UADownloader.Filters
{
    [Serializable]
    public class FilteredRecord
    {
        public int packageId;
        public string packageName;
        public string version;
        public string filterName;
        public string filterReason;
        public string filteredTime;
    }
    
    [Serializable]
    public class FilteredData
    {
        public List<FilteredRecord> records = new List<FilteredRecord>();
    }
}
