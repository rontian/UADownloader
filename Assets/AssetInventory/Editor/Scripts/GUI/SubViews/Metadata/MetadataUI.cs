using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AssetInventory
{
    public sealed class MetadataUI : EditorWindow
    {
        private List<MetadataDefinition> _metas;
        private string _searchTerm;
        private Vector2 _scrollPos;
        private SearchField SearchField => _searchField = _searchField ?? new SearchField();
        private SearchField _searchField;

        public static MetadataUI ShowWindow()
        {
            MetadataUI window = GetWindow<MetadataUI>("Metadata Management");
            window.minSize = new Vector2(300, 250);

            return window;
        }

        private void OnEnable()
        {
            Metadata.OnDefinitionsChanged += Init;
        }

        private void OnDisable()
        {
            Metadata.OnDefinitionsChanged -= Init;
        }

        public void Init()
        {
            _metas = Metadata.LoadDefinitions();
            Metadata.LoadAssignments(null, false);
        }

        public void OnGUI()
        {
            _searchTerm = SearchField.OnGUI(_searchTerm, GUILayout.ExpandWidth(true));
            if (_metas != null)
            {
                EditorGUILayout.Space();
                if (_metas.Count == 0)
                {
                    EditorGUILayout.HelpBox("No metadata fields defined yet.", MessageType.Info);
                }
                else
                {
                    _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
                    foreach (MetadataDefinition meta in _metas)
                    {
                        // filter
                        if (!string.IsNullOrWhiteSpace(_searchTerm) && !meta.Name.ToLowerInvariant().Contains(_searchTerm.ToLowerInvariant())) continue;

                        GUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(new GUIContent($"{meta.Name} ({meta.Type}{(meta.RestrictAssetSource ? $", {StringUtils.CamelCaseToWords(meta.ApplicableSource.ToString())}" : "")})"), EditorStyles.boldLabel);
                        if (GUILayout.Button(EditorGUIUtility.IconContent("editicon.sml", "|Edit metadata"), GUILayout.Width(30)))
                        {
                            MetadataEditorUI metaUI = MetadataEditorUI.ShowWindow();
                            metaUI.Init(meta);
                        }
                        if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Remove metadata completely"), GUILayout.Width(30)))
                        {
                            if (EditorUtility.DisplayDialog("Delete Metadata Definitions", "Are you sure you want to delete this metadata definitions and all connected data? This action cannot be undone.", "Delete", "Cancel"))
                            {
                                Metadata.DeleteDefinition(meta);
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New..."))
            {
                MetadataEditorUI metaUI = MetadataEditorUI.ShowWindow();
                metaUI.Init();
            }
            EditorGUI.BeginDisabledGroup(_metas == null || _metas.Count == 0);
            if (GUILayout.Button("Delete All"))
            {
                if (EditorUtility.DisplayDialog("Delete All Metadata Definitions", "Are you sure you want to delete all metadata definitions? This action cannot be undone.", "Delete", "Cancel"))
                {
                    _metas?.ForEach(Metadata.DeleteDefinition);
                }
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
        }
    }
}