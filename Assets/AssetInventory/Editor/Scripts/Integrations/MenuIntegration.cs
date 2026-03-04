using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public class MenuIntegration: EditorWindow
    {
#if !ASSET_INVENTORY_HIDE_AI
        [MenuItem("Assets/Asset Inventory", priority = 9000)]
#endif
#if UNITY_6000_1_OR_NEWER
        [MenuItem("Window/Package Management/Asset Inventory")]
#else
        [MenuItem("Window/Asset Inventory", priority = 1500)]
#endif
        public static void ShowWindow()
        {
            IndexUI window = GetWindow<IndexUI>("Asset Inventory");
            window.minSize = new Vector2(650, 300);
        }
    }
}
