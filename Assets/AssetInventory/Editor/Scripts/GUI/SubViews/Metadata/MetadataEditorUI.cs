using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class MetadataEditorUI : BasicEditorUI
    {
        private MetadataDefinition _def;
        private Vector2 _scrollPos;

        public static MetadataEditorUI ShowWindow()
        {
            MetadataEditorUI window = GetWindow<MetadataEditorUI>("Metadata Definition");
            window.minSize = new Vector2(500, 250);
            window.maxSize = window.minSize;
            return window;
        }

        public void Init(MetadataDefinition metadataDefinition = null)
        {
            _def = metadataDefinition;
            if (_def == null) _def = new MetadataDefinition();
        }

        public override void OnGUI()
        {
            int labelWidth = 160;
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));

            EditorGUILayout.HelpBox("Define what type of additional data you want to provide to packages. You can also restrict where these fields are visible.", MessageType.Info);
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Name", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _def.Name = EditorGUILayout.TextField(_def.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Type", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _def.Type = (MetadataDefinition.DataType)EditorGUILayout.EnumPopup(_def.Type);
            GUILayout.EndHorizontal();

            if (_def.Type == MetadataDefinition.DataType.SingleSelect)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UIStyles.Content("Possible Values", "Possible values separated by comma"), EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                _def.ValueList = EditorGUILayout.TextField(_def.ValueList);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Restrict to Asset Source", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _def.RestrictAssetSource = EditorGUILayout.Toggle(_def.RestrictAssetSource, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (_def.RestrictAssetSource)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{UIStyles.INDENT}Asset Source", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                _def.ApplicableSource = (Asset.Source)EditorGUILayout.EnumPopup(_def.ApplicableSource);
                GUILayout.EndHorizontal();
            }

            /*
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Restrict to Asset Group", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
            _def.RestrictAssetGroup = EditorGUILayout.Toggle(_def.RestrictAssetGroup, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            if (_def.RestrictAssetGroup)
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{UIStyles.INDENT}Asset Group", EditorStyles.boldLabel, GUILayout.MaxWidth(labelWidth));
                _def.ApplicableGroup = (AI.AssetGroup)EditorGUILayout.EnumPopup(_def.ApplicableGroup);
                GUILayout.EndHorizontal();
            }
            */

            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_def.Name));
            if (GUILayout.Button(_def.Id > 0 ? "Update" : "Create", UIStyles.mainButton, GUILayout.Height(UIStyles.BIG_BUTTON_HEIGHT)))
            {
                if (Metadata.AddDefinition(_def) != null) Close();
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}