using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using AssetInventory;
using UnityEngine.Networking;
using AssetInventory;

namespace UADownloader
{
    public static class AssetStoreAPI
    {
        private const int PAGE_SIZE = 100;
        private const string URL_PURCHASES = "https://packages-v2.unity.com/-/api/purchases";
        private const string URL_ASSET_DETAILS = "https://packages-v2.unity.com/-/api/product";
        private const string URL_ASSET_DOWNLOAD = "https://packages-v2.unity.com/-/api/legacy-package-download-info";

        public static string GetToken()
        {
            string token = CloudProjectSettings.accessToken;
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogError("未登录Asset Store，请先在Unity中登录");
            }
            return token;
        }

        public static async Task<List<PackageInfo>> FetchPurchasesAsync()
        {
            string token = GetToken();
            if (string.IsNullOrEmpty(token)) return null;

            var packages = new List<PackageInfo>();
            int offset = 0;

            string url = $"{URL_PURCHASES}?offset={offset}&limit={PAGE_SIZE}";
            var result = await AssetUtils.FetchAPIData<AssetPurchases>(url, "GET", null, token);

            if (result?.results != null)
            {
                foreach (var purchase in result.results)
                {
                    packages.Add(new PackageInfo
                    {
                        id = int.TryParse(purchase.packageId, out int id) ? id : 0,
                        name = purchase.displayName,
                        publisher = purchase.publisher,
                        category = purchase.category,
                        version = purchase.version,
                        size = 0,
                        fetchTime = DateTime.Now.ToString("o")
                    });
                }

                if (result.total > PAGE_SIZE)
                {
                    var tasks = new List<Task<AssetPurchases>>();
                    for (offset = PAGE_SIZE; offset < result.total; offset += PAGE_SIZE)
                    {
                        string pageUrl = $"{URL_PURCHASES}?offset={offset}&limit={PAGE_SIZE}";
                        tasks.Add(AssetUtils.FetchAPIData<AssetPurchases>(pageUrl, "GET", null, token));
                    }

                    var results = await Task.WhenAll(tasks);
                    foreach (var pageResult in results)
                    {
                        if (pageResult?.results != null)
                        {
                            foreach (var purchase in pageResult.results)
                            {
                                packages.Add(new PackageInfo
                                {
                                    id = int.TryParse(purchase.packageId, out int id) ? id : 0,
                                    name = purchase.displayName,
                                    publisher = purchase.publisher,
                                    category = purchase.category,
                                    version = purchase.version,
                                    size = 0,
                                    fetchTime = DateTime.Now.ToString("o")
                                });
                            }
                        }
                    }
                }
            }

            url = $"{URL_PURCHASES}?status=hidden&offset=0&limit=1000";
            var hiddenResult = await AssetUtils.FetchAPIData<AssetPurchases>(url, "GET", null, token);
            if (hiddenResult?.results != null)
            {
                var existingIds = new HashSet<int>(packages.Select(p => p.id));
                foreach (var purchase in hiddenResult.results)
                {
                    int id = int.TryParse(purchase.packageId, out int pid) ? pid : 0;
                    if (!existingIds.Contains(id))
                    {
                        packages.Add(new PackageInfo
                        {
                            id = id,
                            name = purchase.displayName,
                            publisher = purchase.publisher,
                            category = purchase.category,
                            version = purchase.version,
                            size = 0,
                            fetchTime = DateTime.Now.ToString("o")
                        });
                    }
                }
            }

            return packages;
        }

        public static async Task<bool> FetchAssetDetailsAsync(PackageInfo package)
        {
            string token = GetToken();
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[AssetStoreAPI] 未获取到访问令牌");
                return false;
            }

            try
            {
                string url = $"{URL_ASSET_DETAILS}/{package.id}";
                var details = await AssetUtils.FetchAPIData<AssetDetails>(url, "GET", null, token);

                if (details?.version != null)
                {
                    package.version = details.version.name;
                    package.publishedDate = details.version.publishedDate?.ToString("yyyy-MM-dd");
                    
                    package.description = details.description;
                    package.publishNotes = details.publishNotes;
                    package.keyWords = details.keyWords;
                    package.supportedUnityVersions = details.supportedUnityVersions;
                    package.requirements = details.requirements;
                    package.state = details.state;
                    package.slug = details.slug;
                    package.revision = details.revision;
                    
                    if (details.mainImage != null)
                    {
                        package.mainImageIcon = details.mainImage.icon;
                    }
                    
                    if (details.productReview != null)
                    {
                        float.TryParse(details.productReview.ratingAverage, out package.ratingAverage);
                        int.TryParse(details.productReview.ratingCount, out package.ratingCount);
                        int.TryParse(details.productReview.reviewCount, out package.reviewCount);
                    }
                    
                    if (details.uploads != null && details.uploads.Count > 0)
                    {
                        var firstUpload = details.uploads.Values.FirstOrDefault();
                        if (firstUpload != null)
                        {
                            long.TryParse(firstUpload.downloadSize, out package.size);
                        }
                        else
                        {
                            Debug.LogWarning($"[AssetStoreAPI] uploads.Values.FirstOrDefault() 返回null");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[AssetStoreAPI] uploads字段为null或为空 - {package.name}");
                    }
                    
                    if (details.images != null && details.images.Length > 0)
                    {
                        package.images = details.images.Select(img => new ImageInfo
                        {
                            imageUrl = img.imageUrl,
                            webpUrl = img.webpUrl,
                            thumbnailUrl = img.thumbnailUrl,
                            type = img.type,
                            width = img.width,
                            height = img.height
                        }).ToArray();
                        
                        Debug.Log($"[AssetStoreAPI] 获取到 {package.images.Length} 张图片 - {package.name}");
                    }
                    
                    return true;
                }
                else
                {
                    Debug.LogWarning($"[AssetStoreAPI] 详情中没有版本信息 - {package.name}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AssetStoreAPI] 获取详情失败 - {package.name}: {e.Message}");
            }

            return false;
        }

        public static async Task<DownloadDetails> FetchDownloadInfoAsync(int id)
        {
            string token = GetToken();
            if (string.IsNullOrEmpty(token))
            {
                Debug.LogWarning("[AssetStoreAPI] 未获取到访问令牌");
                return null;
            }

            try
            {
                string url = $"{URL_ASSET_DOWNLOAD}/{id}";
                var result = await AssetUtils.FetchAPIData<DownloadInfoResult>(url, "GET", null, token);

                if (result?.result?.download != null)
                {
                    var download = result.result.download;
                    
                    // 统一对三个safe字段做最小处理（仅删除点号，与AssetInventory一致）
                    if (!string.IsNullOrEmpty(download.filename_safe_publisher_name))
                    {
                        download.filename_safe_publisher_name = download.filename_safe_publisher_name.Replace(".", string.Empty);
                    }
                    if (!string.IsNullOrEmpty(download.filename_safe_category_name))
                    {
                        download.filename_safe_category_name = download.filename_safe_category_name.Replace(".", string.Empty);
                    }
                    if (!string.IsNullOrEmpty(download.filename_safe_package_name))
                    {
                        download.filename_safe_package_name = download.filename_safe_package_name.Replace(".", string.Empty);
                    }
                    
                    return download;
                }
                else
                {
                    Debug.LogWarning($"[AssetStoreAPI] 下载信息为空 - Asset ID: {id}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AssetStoreAPI] 获取下载信息失败 - Asset {id}: {e.Message}");
            }

            return null;
        }
    }
}
