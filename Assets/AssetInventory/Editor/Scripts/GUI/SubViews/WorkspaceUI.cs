using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AssetInventory
{
    public sealed class WorkspaceUI : BasicEditorUI
    {
        private Workspace _workspace;
        private List<WorkspaceSearch> _searches;
        private List<SavedSearch> _savedSearches;
        private Vector2 _scrollPos;
        private Action<Workspace> _onSave;

        public static WorkspaceUI ShowWindow()
        {
            WorkspaceUI window = GetWindow<WorkspaceUI>("Workspace Editor");
            window.minSize = new Vector2(300, 180);
            return window;
        }

        public void Init(Workspace workspace, Action<Workspace> onSave = null)
        {
            _workspace = workspace;
            _searches = _workspace?.LoadSearches();
            _onSave = onSave;
            _savedSearches = DBAdapter.DB.Table<SavedSearch>().ToList();
            _serializedSearchesObject = null;
        }

        private sealed class SearchesWrapper : ScriptableObject
        {
            public List<WorkspaceSearch> searches = new List<WorkspaceSearch>();
        }

        private ReorderableList SearchesListControl
        {
            get
            {
                if (_searchesListControl == null) InitSearchesControl();
                return _searchesListControl;
            }
        }
        private ReorderableList _searchesListControl;

        private SerializedObject SerializedSearchesObject
        {
            get
            {
                // reference can become null on reload
                if (_serializedSearchesObject == null || _serializedSearchesObject.targetObjects.FirstOrDefault() == null) InitSearchesControl();
                return _serializedSearchesObject;
            }
        }
        private SerializedObject _serializedSearchesObject;
        private SerializedProperty _searchesProperty;
        private int _selectedSearchIndex = -1;

        private void InitSearchesControl()
        {
            SearchesWrapper obj = CreateInstance<SearchesWrapper>();
            obj.searches = _searches;

            _serializedSearchesObject = new SerializedObject(obj);
            _searchesProperty = _serializedSearchesObject.FindProperty("searches");
            _searchesListControl = new ReorderableList(_serializedSearchesObject, _searchesProperty, true, true, true, true);
            _searchesListControl.drawElementCallback = DrawSearchListItem;
            _searchesListControl.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Saved Searches to Show");
            _searchesListControl.onAddCallback = OnAddSearch;
            _searchesListControl.onRemoveCallback = OnRemoveSearch;
            _searchesListControl.onReorderCallbackWithDetails = OnReorderCallbackWithDetails;
        }

        private void OnReorderCallbackWithDetails(ReorderableList list, int oldIndex, int newIndex)
        {
            // move search from old to new position since list does for some reason not persist this
            // newIndex is already adjusted with removal in mind
            WorkspaceSearch item = _searches[oldIndex];
            _searches.RemoveAt(oldIndex);
            _searches.Insert(newIndex, item);
        }

        private void OnAddSearch(ReorderableList list)
        {
            GenericMenu menu = new GenericMenu();
            foreach (SavedSearch search in _savedSearches)
            {
                menu.AddItem(new GUIContent(search.Name, search.SearchPhrase), false, () => AddSearch(search));
            }
            menu.ShowAsContext();
        }

        private void OnRemoveSearch(ReorderableList list)
        {
            if (_selectedSearchIndex < 0 || _selectedSearchIndex >= _searches.Count) return;
            _searches.RemoveAt(_selectedSearchIndex);
            _selectedSearchIndex = -1;
        }

        private void DrawSearchListItem(Rect rect, int index, bool isActive, bool isFocused)
        {
            // draw alternating-row background
            if (Event.current.type == EventType.Repaint && index % 2 == 1)
            {
                // choose a tiny overlay that will darken/lighten regardless of the exact theme colors
                Color overlay = EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f) // on dark (Pro) skin, brighten a hair
                    : new Color(0f, 0f, 0f, 0.025f); // on light skin, darken a hair

                EditorGUI.DrawRect(rect, overlay);
            }

            if (index >= _searches.Count) return;
            if (isFocused) _selectedSearchIndex = index;
            if (!isFocused && _selectedSearchIndex == index) _selectedSearchIndex = -1;

            WorkspaceSearch search = _searches[index];
            SavedSearch savedSearch = _savedSearches.FirstOrDefault(s => s.Id == search.SavedSearchId);

            GUI.Label(new Rect(rect.x, rect.y + 2, rect.width, rect.height),
                UIStyles.Content(savedSearch != null ? savedSearch.Name : $"-Unknown Search- ({search.SavedSearchId})"),
                UIStyles.entryStyle);
        }

        public override void OnGUI()
        {
            int labelWidth = 80;

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.Width(labelWidth));
            _workspace.Name = EditorGUILayout.TextField(_workspace.Name);
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(15);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
            if (SerializedSearchesObject != null)
            {
                SerializedSearchesObject.Update();
                SearchesListControl.DoLayoutList();
                SerializedSearchesObject.ApplyModifiedProperties();
            }
            GUILayout.EndScrollView();

            // Action Buttons
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_workspace.Name));
            if (GUILayout.Button("Update", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                Save();
                Close();
            }
            EditorGUI.EndDisabledGroup();
        }

        private void Save()
        {
            DBAdapter.DB.Update(_workspace);

            for (int i = 0; i < _searches.Count; i++)
            {
                WorkspaceSearch search = _searches[i];
                search.OrderIdx = i;

                if (search.Id > 0)
                {
                    DBAdapter.DB.Update(search);
                }
                else
                {
                    DBAdapter.DB.Insert(search);
                }
            }

            // delete removed searches
            DBAdapter.DB.Execute("delete from WorkspaceSearch where WorkspaceId=? and Id not in (" +
                string.Join(",", _searches.Select(s => s.Id)) + ")", _workspace.Id);

            _onSave?.Invoke(_workspace);
        }

        private void AddSearch(SavedSearch savedSearch)
        {
            WorkspaceSearch wsSearch = new WorkspaceSearch
            {
                WorkspaceId = _workspace.Id,
                SavedSearchId = savedSearch.Id,
                OrderIdx = _searches.Count
            };

            if (_selectedSearchIndex >= 0)
            {
                _searches.Insert(_selectedSearchIndex + 1, wsSearch);
            }
            else
            {
                _searches.Add(wsSearch);
            }
        }
    }
}