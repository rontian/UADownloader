using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SQLite;

namespace AssetInventory
{
    [Serializable]
    public sealed class CustomActionStep
    {
        [PrimaryKey, AutoIncrement] public int Id { get; set; }
        public int ActionId { get; set; }
        public string Key { get; set; }
        public int OrderIdx { get; set; }
        public string Params { get; set; }

        // Runtime
        [Ignore] public ActionStep StepDef { get; set; }
        [Ignore] public List<ParameterValue> Values { get; set; } = new List<ParameterValue>();

        public CustomActionStep()
        {
        }

        public void ResolveValues()
        {
            StepDef = AI.Actions.ActionSteps.FirstOrDefault(s => s.Key == Key);
            if (Params != null)
            {
                Values = JsonConvert.DeserializeObject<List<ParameterValue>>(Params);
            }
            if (Values == null) Values = new List<ParameterValue>();
        }

        public void PersistValues()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.None
            };
            Params = JsonConvert.SerializeObject(Values, settings);
        }

        public override string ToString()
        {
            return $"Action Step '{Key}'";
        }
    }
}
