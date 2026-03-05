using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UADownloader.Filters
{
    public class FilterSelectionWindow : EditorWindow
    {
        private string _exportPath;
        private List<IPackageFilter> _availableFilters;
        private IPackageFilter _selectedFilter;
        private Dictionary<string, string> _paramInputs = new Dictionary<string, string>();
        private Action<IPackageFilter> _onFilterSelected;
        
        public static void ShowWindow(string exportPath, Action<IPackageFilter> onFilterSelected)
        {
            var window = GetWindow<FilterSelectionWindow>("选择过滤器");
            window.minSize = new Vector2(400, 300);
            window._exportPath = exportPath;
            window._onFilterSelected = onFilterSelected;
            window._availableFilters = FilterRegistry.GetAllAvailableFilters(exportPath);
            window.Show();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("可用过滤器", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            
            if (_availableFilters == null || _availableFilters.Count == 0)
            {
                EditorGUILayout.HelpBox("没有可用的过滤器", MessageType.Info);
                return;
            }
            
            foreach (var filter in _availableFilters)
            {
                EditorGUILayout.BeginVertical("box");
                
                EditorGUILayout.BeginHorizontal();
                
                bool isSelected = _selectedFilter == filter;
                if (GUILayout.Toggle(isSelected, "", GUILayout.Width(20)))
                {
                    if (!isSelected)
                    {
                        _selectedFilter = filter;
                        InitializeParamInputs();
                    }
                }
                else if (isSelected)
                {
                    _selectedFilter = null;
                    _paramInputs.Clear();
                }
                
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField(filter.GetName(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(filter.GetDescription(), EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                
                EditorGUILayout.EndHorizontal();
                
                if (isSelected)
                {
                    DrawParamInputs(filter);
                }
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }
            
            EditorGUILayout.Space(10);
            
            GUI.enabled = _selectedFilter != null;
            if (GUILayout.Button("确定", GUILayout.Height(30)))
            {
                ApplyParamsAndClose();
            }
            GUI.enabled = true;
            
            if (GUILayout.Button("取消", GUILayout.Height(30)))
            {
                Close();
            }
        }
        
        private void InitializeParamInputs()
        {
            _paramInputs.Clear();
            
            if (_selectedFilter == null) return;
            
            var paramDefs = _selectedFilter.GetParamDefinitions();
            var currentParams = _selectedFilter.GetParams();
            
            foreach (var paramDef in paramDefs)
            {
                string paramName = paramDef.Key;
                if (currentParams.ContainsKey(paramName))
                {
                    _paramInputs[paramName] = currentParams[paramName]?.ToString() ?? "";
                }
                else
                {
                    _paramInputs[paramName] = GetDefaultValue(paramDef.Value);
                }
            }
        }
        
        private void DrawParamInputs(IPackageFilter filter)
        {
            var paramDefs = filter.GetParamDefinitions();
            if (paramDefs == null || paramDefs.Count == 0)
            {
                return;
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("参数设置:", EditorStyles.boldLabel);
            
            foreach (var paramDef in paramDefs)
            {
                string paramName = paramDef.Key;
                FilterParamType paramType = paramDef.Value;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GetParamDisplayName(paramName), GUILayout.Width(150));
                
                if (!_paramInputs.ContainsKey(paramName))
                {
                    _paramInputs[paramName] = GetDefaultValue(paramType);
                }
                
                _paramInputs[paramName] = EditorGUILayout.TextField(_paramInputs[paramName]);
                
                EditorGUILayout.EndHorizontal();
            }
        }
        
        private string GetParamDisplayName(string paramName)
        {
            switch (paramName)
            {
                case "maxSizeMB":
                    return "最大文件大小(MB):";
                default:
                    return paramName + ":";
            }
        }
        
        private string GetDefaultValue(FilterParamType paramType)
        {
            switch (paramType)
            {
                case FilterParamType.String:
                    return "";
                case FilterParamType.Int:
                    return "500";
                case FilterParamType.Float:
                    return "0.0";
                case FilterParamType.Bool:
                    return "true";
                default:
                    return "";
            }
        }
        
        private void ApplyParamsAndClose()
        {
            if (_selectedFilter == null) return;
            
            var paramValues = new Dictionary<string, object>();
            var paramDefs = _selectedFilter.GetParamDefinitions();
            
            foreach (var paramDef in paramDefs)
            {
                string paramName = paramDef.Key;
                FilterParamType paramType = paramDef.Value;
                
                if (!_paramInputs.ContainsKey(paramName))
                {
                    continue;
                }
                
                string inputValue = _paramInputs[paramName];
                
                try
                {
                    object parsedValue = ParseValue(inputValue, paramType);
                    paramValues[paramName] = parsedValue;
                }
                catch (Exception e)
                {
                    Debug.LogError($"参数 {paramName} 解析失败: {e.Message}");
                    return;
                }
            }
            
            _selectedFilter.SetParams(paramValues);
            _onFilterSelected?.Invoke(_selectedFilter);
            Close();
        }
        
        private object ParseValue(string input, FilterParamType paramType)
        {
            switch (paramType)
            {
                case FilterParamType.String:
                    return input;
                case FilterParamType.Int:
                    return int.Parse(input);
                case FilterParamType.Float:
                    return float.Parse(input);
                case FilterParamType.Bool:
                    return bool.Parse(input);
                default:
                    return input;
            }
        }
    }
}
