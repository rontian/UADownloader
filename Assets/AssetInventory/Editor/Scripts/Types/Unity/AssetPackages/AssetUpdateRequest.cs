using System;

namespace AssetInventory
{
    [Serializable]
    public sealed class AssetUpdateRequest
    {
        public string local_path;
        public string id;
        public string version;
        public string version_id;
        public string upload_id;

        public override string ToString()
        {
            return $"Asset Update Request ({id}, {version}, {upload_id})";
        }
    }
}