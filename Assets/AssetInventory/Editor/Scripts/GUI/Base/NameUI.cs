using System;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class NameUI : PopupWindowContent
    {
        private string _text;
        private string _title;
        private Action<string> _callback;
        private bool _firstRunDone;
        private bool _allowEmpty;

        public void Init(string text, Action<string> callback, bool allowEmpty = false, string title = null)
        {
            _text = text;
            _callback = callback;
            _allowEmpty = allowEmpty;
            _title = title;
        }

        public override void OnGUI(Rect rect)
        {
            editorWindow.maxSize = new Vector2(200, string.IsNullOrEmpty(_title) ? 45 : 65);

            if (!string.IsNullOrEmpty(_title))
            {
                EditorGUILayout.LabelField(_title, EditorStyles.boldLabel);
            }

            GUI.SetNextControlName("TextField");
            _text = EditorGUILayout.TextField(_text, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            if ((Event.current.isKey && Event.current.keyCode == KeyCode.Return)
                || GUILayout.Button("OK", UIStyles.mainButton, GUILayout.ExpandWidth(true))
                && (_allowEmpty || !string.IsNullOrWhiteSpace(_text)))
            {
                _callback?.Invoke(_text);
                editorWindow.Close();
            }
            if (GUILayout.Button("Cancel", GUILayout.ExpandWidth(false)))
            {
                editorWindow.Close();
            }
            GUILayout.EndHorizontal();

            if (!_firstRunDone)
            {
                GUI.FocusControl("TextField");
                _firstRunDone = true;
            }
        }
    }
}