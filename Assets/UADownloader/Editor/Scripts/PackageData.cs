using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace UADownloader
{
    /// <summary>
    /// 资源包基本信息（用于packages.json）
    /// </summary>
    [Serializable]
    public class PackageInfo
    {
        public int id;
        public string name;
        public string publisher;
        public string category;
        public string version;
        public long size;
        public string fetchTime;
        
        public string description;
        public string publishNotes;
        public float ratingAverage;
        public int ratingCount;
        public int reviewCount;
        public string[] keyWords;
        public string[] supportedUnityVersions;
        public string[] requirements;
        public string mainImageIcon;
        public string state;
        public string slug;
        public int revision;
        public string publishedDate;
        public ImageInfo[] images;
        
        // Asset Store链接与额外信息
        public string storeUrl;
        public string originPrice;
        public string publisherId;
        public string[] supportLinks;
        public string elevatorPitch;
        public string keyFeatures;
        public string aiDescription;
    }
    
    /// <summary>
    /// 资源图片信息
    /// </summary>
    [Serializable]
    public class ImageInfo
    {
        public string imageUrl;
        public string webpUrl;
        public string thumbnailUrl;
        public string type;
        public int width;
        public int height;
    }

    /// <summary>
    /// 资源包列表容器
    /// </summary>
    [Serializable]
    public class PackageList
    {
        public List<PackageInfo> packages = new List<PackageInfo>();
    }

    /// <summary>
    /// 下载索引（用于index.json）
    /// </summary>
    [Serializable]
    public class DownloadIndex
    {
        public int currentIndex;
        public int totalCount;
        public List<int> downloaded = new List<int>();
        public Dictionary<int, string> downloadedVersions = new Dictionary<int, string>();  // 记录已下载资源包的版本号
        public List<int> failed = new List<int>();
        public string lastUpdate;
        public string currentDownloadingId;  // 当前正在下载的包ID
    }

    /// <summary>
    /// Asset Store API购买记录
    /// </summary>
    [Serializable]
    public class AssetPurchases
    {
        public int total;
        public List<AssetPurchase> results;
    }

    [Serializable]
    public class AssetPurchase
    {
        public string packageId;
        public string displayName;
        public string category;
        public string publisher;
        public string version;
    }

    /// <summary>
    /// 下载信息
    /// </summary>
    [Serializable]
    public class DownloadInfoResult
    {
        public DownloadInfoData result;
        
        [Serializable]
        public class DownloadInfoData
        {
            public DownloadDetails download;
        }
    }

    [Serializable]
    public class DownloadDetails
    {
        public string url;
        public string key;
        public string filename_safe_publisher_name;
        public string filename_safe_category_name;
        public string filename_safe_package_name;
        public string size;
    }

    [Serializable]
    public class DownloadState
    {
        [JsonProperty("in_progress")]
        public bool inProgress;
        public DownloadStateDetails download;
        public long downloadedBytes;
        public long totalBytes;
        public string error;
    }

    [Serializable]
    public class DownloadStateDetails
    {
        public string url;
        public string key;
    }
}
