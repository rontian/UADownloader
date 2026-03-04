using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AssetInventory
{
    public sealed class TagsUI : BasicEditorUI
    {
        private List<Tag> _tags;
        private string _searchTerm;
        private Vector2 _scrollPos;
        private SearchField SearchField => _searchField = _searchField ?? new SearchField();
        private SearchField _searchField;

        public static TagsUI ShowWindow()
        {
            TagsUI window = GetWindow<TagsUI>("Tag Management");
            window.minSize = new Vector2(410, 200);
            
            return window;
        }

        public void Init()
        {
            _tags = Tagging.LoadTags();
        }

        public void OnEnable()
        {
            Tagging.OnTagsChanged += Init;
        }

        public void OnDisable()
        {
            Tagging.OnTagsChanged -= Init;
        }

        private Tag GetTagWithHotkey(string hotkey)
        {
            if (string.IsNullOrEmpty(hotkey)) return null;
            return _tags.Find(t => t.Hotkey == hotkey);
        }

        private void SetHotkey(Tag tag, string newHotkey)
        {
            if (string.IsNullOrEmpty(newHotkey))
            {
                tag.Hotkey = null;
                Tagging.SaveTag(tag);
                return;
            }

            // Only allow single letter or number
            if (newHotkey.Length > 1)
            {
                newHotkey = newHotkey.Substring(0, 1);
            }
            if (!char.IsLetterOrDigit(newHotkey[0])) return;

            // If hotkey is already in use by another tag, remove it from that tag
            newHotkey = newHotkey.ToLowerInvariant();
            Tag existingTag = GetTagWithHotkey(newHotkey);
            if (existingTag != null && existingTag.Id != tag.Id)
            {
                existingTag.Hotkey = null;
                Tagging.SaveTag(existingTag);
            }

            tag.Hotkey = newHotkey;
            Tagging.SaveTag(tag);
        }

        public override void OnGUI()
        {
            if (Event.current.isKey && Event.current.keyCode == KeyCode.Return && !string.IsNullOrWhiteSpace(_searchTerm))
            {
                if (_searchTerm.Contains("/")) // prevent creating tags with slashes, as they are used for subfolders
                {
                    EditorUtility.DisplayDialog("Invalid Tag", "Tags cannot contain slashes (/). Please use a different name.", "OK");
                }
                else
                {
                    Tagging.AddTag(_searchTerm);
                    _searchTerm = "";
                }
            }
            _searchTerm = SearchField.OnGUI(_searchTerm, GUILayout.ExpandWidth(true));
            if (_tags != null)
            {
                EditorGUILayout.Space();
                if (_tags.Count == 0)
                {
                    EditorGUILayout.HelpBox("No tags created yet. Use the textfield above to create the first tag.", MessageType.Info);
                }
                else
                {
                    _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
                    foreach (Tag tag in _tags)
                    {
                        // filter
                        if (!string.IsNullOrWhiteSpace(_searchTerm) && !tag.Name.ToLowerInvariant().Contains(_searchTerm.ToLowerInvariant())) continue;

                        GUILayout.BeginHorizontal();
                        EditorGUI.BeginChangeCheck();
                        tag.Color = "#" + ColorUtility.ToHtmlStringRGB(EditorGUILayout.ColorField(GUIContent.none, tag.GetColor(), false, false, false, GUILayout.Width(20)));
                        if (EditorGUI.EndChangeCheck()) Tagging.SaveTag(tag);

                        EditorGUILayout.LabelField(new GUIContent(tag.Name, tag.FromAssetStore ? "From Asset Store" : "Local Tag"));
                        if (GUILayout.Button(EditorGUIUtility.IconContent("editicon.sml", "|Rename tag"), GUILayout.Width(30)))
                        {
                            NameUI nameUI = new NameUI();
                            nameUI.Init(tag.Name, newName => RenameTag(tag, newName));
                            PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
                        }

                        // Hotkey button
                        string buttonText = string.IsNullOrEmpty(tag.Hotkey) ? "Set Hotkey" : $"Alt+{tag.Hotkey}";
                        if (GUILayout.Button(buttonText, GUILayout.Width(90)))
                        {
                            NameUI nameUI = new NameUI();
                            nameUI.Init(tag.Hotkey, newHotkey => SetHotkey(tag, newHotkey), true);
                            PopupWindow.Show(new Rect(Event.current.mousePosition.x, Event.current.mousePosition.y, 0, 0), nameUI);
                        }

                        if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Remove tag completely"), GUILayout.Width(30)))
                        {
                            if (EditorUtility.DisplayDialog("Delete Tag", $"Are you sure you want to delete the tag '{tag.Name}'? This action cannot be undone.", "Delete", "Cancel"))
                            {
                                Tagging.DeleteTag(tag);
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (!string.IsNullOrWhiteSpace(_searchTerm))
                    {
                        EditorGUILayout.HelpBox("Press RETURN to create a new tag", MessageType.Info);
                    }
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox("Temporary limitation: Actual tag colors will appear darker than selected here.", MessageType.Info);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Delete All"))
                    {
                        if (EditorUtility.DisplayDialog("Delete All Tags", "Are you sure you want to delete all tags? This action cannot be undone.", "Delete", "Cancel"))
                        {
                            _tags.ForEach(Tagging.DeleteTag);
                        }
                    }
                    GUILayout.EndScrollView();
                }
            }
        }

        private void RenameTag(Tag tag, string newName)
        {
            if (string.IsNullOrEmpty(newName) || tag.Name == newName) return;

            Tag existingTag = DBAdapter.DB.Find<Tag>(t => t.Id != tag.Id && t.Name.ToLower() == newName.ToLower());
            if (existingTag != null)
            {
                EditorUtility.DisplayDialog("Error", "A tag with that name already exists (and merging tags is not yet supported).", "OK");
                return;
            }
            if (newName.Contains("/")) // prevent creating tags with slashes, as they are used for subfolders
            {
                EditorUtility.DisplayDialog("Invalid Tag", "Tags cannot contain slashes (/). Please use a different name.", "OK");
                return;
            }

            Tagging.RenameTag(tag, newName);
        }
    }
}