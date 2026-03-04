using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class PackageDeletionUI : BasicEditorUI
    {
        private enum DeletionMode
        {
            DatabaseOnly = 0,
            FileSystemOnly = 1,
            Both = 2
        }

        private AssetInfo _info;
        private Action _onComplete;
        private DeletionMode _selectedMode = DeletionMode.DatabaseOnly;
        private bool _canDeleteFromFileSystem;

        public static PackageDeletionUI ShowWindow()
        {
            PackageDeletionUI window = GetWindow<PackageDeletionUI>("Delete Package");
            window.maxSize = new Vector2(500, 400);
            window.minSize = window.maxSize;

            return window;
        }

        public void Init(AssetInfo info, Action onComplete = null)
        {
            _info = info;
            _onComplete = onComplete;

            // Determine available options based on package type and state
            _canDeleteFromFileSystem = info.ParentId <= 0 && info.IsDownloaded && info.SafeName != Asset.NONE
                && info.AssetSource != Asset.Source.RegistryPackage && info.AssetSource != Asset.Source.AssetManager
                && info.AssetSource != Asset.Source.Directory;

            // Set default selection
            _selectedMode = DeletionMode.DatabaseOnly;
        }

        public override void OnGUI()
        {
            if (_info == null)
            {
                EditorGUILayout.HelpBox("No package selected.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space(10);

            // Package information
            GUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Package Information", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            int labelWidth = 80;

            GUILabelWithTextNoMax("Name:", _info.GetDisplayName(), labelWidth);
            GUILabelWithTextNoMax("Type:", StringUtils.CamelCaseToWords(_info.AssetSource.ToString()), labelWidth);
            if (!string.IsNullOrEmpty(_info.Version))
            {
                GUILabelWithTextNoMax("Version:", _info.Version, labelWidth);
            }
            if (_info.IsDownloaded && !string.IsNullOrEmpty(_info.GetLocation(true)))
            {
                GUILabelWithTextNoMax("Location:", _info.GetLocation(true), labelWidth, null, true);
            }
            GUILayout.EndVertical();

            EditorGUILayout.Space(15);

            // Deletion options
            EditorGUILayout.LabelField("Deletion Options", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Database only option (always available)
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_selectedMode == DeletionMode.DatabaseOnly, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(15)))
            {
                _selectedMode = DeletionMode.DatabaseOnly;
            }
            if (GUILayout.Button("Delete from Index", EditorStyles.label))
            {
                _selectedMode = DeletionMode.DatabaseOnly;
            }
            GUILayout.EndHorizontal();

            // File system only option (conditionally available)
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!_canDeleteFromFileSystem);
            if (GUILayout.Toggle(_selectedMode == DeletionMode.FileSystemOnly, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(15)))
            {
                if (_canDeleteFromFileSystem) _selectedMode = DeletionMode.FileSystemOnly;
            }
            if (GUILayout.Button("Delete from File System", EditorStyles.label))
            {
                if (_canDeleteFromFileSystem) _selectedMode = DeletionMode.FileSystemOnly;
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            // Both option (conditionally available)
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!_canDeleteFromFileSystem);
            if (GUILayout.Toggle(_selectedMode == DeletionMode.Both, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(15)))
            {
                if (_canDeleteFromFileSystem) _selectedMode = DeletionMode.Both;
            }
            if (GUILayout.Button("Delete from Index and File System", EditorStyles.label))
            {
                if (_canDeleteFromFileSystem) _selectedMode = DeletionMode.Both;
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // Show appropriate warning message based on selected mode
            switch (_selectedMode)
            {
                case DeletionMode.DatabaseOnly:
                    EditorGUILayout.HelpBox("The package will be removed from the index only. The file will remain in the cache and the package will reappear after the next index update.", MessageType.Warning);
                    break;
                case DeletionMode.FileSystemOnly:
                    EditorGUILayout.HelpBox("The package file will be removed from the location above. The index entry will remain and marked as not downloaded.", MessageType.Info);
                    break;
                case DeletionMode.Both:
                    EditorGUILayout.HelpBox("The package will be permanently removed from both the index and the file system.", MessageType.Warning);
                    break;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.Space(10);

            // Action buttons
            if (GUILayout.Button("Delete", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                PerformDeletion();
            }

            EditorGUILayout.Space(5);
        }

        private void PerformDeletion()
        {
            switch (_selectedMode)
            {
                case DeletionMode.DatabaseOnly:
                    // Delete from database only
                    AI.RemovePackage(_info, false);
                    break;

                case DeletionMode.FileSystemOnly:
                    // Delete from file system only
                    if (File.Exists(_info.GetLocation(true)))
                    {
                        File.Delete(_info.GetLocation(true));
                        _info.SetLocation(null);
                        _info.PackageSize = 0;
                        _info.CurrentState = Asset.State.New;
                        _info.Refresh();
                        DBAdapter.DB.Execute("update Asset set Location=null, PackageSize=0, CurrentState=? where Id=?", Asset.State.New, _info.AssetId);
                    }
                    break;

                case DeletionMode.Both:
                    // Delete from both database and file system
                    AI.RemovePackage(_info, true);
                    break;
            }

            _onComplete?.Invoke();
            Close();
        }
    }
}