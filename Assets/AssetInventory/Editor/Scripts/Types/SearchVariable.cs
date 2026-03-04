using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Represents a variable that can be used in search queries.
    /// Variables allow parameterization of saved searches for reusability.
    /// </summary>
    [Serializable]
    public class SearchVariable
    {
        public string name;
        public string defaultValue;
        public List<string> options = new List<string>(); // Predefined list of values
        public string currentValue; // Active value for this session
    }

    /// <summary>
    /// Collection of search variables with JSON serialization support.
    /// Used to persist variable definitions in saved searches.
    /// </summary>
    [Serializable]
    public class SearchVariableCollection
    {
        [SerializeField] private List<SearchVariableData> variables = new List<SearchVariableData>();

        [Serializable]
        private class SearchVariableData
        {
            public string name;
            public string defaultValue;
            public List<string> options = new List<string>();
        }

        public Dictionary<string, SearchVariable> Variables
        {
            get
            {
                Dictionary<string, SearchVariable> result = new Dictionary<string, SearchVariable>();
                foreach (SearchVariableData data in variables)
                {
                    result[data.name] = new SearchVariable
                    {
                        name = data.name,
                        defaultValue = data.defaultValue,
                        options = data.options ?? new List<string>(),
                        currentValue = data.defaultValue
                    };
                }
                return result;
            }
            set
            {
                variables.Clear();
                foreach (var kvp in value)
                {
                    variables.Add(new SearchVariableData
                    {
                        name = kvp.Key,
                        defaultValue = kvp.Value.defaultValue,
                        options = kvp.Value.options ?? new List<string>()
                    });
                }
            }
        }

        /// <summary>
        /// Serializes the variable collection to JSON.
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        /// <summary>
        /// Deserializes a variable collection from JSON.
        /// </summary>
        public static SearchVariableCollection FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new SearchVariableCollection();
            }

            try
            {
                return JsonUtility.FromJson<SearchVariableCollection>(json);
            }
            catch
            {
                return new SearchVariableCollection();
            }
        }

        /// <summary>
        /// Creates a collection from a dictionary of search variables.
        /// </summary>
        public static SearchVariableCollection FromDictionary(Dictionary<string, SearchVariable> variables)
        {
            SearchVariableCollection collection = new SearchVariableCollection();
            collection.Variables = variables;
            return collection;
        }
    }
}