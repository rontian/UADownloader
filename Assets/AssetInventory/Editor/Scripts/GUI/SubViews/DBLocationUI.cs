using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class DBLocationUI : BasicEditorUI
    {
        private Vector2 _scrollPos;
        private bool _inProgress;
        private string _newFolder;
        private long _dbSize = -1;
        private long _backupSize = -1;
        private long _previewSize = -1;
        private long _cacheSize = -1;
        private long _targetSpace = -1;
        private bool _calculating;
        private bool _ignoreCache = true;
        private bool _sameDrive;

        public static DBLocationUI ShowWindow()
        {
            DBLocationUI window = GetWindow<DBLocationUI>("Change Database Location");
            window.minSize = new Vector2(400, 300);

            return window;
        }

        public void Init(string newFolder)
        {
            _newFolder = newFolder;

            CalculateSizes();
        }

        private async void CalculateSizes()
        {
            _calculating = true;

            _dbSize = new FileInfo(DBAdapter.GetDBPath()).Length;
            _targetSpace = IOUtils.GetFreeSpace(_newFolder);
            _sameDrive = IOUtils.IsSameDrive(DBAdapter.GetDBPath(), _newFolder);
            if (string.IsNullOrEmpty(AI.Config.backupFolder)) _backupSize = await AI.GetBackupFolderSize();
            if (string.IsNullOrEmpty(AI.Config.cacheFolder)) _cacheSize = await AI.GetCacheFolderSize();
            if (string.IsNullOrEmpty(AI.Config.previewFolder)) _previewSize = await AI.GetPreviewFolderSize();

            _calculating = false;
        }

        public override void OnGUI()
        {
            if (string.IsNullOrWhiteSpace(_newFolder))
            {
                EditorGUILayout.HelpBox("Select a target folder first before starting this wizard.", MessageType.Info);
                return;
            }

            int labelWidth = 115;
            int sizeLabelWidth = 90;
            long spaceRequired = _dbSize;
            string curFolder = DBAdapter.GetDBPath().Replace("\\", "/");

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Current Database", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
            EditorGUILayout.LabelField(curFolder, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(EditorUtility.FormatBytes(_dbSize), UIStyles.miniLabelRight, GUILayout.Width(sizeLabelWidth));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Target Folder", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
            EditorGUILayout.LabelField(_newFolder, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("free: " + EditorUtility.FormatBytes(_targetSpace), UIStyles.miniLabelRight, GUILayout.Width(sizeLabelWidth));
            GUILayout.EndHorizontal();

            EditorGUILayout.Space();

#if UNITY_EDITOR_WIN
            if (!IOUtils.IsSameDrive(curFolder, _newFolder))
            {
                EditorGUILayout.HelpBox("Moving the database to a different drive using this wizard is not supported at this point in time. Please close Unity and move the directory or database manually. Then set the new location here and switch to switch to it.", MessageType.Error);
                return;
            }
#endif

            if (string.IsNullOrEmpty(AI.Config.previewFolder) || string.IsNullOrEmpty(AI.Config.backupFolder) || string.IsNullOrEmpty(AI.Config.cacheFolder))
            {
                EditorGUILayout.HelpBox("By default the folders below are stored where the database is. Moving the database will also move these folders. If you do not want this, select to have them remain where they are.", MessageType.Info);

                if (string.IsNullOrEmpty(AI.Config.previewFolder))
                {
                    Color oldCol = GUI.backgroundColor;
                    GUI.backgroundColor = Color.yellow;
                    GUILayout.BeginVertical("box");
                    GUI.backgroundColor = oldCol;

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Previews Folder", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    EditorGUILayout.LabelField(AI.GetPreviewFolder().Replace("\\", "/"), EditorStyles.wordWrappedLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(_previewSize < 0 ? "calculating..." : EditorUtility.FormatBytes(_previewSize), UIStyles.miniLabelRight, GUILayout.Width(sizeLabelWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(labelWidth + 6);
                    if (GUILayout.Button("Leave in Place", GUILayout.ExpandWidth(false)))
                    {
                        AI.Config.previewFolder = AI.GetPreviewFolder();
                        AI.SaveConfig();
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();

                    spaceRequired += _previewSize;
                }

                if (string.IsNullOrEmpty(AI.Config.backupFolder))
                {
                    Color oldCol = GUI.backgroundColor;
                    GUI.backgroundColor = Color.yellow;
                    GUILayout.BeginVertical("box");
                    GUI.backgroundColor = oldCol;

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Backup Folder", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    EditorGUILayout.LabelField(AI.GetBackupFolder().Replace("\\", "/"), EditorStyles.wordWrappedLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(_backupSize < 0 ? "calculating..." : EditorUtility.FormatBytes(_backupSize), UIStyles.miniLabelRight, GUILayout.Width(sizeLabelWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(labelWidth + 6);
                    if (GUILayout.Button("Leave in Place", GUILayout.ExpandWidth(false)))
                    {
                        AI.Config.backupFolder = AI.GetBackupFolder();
                        AI.SaveConfig();
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();

                    spaceRequired += _backupSize;
                }

                if (string.IsNullOrEmpty(AI.Config.cacheFolder))
                {
                    Color oldCol = GUI.backgroundColor;
                    GUI.backgroundColor = _ignoreCache ? Color.green : Color.yellow;
                    GUILayout.BeginVertical("box");
                    GUI.backgroundColor = oldCol;

                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Cache Folder", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
                    EditorGUILayout.LabelField(AI.GetMaterializeFolder().Replace("\\", "/"), EditorStyles.wordWrappedLabel);
                    GUILayout.FlexibleSpace();
                    if (!_ignoreCache) EditorGUILayout.LabelField(_cacheSize < 0 ? "calculating..." : EditorUtility.FormatBytes(_cacheSize), UIStyles.miniLabelRight, GUILayout.Width(sizeLabelWidth));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Space(labelWidth + 6);
                    if (GUILayout.Button("Leave in Place", GUILayout.ExpandWidth(false)))
                    {
                        AI.Config.cacheFolder = AI.GetMaterializeFolder();
                        AI.SaveConfig();
                    }
                    _ignoreCache = EditorGUILayout.ToggleLeft(UIStyles.Content("Ignore and Delete", "The cache will be recreated on demand whenever needed so it is not crucial to move it as well to save time. You will experience delays upon first access to packages though which were already cached."), _ignoreCache);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();

                    spaceRequired += _ignoreCache ? 0 : _cacheSize;
                }
            }

            GUILayout.FlexibleSpace();
            bool spaceIssues = !_sameDrive && spaceRequired > 0 && spaceRequired > _targetSpace;
            if (!_sameDrive && spaceIssues) EditorGUILayout.HelpBox("The target drive does not have enough space to move the database and all related files. Please select a different location or free up some space.", MessageType.Error);
            EditorGUI.BeginDisabledGroup(_calculating || spaceIssues);
            if (GUILayout.Button(_calculating ? "Calculating disk space..." : "Move Database", GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                MoveDatabase(_newFolder);
            }
            EditorGUI.EndDisabledGroup();
        }

        private void MoveDatabase(string targetFolder)
        {
            string targetDBFile = Path.Combine(targetFolder, Path.GetFileName(DBAdapter.GetDBPath()));
            if (File.Exists(targetDBFile)) File.Delete(targetDBFile);
            DBAdapter.Close();

            try
            {
                EditorUtility.DisplayProgressBar("Moving Database", "Moving database to new location...", 0.1f);
                File.Move(DBAdapter.GetDBPath(), targetDBFile);
                EditorUtility.ClearProgressBar();

                if (string.IsNullOrEmpty(AI.Config.previewFolder) && Directory.Exists(AI.GetPreviewFolder()))
                {
                    EditorUtility.DisplayProgressBar("Moving Preview Images", "Copying preview images to new location...", 0.3f);
                    Directory.Move(AI.GetPreviewFolder(), AI.GetPreviewFolder(targetFolder, true, false));
                    EditorUtility.ClearProgressBar();
                }

                if (string.IsNullOrEmpty(AI.Config.backupFolder) && Directory.Exists(AI.GetBackupFolder()))
                {
                    EditorUtility.DisplayProgressBar("Moving Backups", "Copying backups to new location...", 0.6f);
                    Directory.Move(AI.GetBackupFolder(), AI.GetBackupFolder(false, targetFolder));
                    EditorUtility.ClearProgressBar();
                }

                if (string.IsNullOrEmpty(AI.Config.cacheFolder) && Directory.Exists(AI.GetMaterializeFolder()))
                {
                    if (!_ignoreCache)
                    {
                        EditorUtility.DisplayProgressBar("Moving Cache", "Copying cache to new location...", 0.6f);
                        Directory.Move(AI.GetMaterializeFolder(), AI.GetMaterializeFolder(targetFolder, true));
                        EditorUtility.ClearProgressBar();
                    }
                    else
                    {
                        _ = IOUtils.DeleteFileOrDirectory(AI.GetMaterializeFolder());
                    }
                }

                // set new location
                AI.SwitchDatabase(targetFolder);
                Close();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error Moving Data",
                    "There were errors moving the existing database to a new location. Check the error log for details. Try moving the left-over files manually with Unity closed.\n\n" + e.Message,
                    "OK");
            }

            EditorUtility.ClearProgressBar();
        }
    }
}