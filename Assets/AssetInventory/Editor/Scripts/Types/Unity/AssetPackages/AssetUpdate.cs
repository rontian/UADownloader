using System;
using System.Collections.Generic;

namespace AssetInventory
{
    [Serializable]
    public sealed class AssetUpdate
    {
        public int can_comment;
        public int can_download;
        public int can_update;
        public Category category;
        public string created_at;
        public string icon;
        public string icon128;
        public string id;
        public int in_user_downloads;
        public int is_complete_project;
        public Category kategory;
        public string last_downloaded_at;
        public string local_path;
        public string local_version_name;
        public string name;
        public string published_at;
        public Publisher publisher;
        public string purchased_at;
        public int recommended_version_compare;
        public string slug;
        public string status;
        public List<string> tags;
        public string type;
        public string updated_at;
        public int user_rating;

        public override string ToString()
        {
            return $"Asset Update ({name}, {id}, {updated_at})";
        }
    }
}