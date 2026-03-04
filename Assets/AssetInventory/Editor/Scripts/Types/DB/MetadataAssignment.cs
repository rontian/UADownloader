using System;
using SQLite;

namespace AssetInventory
{
    [Serializable]
    public class MetadataAssignment
    {
        public enum Target
        {
            Package = 0,
            Asset = 1
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int MetadataId { get; set; }
        [Indexed] public Target MetadataTarget { get; set; }
        [Indexed] public int TargetId { get; set; }
        public string StringValue { get; set; }
        public int IntValue { get; set; }
        public float FloatValue { get; set; }
        public bool BoolValue { get; set; }
        public DateTime DateTimeValue { get; set; }

        public MetadataAssignment()
        {
        }

        public MetadataAssignment(int metadataId, Target metadataTarget, int targetId)
        {
            MetadataId = metadataId;
            MetadataTarget = metadataTarget;
            TargetId = targetId;
        }

        public override string ToString()
        {
            return $"Metadata Assignment '{MetadataTarget}' ({MetadataId})";
        }
    }
}
