using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetInventory
{
    [Serializable]
    public abstract class ActionStep
    {
        public enum ActionCategory
        {
            FilesAndFolders,
            Importing,
            Actions,
            Settings,
            Misc
        }

        public string Key { get; protected set; }
        public string Name { get; protected set; }
        public string Description { get; protected set; }
        public ActionCategory Category { get; protected set; } = ActionCategory.Misc;
        public bool InterruptsExecution { get; protected set; }
        public List<StepParameter> Parameters { get; protected set; } = new List<StepParameter>();

        public abstract Task Run(List<ParameterValue> parameters);
        public virtual StepParameter.ParamType GetParamType(StepParameter param, List<ParameterValue> parameters)
        {
            return param.Type;
        }
        public virtual StepParameter.ValueType GetParamValueList(StepParameter param, List<ParameterValue> parameters)
        {
            return param.ValueList;
        }
        public virtual List<Tuple<string, ParameterValue>> GetParamOptions(StepParameter param, List<ParameterValue> parameters)
        {
            return param.Options;
        }
    }

    [Serializable]
    public class StepParameter
    {
        public enum ParamType
        {
            String,
            MultilineString,
            Int,
            Bool,
            Dynamic // determined depending on other parameters 
        }

        public enum ValueType
        {
            None,
            Custom,
            Folder,
            Package
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public ParamType Type { get; set; } = ParamType.String;
        public ParameterValue DefaultValue { get; set; } = new ParameterValue();
        public ValueType ValueList { get; set; } = ValueType.None;
        public List<Tuple<string, ParameterValue>> Options { get; set; }
        public bool Optional { get; set; }
    }

    [Serializable]
    public class ParameterValue
    {
        public string stringValue;
        public int intValue;
        public bool boolValue;

        public ParameterValue() { }

        public ParameterValue(ParameterValue copyFrom)
        {
            stringValue = copyFrom.stringValue;
            intValue = copyFrom.intValue;
            boolValue = copyFrom.boolValue;
        }

        public ParameterValue(string stringValue)
        {
            this.stringValue = stringValue;
        }

        public ParameterValue(int intValue)
        {
            this.intValue = intValue;
        }

        public ParameterValue(bool boolValue)
        {
            this.boolValue = boolValue;
        }
    }
}