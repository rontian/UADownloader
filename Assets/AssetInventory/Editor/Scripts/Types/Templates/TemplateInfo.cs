using System;
using System.IO;

namespace AssetInventory
{
    public class TemplateInfo
    {
        public string name;
        public string description;
        public int version = 1;
        public DateTime date;
        public bool readOnly;
        public bool isSample;
        public bool fixedTargetFolder;
        public string entryPath;
        public bool needsDataPath;
        public bool needsImagePath;
        public string[] packageFields;
        public string[] fileFields;

        public string inheritFrom;
        public string[] moveFiles;
        public string[] deleteFiles;
        public string[] parameters;

        // runtime
        [field: NonSerialized] public string path;
        [field: NonSerialized] public bool hasDescriptor;
        [field: NonSerialized] public bool hasFilesData;

        public string GetNameFromFile()
        {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
        }

        public string GetNameFromFile(string filePath)
        {
            return Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(filePath));
        }

        public string GetDescriptorPath()
        {
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)) + ".json");
        }
    }
}