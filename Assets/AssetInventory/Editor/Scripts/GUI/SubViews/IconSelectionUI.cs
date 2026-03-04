using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace AssetInventory
{
    public sealed class IconSelectionUI : PopupWindowContent
    {
        private string[] _iconNames;
        private string _search;
        private Vector2 _scroll;
        private bool _firstRunDone;
        private SearchField SearchField => _searchField = _searchField ?? new SearchField();
        private SearchField _searchField;
        private Action<string> _onIconSelected;

        public void Init(Action<string> onIconSelected = null)
        {
            _onIconSelected = onIconSelected;

            Type asc = typeof (EditorGUIUtility);
            MethodInfo importPackageMethod = asc.GetMethod("GetEditorAssetBundle", BindingFlags.NonPublic | BindingFlags.Static);
            AssetBundle editorAssetBundle = (AssetBundle)importPackageMethod?.Invoke(null, null);

            _iconNames = editorAssetBundle?
                .GetAllAssetNames()
                // 1. Filter by path prefix before any asset loads
                .Where(path => path.StartsWith("icons/", StringComparison.Ordinal))
                // 2. Load once and drop nulls
                .Select(path => new {path, icon = editorAssetBundle.LoadAsset<Texture2D>(path)})
                .Where(x => x.icon != null)
                // 3. Compute lower-case name a single time
                .Select(x => new
                {
                    x.icon.name, lower = x.icon.name.ToLowerInvariant()
                })
                // 4. Apply all inexpensive string tests on the lower-case name
                .Where(x =>
                    !x.lower.StartsWith("d_") &&
                    !x.lower.EndsWith(".small") &&
                    !x.lower.EndsWith("_sml") &&
                    !x.name.Contains("@"))
                // 5. Order and project to just the original name
                .OrderBy(x => x.name)
                .Select(x => x.name)
                .Distinct()
                .ToArray();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(300, 400);
        }

        public override void OnGUI(Rect rect)
        {
            if (_iconNames == null) return;

            _search = SearchField.OnGUI(_search, GUILayout.ExpandWidth(true));

            string[] list = string.IsNullOrEmpty(_search)
                ? _iconNames
                : _iconNames.Where(n => n.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToArray();

            if (list.Length == 0)
            {
                EditorGUILayout.HelpBox("No icons found matching your search.", MessageType.Info);
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            int cols = Mathf.Max(1, Mathf.FloorToInt(rect.width / 40f));
            for (int i = 0; i < list.Length; i += cols)
            {
                EditorGUILayout.BeginHorizontal();
                for (int j = 0; j < cols && i + j < list.Length; j++)
                {
                    string name = list[i + j];
                    GUIContent icon = EditorGUIUtility.IconContent(name);
                    if (GUILayout.Button(icon, GUILayout.Width(32), GUILayout.Height(32)))
                    {
                        _onIconSelected?.Invoke(name);
                        editorWindow.Close();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (!_firstRunDone)
            {
                SearchField.SetFocus();
                _firstRunDone = true;
            }
        }
    }
}