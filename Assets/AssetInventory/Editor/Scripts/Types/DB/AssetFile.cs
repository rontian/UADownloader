using System;
using System.IO;
using SQLite;
using UnityEditor;

namespace AssetInventory
{
    [Serializable]
    public class AssetFile : TreeElement
    {
        public enum PreviewOptions
        {
            None = 0,
            Provided = 1,
            Redo = 2, // implies there is a valid preview already to which can be reverted
            Custom = 3,
            Error = 4,
            NotApplicable = 5,
            RedoMissing = 6, // no valid preview yet
            UseOriginal = 7 // no extra preview, original file is the preview (e.g. image files)
        }

        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        [Indexed] public int AssetId { get; set; }
        [Indexed] public string Guid { get; set; }
        [Collation("NOCASE")] public string Path { get; set; } // index created manually for collation
        [Collation("NOCASE")] public string FileName { get; set; } // index created manually for collation
        public string SourcePath { get; set; }
        public string FileVersion { get; set; }
        public string FileStatus { get; set; }
        [Indexed] public string Type { get; set; }
        [Indexed] public PreviewOptions PreviewState { get; set; }
        public float Hue { get; set; } = -1f;
        public long Size { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public float Length { get; set; }
        public string AICaption { get; set; }

        // runtime
        [Ignore] public string ProjectPath { get; set; }
        [Ignore] public bool InProject => !string.IsNullOrWhiteSpace(ProjectPath) && !ProjectPath.Contains(AI.TEMP_FOLDER) && !ProjectPath.Contains(UnityPreviewGenerator.PREVIEW_FOLDER);

        [Ignore] public string ShortPath => !string.IsNullOrEmpty(Path) && Path.StartsWith("Assets/") ? Path.Substring(7) : Path;

        public void CheckIfInProject()
        {
            // check if file already exists in project, and work-around issue that Unity reports deleted assets still back
            ProjectPath = AssetDatabase.GUIDToAssetPath(Guid);
            if (!string.IsNullOrEmpty(ProjectPath) && (ProjectPath.StartsWith($"Assets/{AI.TEMP_FOLDER}") || !File.Exists(ProjectPath))) ProjectPath = null;
        }

        public bool HasPreview(bool allowScheduled = false)
        {
            return 
                PreviewState == PreviewOptions.UseOriginal ||
                PreviewState == PreviewOptions.Custom || 
                PreviewState == PreviewOptions.Provided || 
                (allowScheduled && PreviewState == PreviewOptions.Redo);
        }

        public bool IsUnityPackage()
        {
            return Type == "unitypackage";
        }

        public bool IsArchive()
        {
            return Type == "zip" || Type == "rar" || Type == "7z";
        }

        public string GetPreviewFolder(string previewFolder)
        {
            return IOUtils.ToLongPath(System.IO.Path.Combine(previewFolder, AssetId.ToString()));
        }

        public string GetPreviewFile(string previewFolder, bool animated = false)
        {
            if (!animated && PreviewState == PreviewOptions.UseOriginal) return SourcePath;

            string aniSign = animated ? "a" : string.Empty;

            // inline for performance
            return IOUtils.ToLongPath(System.IO.Path.Combine(previewFolder, AssetId.ToString(), $"af{aniSign}-{Id}.png"));
        }

        public string GetPath(bool expanded)
        {
            return expanded ? AI.DeRel(Path) : Path;
        }

        internal void SetPath(string path)
        {
            Path = path?.Replace("\\", "/");
        }

        public string GetSourcePath(bool expanded)
        {
            return expanded ? AI.DeRel(SourcePath) : SourcePath;
        }

        internal void SetSourcePath(string sourcePath)
        {
            SourcePath = sourcePath?.Replace("\\", "/");
        }

        public override string ToString()
        {
            return $"Asset File '{Path}' ({EditorUtility.FormatBytes(Size)}, Asset {AssetId})";
        }
    }
}