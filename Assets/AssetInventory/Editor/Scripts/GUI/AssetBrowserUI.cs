using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public sealed class AssetBrowserUI : IndexUI
    {
#if !ASSET_INVENTORY_HIDE_BROWSER
        [MenuItem("Assets/Asset Inventory (Browser Only)", priority = 9001)]
#endif
        public static void ShowBrowser()
        {
            AssetBrowserUI window = GetWindow<AssetBrowserUI>("Asset Browser");
            window.minSize = new Vector2(650, 300);
            window.hideMainNavigation = true;
            window.workspaceMode = true;
        }
    }
}