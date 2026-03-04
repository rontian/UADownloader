using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory
{
    public static class Metadata
    {
        public static event Action OnDefinitionsChanged;

        internal static IEnumerable<MetadataInfo> Metadatas
        {
            get
            {
                if (_metas == null) LoadAssignments();
                return _metas;
            }
        }
        private static List<MetadataInfo> _metas;

        internal static int MetadataHash { get; private set; }

        public static List<MetadataDefinition> LoadDefinitions()
        {
            List<MetadataDefinition> defs = DBAdapter.DB.Table<MetadataDefinition>().ToList().OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (defs.Count == 0)
            {
                MetadataDefinition def = new MetadataDefinition("Comments");
                def.Type = MetadataDefinition.DataType.BigText;
                DBAdapter.DB.Insert(def);
                defs.Add(def);
            }
            return defs;
        }

        public static MetadataDefinition AddDefinition(MetadataDefinition def)
        {
            def.Name = def.Name.Trim();
            if (string.IsNullOrWhiteSpace(def.Name)) return null;

            if (def.Id > 0)
            {
                DBAdapter.DB.Update(def);
            }
            else
            {
                DBAdapter.DB.Insert(def);
            }
            OnDefinitionsChanged?.Invoke();

            return def;
        }

        public static void DeleteDefinition(MetadataDefinition def)
        {
            DBAdapter.DB.Execute("DELETE from MetadataAssignment where MetadataId=?", def.Id);
            DBAdapter.DB.Delete<MetadataDefinition>(def.Id);

            OnDefinitionsChanged?.Invoke();
        }

        public static bool AddAssignment(int targetId, int id, MetadataAssignment.Target target, bool fromAssetStore = false)
        {
            MetadataAssignment existingA = DBAdapter.DB.Find<MetadataAssignment>(t => t.MetadataId == id && t.TargetId == targetId && t.MetadataTarget == target);
            if (existingA != null) return false; // already added

            MetadataAssignment newAssignment = new MetadataAssignment(id, target, targetId);
            DBAdapter.DB.Insert(newAssignment);

            return true;
        }

        public static bool AddAssignment(AssetInfo info, int id, MetadataAssignment.Target target, bool byUser = false)
        {
            if (!AddAssignment(target == MetadataAssignment.Target.Asset ? info.Id : info.AssetId, id, target)) return false;

            LoadAssignments(info);

            return true;
        }

        public static void RemoveAssignment(AssetInfo info, MetadataInfo metadataInfo, bool autoReload = true, bool byUser = false)
        {
            DBAdapter.DB.Delete<MetadataAssignment>(metadataInfo.Id);

            if (autoReload) LoadAssignments(info);
        }

        internal static void LoadAssignments(AssetInfo info = null, bool triggerEvents = true)
        {
            string dataQuery = "SELECT *, MetadataAssignment.Id as Id, MetadataDefinition.Id as DefinitionId from MetadataAssignment inner join MetadataDefinition on MetadataDefinition.Id = MetadataAssignment.MetadataId order by MetadataTarget, TargetId, MetadataAssignment.Id";
            _metas = DBAdapter.DB.Query<MetadataInfo>($"{dataQuery}").ToList();

            MetadataHash = UnityEngine.Random.Range(0, int.MaxValue);

            info?.SetMetadataDirty();
            if (triggerEvents) OnDefinitionsChanged?.Invoke();
        }

        public static List<MetadataInfo> GetPackageMetadata(int assetId)
        {
            return Metadatas?.Where(t => t.MetadataTarget == MetadataAssignment.Target.Package && t.TargetId == assetId)
                .OrderBy(t => t.Id).ToList();
        }
    }
}
