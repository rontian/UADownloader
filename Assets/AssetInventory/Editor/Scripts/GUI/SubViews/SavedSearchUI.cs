using System;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class SavedSearchUI : BasicEditorUI
    {
        private SavedSearch _savedSearch;
        private Action<SavedSearch> _onSave;
        private Rect _iconButtonRect;

        public static SavedSearchUI ShowWindow()
        {
            SavedSearchUI window = GetWindow<SavedSearchUI>("Saved Search Editor");
            window.minSize = new Vector2(400, 150);
            return window;
        }

        public void Init(SavedSearch savedSearch, Action<SavedSearch> onSave = null)
        {
            _savedSearch = savedSearch;
            _onSave = onSave;
        }

        public override void OnGUI()
        {
            int labelWidth = 100;

            if (_savedSearch == null)
            {
                Close();
                return;
            }

            GUILabelWithTextNoMax("Search Phrase", _savedSearch.SearchPhrase, labelWidth, null, true);
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
            _savedSearch.Name = EditorGUILayout.TextField(_savedSearch.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Color", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
            Color currentColor = Color.white;
            if (!string.IsNullOrEmpty(_savedSearch.Color))
            {
                ColorUtility.TryParseHtmlString("#" + _savedSearch.Color, out currentColor);
            }
            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField(GUIContent.none, currentColor, false, false, false, GUILayout.Width(20));
            if (EditorGUI.EndChangeCheck())
            {
                _savedSearch.Color = ColorUtility.ToHtmlStringRGB(newColor);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Icon", EditorStyles.boldLabel, GUILayout.Width(labelWidth));

            // Show current icon if selected
            if (!string.IsNullOrEmpty(_savedSearch.Icon))
            {
                GUIContent iconContent = EditorGUIUtility.IconContent(_savedSearch.Icon);
                GUILayout.Label(iconContent, GUILayout.Width(24), GUILayout.Height(24));
                // GUILayout.Label(_savedSearch.Icon, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            }
            else
            {
                GUILayout.Label("-No Icon-", GUILayout.ExpandWidth(true));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select...", GUILayout.ExpandWidth(false)))
            {
                IconSelectionUI iconSelectionUI = new IconSelectionUI();
                iconSelectionUI.Init(iconName => _savedSearch.Icon = iconName);
                PopupWindow.Show(_iconButtonRect, iconSelectionUI);
            }
            if (Event.current.type == EventType.Repaint) _iconButtonRect = GUILayoutUtility.GetLastRect();
            if (!string.IsNullOrEmpty(_savedSearch.Icon) && GUILayout.Button("Clear", GUILayout.ExpandWidth(false)))
            {
                _savedSearch.Icon = null;
            }
            GUILayout.EndHorizontal();

            // Action Buttons
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Update", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                if (string.IsNullOrWhiteSpace(_savedSearch.Name) && string.IsNullOrWhiteSpace(_savedSearch.Icon))
                {
                    EditorUtility.DisplayDialog("Invalid Name", "Please enter a name or set an icon for the saved search.", "OK");
                    return;
                }

                DBAdapter.DB.Update(_savedSearch);
                _onSave?.Invoke(_savedSearch);

                Close();
            }
        }
    }
}