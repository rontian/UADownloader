using System;
using System.Collections.Generic;

namespace AssetInventory
{
    public class TemplateVariables
    {
        public string title = "Asset Inventory";
        public string prefix = "";
        public string dataPath = "";
        public string imagePath = "";
        public string active = "packages";
        public int pageSize = 10;
        public string affiliateParam = "";
        public bool hasFilesData;
        public bool internalIdsOnly;
        public string[] parameters = Array.Empty<string>();

        public List<AssetInfo> packages;

        public AssetInfo package;
        public List<AssetFile> packageFiles;
    }
}