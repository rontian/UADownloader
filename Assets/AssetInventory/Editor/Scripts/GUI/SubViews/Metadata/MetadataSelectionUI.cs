using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AssetInventory
{
    public sealed class MetadataSelectionUI : PopupWindowContent
    {
        private List<AssetInfo> _assetInfo;
        private List<MetadataDefinition> _metas;
        private Vector2 _scrollPos;
        private bool _firstRunDone;
        private SearchField SearchField => _searchField = _searchField ?? new SearchField();
        private SearchField _searchField;
        private MetadataAssignment.Target _target;
        private Action _onSelect;

        public void Init(MetadataAssignment.Target target, Action onSelect = null)
        {
            _target = target;
            _onSelect = onSelect;
            _metas = Metadata.LoadDefinitions();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(220, AI.Config.tagListHeight);
        }

        public void SetAssets(List<AssetInfo> infos)
        {
            _assetInfo = infos;
        }

        public override void OnGUI(Rect rect)
        {
            if (_assetInfo == null) return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Select Metadata to Add", EditorStyles.boldLabel, GUILayout.Width(150));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(EditorGUIUtility.IconContent("Settings", "|Manage Metadata").image, EditorStyles.label))
            {
                MetadataUI metasUI = MetadataUI.ShowWindow();
                metasUI.Init();
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.Space();
            if (_metas != null)
            {
                if (_metas.Count == 0)
                {
                    EditorGUILayout.HelpBox("No metadata fields defined yet. Use the metadata wizard to create new definitions.", MessageType.Info);
                }
                else
                {
                    _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
                    int shownItems = 0;
                    foreach (MetadataDefinition meta in _metas)
                    {
                        if (meta.RestrictAssetSource && !_assetInfo.Any(t => t.AssetSource == meta.ApplicableSource)) continue;

                        // don't show already added tags (for case of only one item selected, otherwise assigning it to all)
                        switch (_target)
                        {
                            case MetadataAssignment.Target.Package:
                                if (_assetInfo.Count == 1 && _assetInfo[0].PackageMetadata.Any(t => t.MetadataId == meta.Id)) continue;
                                break;

                            case MetadataAssignment.Target.Asset:
                                // if (_assetInfo.Count == 1 && _assetInfo[0].AssetTags.Any(t => t.TagId == meta.Id)) continue;
                                break;
                        }
                        shownItems++;

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(8);
                        if (GUILayout.Button($"{meta.Name}" + (AI.ShowAdvanced() ? $" ({meta.Type})" : "")))
                        {
                            _assetInfo.ForEach(info =>
                            {
                                if (meta.RestrictAssetSource && info.AssetSource != meta.ApplicableSource) return;

                                Metadata.AddAssignment(info, meta.Id, _target, true);
                            });
                            _onSelect?.Invoke();
                            editorWindow.Close();
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (shownItems == 0)
                    {
                        EditorGUILayout.HelpBox("All available custom metadata fields were assigned already. Use the metadata wizard to create new ones if needed.", MessageType.Info);
                    }
                    GUILayout.EndScrollView();
                }
            }
            if (!_firstRunDone)
            {
                SearchField.SetFocus();
                _firstRunDone = true;
            }
        }
    }
}