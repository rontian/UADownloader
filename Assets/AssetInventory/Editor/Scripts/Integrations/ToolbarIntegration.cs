#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
using UnityEngine;

namespace AssetInventory
{
    public static class ToolbarIntegration
    {
        private static Texture2D _icon32;
        private static Texture2D ToolbarIcon
        {
            get
            {
                if (_icon32 == null) _icon32 = UIStyles.LoadTexture("asset-inventory-icon32");
                return _icon32;
            }
        }

        [MainToolbarElement("Asset Inventory/Add To Scene", defaultDockPosition = MainToolbarDockPosition.Left)]
        public static MainToolbarElement AddAssetToSceneButton()
        {
            MainToolbarContent content = new MainToolbarContent(ToolbarIcon)
            {
                text = "Add To Scene",
                tooltip = "Open Asset Inventory to pick an asset and add it to the scene"
            };

            return new MainToolbarButton(content, OnButtonClicked);
        }

        private static void OnButtonClicked()
        {
            ResultPickerUI.Show(OnAssetPicked, "Prefabs");
        }

        private static void OnAssetPicked(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath)) return;

            AssetUtils.AddToScene(projectPath);
        }
    }
}
#endif
