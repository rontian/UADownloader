using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetInventory
{
    [Serializable]
    public sealed class UISection
    {
        public string name;
        public List<string> sections = new List<string>();

        public UISection()
        {
        }

        public bool IsFirst(string key)
        {
            return sections[0] == key;
        }

        public bool IsLast(string key)
        {
            return sections.Last() == key;
        }

        public void MoveUp(string key)
        {
            int index = sections.IndexOf(key);
            if (index > 0)
            {
                sections.RemoveAt(index);
                sections.Insert(index - 1, key);
            }
        }

        public void MoveDown(string key)
        {
            int index = sections.IndexOf(key);
            if (index < sections.Count - 1)
            {
                sections.RemoveAt(index);
                sections.Insert(index + 1, key);
            }
        }

        public override string ToString()
        {
            return $"UI Section '{name}'";
        }
    }
}