using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    /// <summary>
    /// Utility class for resolving text variables in action parameters.
    /// Variables are referenced using the $varname syntax.
    /// Supports both user-defined variables and predefined internal variables (e.g., $Application.unityVersion).
    /// </summary>
    public static class VariableResolver
    {
        // Pattern matches $varname where varname starts with letter or underscore
        // and can contain letters, numbers, underscores, and dots (for internal variables)
        private static readonly Regex VariablePattern = new Regex(@"\$([a-zA-Z_][a-zA-Z0-9_.]*)", RegexOptions.Compiled);

        /// <summary>
        /// Finds all variable references in the given text.
        /// </summary>
        /// <param name="text">Text to search for variables</param>
        /// <returns>List of unique variable names (without $ prefix)</returns>
        public static List<string> FindVariableReferences(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();

            MatchCollection matches = VariablePattern.Matches(text);

            // Use HashSet for deduplication, avoiding LINQ overhead
            HashSet<string> uniqueVars = new HashSet<string>();
            for (int i = 0; i < matches.Count; i++)
            {
                uniqueVars.Add(matches[i].Groups[1].Value);
            }

            return new List<string>(uniqueVars);
        }

        /// <summary>
        /// Replaces all variable references in the text with their actual values.
        /// Supports both user-defined variables and internal variables (e.g., $Application.unityVersion).
        /// Throws an exception if any variable cannot be resolved.
        /// </summary>
        /// <param name="text">Text containing variable references</param>
        /// <param name="variables">Dictionary mapping variable names to values (for user-defined variables)</param>
        /// <returns>Text with all variables replaced</returns>
        /// <exception cref="Exception">Thrown if a variable cannot be resolved</exception>
        public static string ReplaceVariables(string text, Dictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return VariablePattern.Replace(text, match =>
            {
                string varName = match.Groups[1].Value;

                // Check if this is an internal variable (contains a dot)
                if (IsInternalVariable(varName))
                {
                    // Resolve as internal variable using reflection
                    return ResolveInternalVariable(varName);
                }
                else
                {
                    // Resolve as user-defined variable
                    if (variables != null && variables.TryGetValue(varName, out string value))
                    {
                        return value;
                    }

                    // Variable not found - throw error
                    throw new Exception($"Variable '${varName}' is not defined");
                }
            });
        }

        /// <summary>
        /// Checks if the text contains any variable references.
        /// </summary>
        /// <param name="text">Text to check</param>
        /// <returns>True if text contains at least one variable reference</returns>
        public static bool ContainsVariables(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            return VariablePattern.IsMatch(text);
        }

        /// <summary>
        /// Validates that all variable references in the text can be resolved.
        /// Checks both user-defined variables and internal variables.
        /// </summary>
        /// <param name="text">Text to validate</param>
        /// <param name="variables">Dictionary of defined user variables</param>
        /// <returns>List of error messages for variables that cannot be resolved (empty if all are valid)</returns>
        public static List<string> ValidateVariables(string text, Dictionary<string, string> variables)
        {
            List<string> referencedVars = FindVariableReferences(text);
            if (referencedVars.Count == 0) return new List<string>();

            List<string> errors = new List<string>();
            foreach (string varName in referencedVars)
            {
                if (IsInternalVariable(varName))
                {
                    // Validate internal variable by attempting to resolve it
                    try
                    {
                        ResolveInternalVariable(varName);
                    }
                    catch (Exception e)
                    {
                        errors.Add($"${varName}: {e.Message}");
                    }
                }
                else
                {
                    // Validate user-defined variable
                    if (variables == null || !variables.ContainsKey(varName))
                    {
                        errors.Add($"${varName}: Variable is not defined");
                    }
                }
            }
            return errors;
        }

        /// <summary>
        /// Validates a variable name to ensure it follows naming rules for user-defined variables.
        /// User-defined variables cannot contain dots (dots are reserved for internal variables).
        /// </summary>
        /// <param name="variableName">Name to validate (without $ prefix)</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool IsValidVariableName(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName)) return false;

            // Must start with letter or underscore
            if (!char.IsLetter(variableName[0]) && variableName[0] != '_') return false;

            // Can only contain letters, numbers, underscores (NO dots for user-defined variables)
            foreach (char c in variableName)
            {
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a variable name represents an internal variable (contains a dot).
        /// </summary>
        /// <param name="variableName">Variable name to check (without $ prefix)</param>
        /// <returns>True if this is an internal variable</returns>
        public static bool IsInternalVariable(string variableName)
        {
            return !string.IsNullOrEmpty(variableName) && variableName.Contains(".");
        }

        /// <summary>
        /// Resolves an internal variable using reflection.
        /// Internal variables have the format "Group.Member" or "Group.Member.SubMember".
        /// Supports both properties and fields.
        /// </summary>
        /// <param name="variableName">Variable name (without $ prefix)</param>
        /// <returns>String value of the resolved property or field</returns>
        /// <exception cref="Exception">Thrown if the group is unknown or member cannot be resolved</exception>
        public static string ResolveInternalVariable(string variableName)
        {
            if (string.IsNullOrEmpty(variableName))
            {
                throw new Exception("Variable name cannot be empty");
            }

            string[] parts = variableName.Split('.');
            if (parts.Length < 2)
            {
                throw new Exception($"Internal variable '{variableName}' must have at least two components (Group.Property)");
            }

            string groupName = parts[0];
            Type targetType;
            object targetInstance = null;

            // Map group name to type
            switch (groupName)
            {
                case "Application":
                    targetType = typeof (Application);
                    break;
                case "SystemInfo":
                    targetType = typeof (SystemInfo);
                    break;
                case "Environment":
                    targetType = typeof (Environment);
                    break;
                case "Config":
                    targetType = typeof (AssetInventorySettings);
                    targetInstance = AI.Config; // Get the singleton instance
                    break;
                case "DateTime":
                    targetType = typeof (DateTime);
                    break;
                case "PlayerSettings":
                    targetType = typeof (PlayerSettings);
                    break;
                case "EditorApplication":
                    targetType = typeof (EditorApplication);
                    break;
                case "BuildTarget":
                    targetType = typeof (EditorUserBuildSettings);
                    break;
                case "QualitySettings":
                    targetType = typeof (QualitySettings);
                    break;
                case "Screen":
                    targetType = typeof (Screen);
                    break;
                default:
                    throw new Exception($"Unknown internal variable group: '{groupName}'. Valid groups are: Application, SystemInfo, Environment, Config, DateTime, PlayerSettings, EditorApplication, BuildTarget, QualitySettings, Screen");
            }

            // Navigate through the property/field path
            object currentValue = targetInstance;
            Type currentType = targetType;

            for (int i = 1; i < parts.Length; i++)
            {
                string memberName = parts[i];

                // Try to get as property first
                PropertyInfo property = currentType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

                if (property != null)
                {
                    try
                    {
                        currentValue = property.GetValue(currentValue, null);

                        if (currentValue == null)
                        {
                            // Property exists but returned null
                            return string.Empty;
                        }

                        currentType = currentValue.GetType();
                    }
                    catch (Exception e)
                    {
                        string memberPath = string.Join(".", parts, 0, i + 1);
                        throw new Exception($"Cannot access property '{memberPath}': {e.Message}", e);
                    }
                }
                else
                {
                    // Try to get as field if property not found
                    FieldInfo field = currentType.GetField(memberName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);

                    if (field == null)
                    {
                        throw new Exception($"Property or field '{memberName}' not found on type '{currentType.Name}'");
                    }

                    try
                    {
                        currentValue = field.GetValue(currentValue);

                        if (currentValue == null)
                        {
                            // Field exists but returned null
                            return string.Empty;
                        }

                        currentType = currentValue.GetType();
                    }
                    catch (Exception e)
                    {
                        string memberPath = string.Join(".", parts, 0, i + 1);
                        throw new Exception($"Cannot access field '{memberPath}': {e.Message}", e);
                    }
                }
            }

            return currentValue?.ToString() ?? string.Empty;
        }
    }
}