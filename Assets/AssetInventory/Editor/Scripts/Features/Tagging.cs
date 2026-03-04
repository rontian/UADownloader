using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AssetInventory
{
    public static class Tagging
    {
        public static event Action OnTagsChanged;

        internal static IEnumerable<TagInfo> Tags
        {
            get
            {
                if (_tags == null) LoadAssignments();
                return _tags;
            }
        }
        private static List<TagInfo> _tags;

        private static Dictionary<int, List<TagInfo>> _packageTagMap;

        internal static int TagHash { get; private set; }

        public static bool AddAssignment(int targetId, string tag, TagAssignment.Target target, bool fromAssetStore = false)
        {
            Tag existingT = AddTag(tag, fromAssetStore);
            if (existingT == null) return false;

            TagAssignment existingA = DBAdapter.DB.Find<TagAssignment>(t => t.TagId == existingT.Id && t.TargetId == targetId && t.TagTarget == target);
            if (existingA != null) return false; // already added

            TagAssignment newAssignment = new TagAssignment(existingT.Id, target, targetId);
            DBAdapter.DB.Insert(newAssignment);

            return true;
        }

        public static bool AddAssignment(AssetInfo info, string tag, TagAssignment.Target target, bool byUser = false)
        {
            if (!AddAssignment(target == TagAssignment.Target.Asset ? info.Id : info.AssetId, tag, target)) return false;

            LoadAssignments(info);
            if (byUser && target == TagAssignment.Target.Asset && info.AssetSource == Asset.Source.AssetManager) AddRemoteTag(info, tag);

            return true;
        }

        public static void AddAssignments(List<AssetInfo> infos, string tag, TagAssignment.Target target, bool byUser = false)
        {
            if (infos.Count == 1 || infos.Any(info => info.AssetSource == Asset.Source.AssetManager))
            {
                // if at least one asset is from AM, we need to sync the tag changes
                infos.ForEach(info => AddAssignment(info, tag, target, byUser));
                return;
            }

            // optimized for bulk assignment without AM sync
            infos.ForEach(info =>
            {
                TagInfo tagInfo = (target == TagAssignment.Target.Asset ? info.AssetTags : info.PackageTags)?.Find(t => t.Name == tag);
                if (tagInfo != null) return;

                if (!AddAssignment(target == TagAssignment.Target.Asset ? info.Id : info.AssetId, tag, target)) return;
                info.SetTagsDirty();
            });
            LoadAssignments();
        }

        public static void RemoveAssignment(AssetInfo info, TagInfo tagInfo, bool autoReload = true, bool byUser = false)
        {
            DBAdapter.DB.Delete<TagAssignment>(tagInfo.Id);

            if (autoReload) LoadAssignments(info);
            if (byUser && tagInfo.TagTarget == TagAssignment.Target.Asset && info.AssetSource == Asset.Source.AssetManager) RemoveRemoteTag(info, tagInfo.Name);
        }

        public static void RemoveAssetAssignments(List<AssetInfo> infos, string name, bool byUser)
        {
            if (infos == null) return;
            infos.ForEach(info =>
            {
                TagInfo tagInfo = info.AssetTags?.Find(t => t.Name == name);
                if (tagInfo == null) return;
                RemoveAssignment(info, tagInfo, false, byUser);
                info.AssetTags.RemoveAll(t => t.Name == name);
                info.SetTagsDirty();
            });
            LoadAssignments();
        }

        public static void RemovePackageAssignments(List<AssetInfo> infos, string name, bool byUser)
        {
            if (infos == null) return;
            infos.ForEach(info =>
            {
                TagInfo tagInfo = info.PackageTags?.Find(t => t.Name == name);
                if (tagInfo == null) return;
                RemoveAssignment(info, tagInfo, false, byUser);
                info.PackageTags.RemoveAll(t => t.Name == name);
                info.SetTagsDirty();
            });
            LoadAssignments();
        }

        private static async void AddRemoteTag(AssetInfo info, string tagName)
        {
#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
            // sync online with AM
            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            await cam.AddTags(info.ToAsset(), info, new List<string> {tagName});
#else
            Debug.LogWarning("Tag changes will not be synced back to Unity Cloud since this project does not have the Cloud Asset dependencies installed (see Settings).");
            await Task.Yield();
#endif
        }

        private static async void RemoveRemoteTag(AssetInfo info, string tagName)
        {
#if USE_ASSET_MANAGER && USE_CLOUD_IDENTITY
            // sync online with AM
            CloudAssetManagement cam = await AI.GetCloudAssetManagement();
            await cam.RemoveTags(info.ToAsset(), info, new List<string> {tagName});
#else
            Debug.LogWarning("Tag changes will not be synced back to Unity Cloud since this project does not have the Cloud Asset dependencies installed (see Settings).");
            await Task.Yield();
#endif
        }

        internal static void LoadAssignments(AssetInfo info = null, bool triggerEvents = true)
        {
            string dataQuery = "SELECT *, TagAssignment.Id as Id from TagAssignment inner join Tag on Tag.Id = TagAssignment.TagId order by TagTarget, TargetId";
            _tags = DBAdapter.DB.Query<TagInfo>($"{dataQuery}").ToList();
            TagHash = Random.Range(0, int.MaxValue);

            // Build fast lookup dictionary for package tags
            _packageTagMap = _tags
                .Where(t => t.TagTarget == TagAssignment.Target.Package)
                .GroupBy(t => t.TargetId)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name).ToList());

            info?.SetTagsDirty();
            if (triggerEvents) OnTagsChanged?.Invoke();
        }

        public static List<TagInfo> GetAssetTags(int assetFileId)
        {
            return Tags?.Where(t => t.TagTarget == TagAssignment.Target.Asset && t.TargetId == assetFileId)
                .OrderBy(t => t.Name).ToList();
        }

        public static List<TagInfo> GetPackageTags(int assetId)
        {
            if (_packageTagMap == null) LoadAssignments();
            return _packageTagMap.TryGetValue(assetId, out List<TagInfo> tags) ? tags : new List<TagInfo>();
        }

        public static void SaveTag(Tag tag)
        {
            DBAdapter.DB.Update(tag);
            LoadAssignments();
        }

        public static Tag AddTag(string name, bool fromAssetStore = false)
        {
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;

            Tag tag = DBAdapter.DB.Find<Tag>(t => t.Name.ToLower() == name.ToLower());
            if (tag == null)
            {
                tag = new Tag(name);
                tag.FromAssetStore = fromAssetStore;
                DBAdapter.DB.Insert(tag);

                OnTagsChanged?.Invoke();
            }
            else if (!tag.FromAssetStore && fromAssetStore)
            {
                tag.FromAssetStore = true;
                DBAdapter.DB.Update(tag); // don't trigger changed event in such cases, this is just for bookkeeping
            }

            return tag;
        }

        public static void RenameTag(Tag tag, string newName)
        {
            newName = newName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            tag.Name = newName;
            DBAdapter.DB.Update(tag);
            LoadAssignments();
        }

        public static void DeleteTag(Tag tag)
        {
            DBAdapter.DB.Execute("DELETE from TagAssignment where TagId=?", tag.Id);
            DBAdapter.DB.Delete<Tag>(tag.Id);
            LoadAssignments();
        }

        public static List<Tag> LoadTags()
        {
            return DBAdapter.DB.Table<Tag>().ToList().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}