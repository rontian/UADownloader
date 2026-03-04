// reference for built-in icons: https://github.com/halak/unity-editor-icons
// new version: https://github.com/Doppelkeks/Unity-Editor-Icons/tree/2019.4

using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    public static class UIStyles
    {
        public enum TagStyle
        {
            Add = 0,
            Remove = 1,
            Neutral = 2,
            ColorSelect = 3
        }

        public const int BORDER_WIDTH = 30;
        public const int INSPECTOR_WIDTH = 300;
        public const int TAG_SIZE_SPACING = 20;
        public const int TAG_OUTER_MARGIN = 20;
        public const string INDENT = "  ";
        public const int INDENT_WIDTH = 8;
        public const float BIG_BUTTON_HEIGHT = 30f;

        public static readonly string[] FolderTypes = {"Unity Packages", "Media Folder", "Archives", "Dev Packages"};
        public static readonly string[] MediaTypes = {"-All Media-", "-All Files-", string.Empty, "Audio", "Images", "Models", string.Empty, "-Custom File Pattern-"};

        public static readonly Color errorColor = EditorGUIUtility.isProSkin ? new Color(1f, 0.5f, 0.5f) : Color.red;

        private static readonly GUIContent GUIText = new GUIContent();
        private static readonly GUIContent GUIImage = new GUIContent();
        private static readonly GUIContent GUITextImage = new GUIContent();

        private const int ENTRY_FONT_SIZE = 11;
        private const int ENTRY_FIXED_HEIGHT = ENTRY_FONT_SIZE + 7;
        private const int TOGGLE_FIXED_WIDTH = 10;

        private static GUIStyle _searchTile;
        public static GUIStyle searchTile
        {
            get { return _searchTile ?? (_searchTile = CreateTileStyle()); }
        }

        private static GUIStyle _packageTile;
        public static GUIStyle packageTile
        {
            get { return _packageTile ?? (_packageTile = CreateTileStyle()); }
        }

        private static GUIStyle _selectedSearchTile;
        public static GUIStyle selectedSearchTile
        {
            get { return _selectedSearchTile ?? (_selectedSearchTile = CreateSelectedTileStyle()); }
        }

        private static GUIStyle _selectedPackageTile;
        public static GUIStyle selectedPackageTile
        {
            get { return _selectedPackageTile ?? (_selectedPackageTile = CreateSelectedTileStyle()); }
        }

        private static GUIStyle _toggleButtonStyleNormal;
        public static GUIStyle toggleButtonStyleNormal
        {
            get { return _toggleButtonStyleNormal ?? (_toggleButtonStyleNormal = new GUIStyle("button")); }
        }
        private static GUIStyle _selectableLabel;
        public static GUIStyle selectableLabel
        {
            get
            {
                if (_selectableLabel == null)
                {
                    _selectableLabel = new GUIStyle(EditorStyles.label)
                    {
                        wordWrap = true, margin = new RectOffset(0, 0, 0, 0), padding = new RectOffset(0, 0, 0, 0)
                    };
                }
                return _selectableLabel;
            }
        }
        private static GUIStyle _toggleButtonStyleToggled;
        public static GUIStyle toggleButtonStyleToggled
        {
            get { return _toggleButtonStyleToggled ?? (_toggleButtonStyleToggled = CreateToggledStyle()); }
        }

        private static GUIStyle _wrappedLinkLabel;
        public static GUIStyle wrappedLinkLabel
        {
            get
            {
                if (_wrappedLinkLabel == null)
                {
                    _wrappedLinkLabel = new GUIStyle(EditorStyles.linkLabel)
                    {
                        wordWrap = true
                    };
                }
                return _wrappedLinkLabel;
            }
        }

        private static GUIStyle _greyMiniLabel;
        public static GUIStyle greyMiniLabel
        {
            get
            {
                if (_greyMiniLabel == null)
                {
                    _greyMiniLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleLeft
                    };
                }
                return _greyMiniLabel;
            }
        }

        private static GUIStyle _wrappedButton;
        public static GUIStyle wrappedButton
        {
            get
            {
                if (_wrappedButton == null)
                {
                    _wrappedButton = new GUIStyle(GUI.skin.button) {wordWrap = true};
                }
                return _wrappedButton;
            }
        }

        public static readonly GUIContent emptyTileContent = new GUIContent();
        public static readonly GUIContent selectedTileContent = new GUIContent
        {
            image = LoadTexture("asset-inventory-selected"),
            text = string.Empty,
            tooltip = string.Empty
        };
        private static GUIStyle _richText;
        public static GUIStyle richText
        {
            get
            {
                if (_richText == null)
                {
                    _richText = new GUIStyle(EditorStyles.wordWrappedLabel) {richText = true};
                }
                return _richText;
            }
        }
        private static GUIStyle _miniLabelRight;
        public static GUIStyle miniLabelRight
        {
            get { return _miniLabelRight ?? (_miniLabelRight = new GUIStyle(EditorStyles.miniLabel) {alignment = TextAnchor.MiddleRight}); }
        }
        private static readonly Func<Rect> getVisibleRect;

        static UIStyles()
        {
            // cache the visible rect getter for performance
            Type clipType = typeof (GUI).Assembly.GetType("UnityEngine.GUIClip");
            PropertyInfo prop = clipType.GetProperty("visibleRect", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo getter = prop.GetGetMethod(true);
            getVisibleRect = (Func<Rect>)Delegate.CreateDelegate(typeof (Func<Rect>), getter);
        }

        public static Rect GetCurrentVisibleRect() => getVisibleRect();

        private static GUIStyle _mainButton;
        private static Texture2D _bg;
        private static Texture2D _bgHover;
        private static Texture2D _bgActive;

        public static GUIStyle mainButton
        {
            get
            {
                if (_mainButton == null) CreateMainButton();
                return _mainButton;
            }
        }

        private static void CreateMainButton()
        {
            Color baseCol = EditorGUIUtility.isProSkin ? new Color(0.20f, 0.48f, 0.95f) : new Color(0.16f, 0.44f, 0.93f);
            Color hoverCol = Color.Lerp(baseCol, Color.white, 0.15f);
            Color activeCol = Color.Lerp(baseCol, Color.black, 0.20f);

            _bg = MakeTex(baseCol);
            _bgHover = MakeTex(hoverCol);
            _bgActive = MakeTex(activeCol);

            GUIStyle s = new GUIStyle();
            s.normal.background = _bg;
            s.normal.textColor = Color.white;
            s.hover.background = _bgHover;
            s.hover.textColor = Color.white;
            s.active.background = _bgActive;
            s.active.textColor = Color.white;
            s.focused.background = _bg;
            s.focused.textColor = Color.white;
            s.onNormal.background = _bg;
            s.onNormal.textColor = Color.white;
            s.onHover.background = _bgHover;
            s.onHover.textColor = Color.white;
            s.onActive.background = _bgActive;
            s.onActive.textColor = Color.white;
            s.onFocused.background = _bg;
            s.onFocused.textColor = Color.white;
            s.border = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(4, 4, 3, 4);
            s.padding = new RectOffset(3, 3, 1, 2);
            s.contentOffset = Vector2.zero;
            s.alignment = TextAnchor.MiddleCenter;
            s.fontStyle = FontStyle.Normal;
            s.imagePosition = ImagePosition.TextOnly;
            s.clipping = TextClipping.Clip;

            _mainButton = s;
        }

        private static Texture2D MakeTex(Color c)
        {
            Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Point;
            t.SetPixel(0, 0, c);
            t.Apply(false, true);

            return t;
        }

        public static bool MainButton(ref bool mainUsed, string text, params GUILayoutOption[] options)
        {
            return MainButton(ref mainUsed, Content(text), options);
        }

        public static bool MainButton(ref bool mainUsed, GUIContent content, params GUILayoutOption[] options)
        {
            bool result;
            if (mainUsed)
            {
                result = GUILayout.Button(content, options);
            }
            else
            {
                result = GUILayout.Button(content, mainButton, options);
            }
            mainUsed = true;

            return result;
        }

        public static Texture2D LoadTexture(string name)
        {
            string asset = AssetDatabase.FindAssets("t:Texture2d " + name).FirstOrDefault();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(asset));
        }

        /*
        public static void SelectableLabel(GUIContent content, params GUILayoutOption[] options)
        {
            // This method is really buggy in Unity, the label will remain on screen and only change when defocusing etc.
            // Wrapping is completely broken
            EditorGUILayout.SelectableLabel(content.text, EditorStyles.wordWrappedLabel, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true), GUILayout.MinHeight(EditorGUIUtility.singleLineHeight), GUILayout.MaxHeight(EditorGUIUtility.singleLineHeight * 5));

            //Rect position = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, selectableLabel, options);
            //EditorGUI.SelectableLabel(position, content.tooltip, selectableLabel);

            if (!string.IsNullOrEmpty(content.tooltip))
            {
                GUI.Label(GUILayoutUtility.GetLastRect(), new GUIContent("", content.tooltip), GUIStyle.none);
            }
        }
        */

        private static GUIStyle CreateToggledStyle()
        {
            GUIStyle baseStyle = new GUIStyle("button");
            baseStyle.normal.background = baseStyle.active.background;

            return baseStyle;
        }

        private static GUIStyle CreateTileStyle()
        {
            GUIStyle baseStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                imagePosition = ImagePosition.ImageAbove,
                wordWrap = true,
                margin = new RectOffset(AI.Config.tileMargin, AI.Config.tileMargin, AI.Config.tileMargin, AI.Config.tileMargin)
            };

            return baseStyle;
        }

        private static GUIStyle CreateSelectedTileStyle()
        {
            GUIStyle baseStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                imagePosition = ImagePosition.ImageOnly,
                overflow = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };

            baseStyle.normal.background = LoadTexture("asset-inventory-transparent");

            return baseStyle;
        }

        private static GUIStyle _tag;
        public static GUIStyle tag
        {
            get
            {
                if (_tag == null)
                {
                    _tag = new GUIStyle(EditorStyles.miniButton)
                    {
                        border = new RectOffset(6, 6, 6, 6),
                        fixedHeight = EditorGUIUtility.singleLineHeight + 2,
                        alignment = TextAnchor.MiddleCenter,
                        margin = new RectOffset(2, 2, 2, 2)
                    };
                }
                return _tag;
            }
        }

        private static GUIStyle _entryStyle;
        public static GUIStyle entryStyle
        {
            get { return _entryStyle ?? (_entryStyle = new GUIStyle(EditorStyles.miniLabel) {fontSize = ENTRY_FONT_SIZE, fixedHeight = ENTRY_FIXED_HEIGHT}); }
        }
        private static GUIStyle _toggleStyle;
        public static GUIStyle toggleStyle
        {
            get { return _toggleStyle ?? (_toggleStyle = new GUIStyle(EditorStyles.toggle) {fixedWidth = TOGGLE_FIXED_WIDTH, fixedHeight = ENTRY_FIXED_HEIGHT}); }
        }
        private static GUIStyle _whiteCenter;
        public static GUIStyle whiteCenter
        {
            get { return _whiteCenter ?? (_whiteCenter = new GUIStyle {alignment = TextAnchor.MiddleCenter, normal = new GUIStyleState {textColor = Color.white}}); }
        }
        private static GUIStyle _blackCenter;
        public static GUIStyle blackCenter
        {
            get { return _blackCenter ?? (_blackCenter = new GUIStyle {alignment = TextAnchor.MiddleCenter, normal = new GUIStyleState {textColor = Color.black}}); }
        }
        private static GUIStyle _centerLabel;
        public static GUIStyle centerLabel
        {
            get { return _centerLabel ?? (_centerLabel = new GUIStyle(GUI.skin.label) {alignment = TextAnchor.MiddleCenter}); }
        }

        private static GUIStyle _centerHeading;
        public static GUIStyle centerHeading
        {
            get { return _centerHeading ?? (_centerHeading = new GUIStyle(GUI.skin.label) {alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold}); }
        }
        private static GUIStyle _centeredWhiteMiniLabel;
        public static GUIStyle centeredWhiteMiniLabel
        {
            get { return _centeredWhiteMiniLabel ?? (_centeredWhiteMiniLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel) {normal = new GUIStyleState {textColor = Color.white}}); }
        }

        private static GUIStyle _centeredGreyWrappedMiniLabel;
        public static GUIStyle centeredGreyWrappedMiniLabel
        {
            get { return _centeredGreyWrappedMiniLabel ?? (_centeredGreyWrappedMiniLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel) {wordWrap = true}); }
        }

        private static GUIStyle _rightLabel;
        public static GUIStyle rightLabel
        {
            get { return _rightLabel ?? (_rightLabel = new GUIStyle(GUI.skin.label) {alignment = TextAnchor.MiddleRight}); }
        }

        private static GUIStyle _centerLinkLabel;
        public static GUIStyle centerLinkLabel
        {
            get { return _centerLinkLabel ?? (_centerLinkLabel = new GUIStyle(EditorStyles.linkLabel) {alignment = TextAnchor.MiddleCenter}); }
        }

        private static GUIStyle _centerPopup;
        public static GUIStyle centerPopup
        {
            get { return _centerPopup ?? (_centerPopup = new GUIStyle(EditorStyles.popup) {alignment = TextAnchor.MiddleCenter}); }
        }

        public static void DrawTag(TagInfo tagInfo, Action action = null)
        {
            DrawTag(tagInfo.Name, tagInfo.GetColor(), action, TagStyle.Remove);
        }

        public static void DrawTag(string name, Color color, Action action, TagStyle style)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            using (new EditorGUILayout.HorizontalScope(tag,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false),
                GUILayout.Width(tag.CalcSize(Content(name)).x + (style != TagStyle.Neutral ? EditorGUIUtility.singleLineHeight : 0))))
            {
                GUI.color = GetHSPColor(color);
                GUIStyle readableText = ReadableText(color);

                GUI.color = EditorGUIUtility.isProSkin ? Color.white : Color.black;
                readableText.normal.textColor = GUI.color;

                switch (style)
                {
                    case TagStyle.Add:
                        if (GUILayout.Button("+ " + name, readableText, GUILayout.Height(EditorGUIUtility.singleLineHeight - 3)))
                        {
                            action?.Invoke();
                        }
                        break;

                    case TagStyle.Remove:
                        GUILayout.Label(name, readableText);
                        GUI.color = oldColor;
                        if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Remove Tag").image,
                            EditorStyles.label, GUILayout.Width(EditorGUIUtility.singleLineHeight),
                            GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                        {
                            action?.Invoke();
                        }
                        break;

                    case TagStyle.Neutral:
                        GUILayout.Label(name, readableText);
                        break;

                    case TagStyle.ColorSelect:
                        GUILayout.Label(name, readableText);
                        if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "|Remove Tag").image,
                            EditorStyles.label, GUILayout.Width(EditorGUIUtility.singleLineHeight),
                            GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                        {
                            action?.Invoke();
                        }
                        break;
                }
            }
            GUI.color = oldColor;
        }

        public static void DrawTag(Rect rect, string name, Color color, TagStyle style)
        {
            Color oldColor = GUI.color;
            GUI.color = color;
            // FIXME: broken, background not at correct position yet
            using (new EditorGUILayout.HorizontalScope(tag,
                GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false),
                GUILayout.Width(tag.CalcSize(Content(name)).x + (style != TagStyle.Neutral ? EditorGUIUtility.singleLineHeight : 0))))
            {
                GUI.color = GetHSPColor(color);
                switch (style)
                {
                    case TagStyle.Neutral:
                        GUI.Label(rect, name, ReadableText(color));
                        break;
                }
            }
            GUI.color = oldColor;
        }

        public static void DrawUILine(Color color, int thickness = 2, int padding = 10)
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
            r.height = thickness;
            r.y += padding / 2f;
            r.x -= 2;
            r.width += 6;
            EditorGUI.DrawRect(r, color);
        }

        public static GUILayoutOption GetLabelMaxWidth()
        {
            return GUILayout.MaxWidth(INSPECTOR_WIDTH - 115);
        }

        private static Color GetHSPColor(Color color)
        {
            // http://alienryderflex.com/hsp.html
            return 0.299 * color.r + 0.587 * color.g + 0.114 * color.b < 0.5f ? Color.white : new Color(0.1f, 0.1f, 0.1f);
        }

        public static GUIStyle ReadableText(Color color, bool wrap = false)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = GetHSPColor(color);
            style.wordWrap = wrap;
            return style;
        }

        public static GUIStyle ColoredText(Color color, bool wrapped = false)
        {
            GUIStyle style = new GUIStyle(wrapped ? EditorStyles.wordWrappedLabel : EditorStyles.label);
            style.normal.textColor = color;
            return style;
        }

        public static void DrawProgressBar(float percentage, string text, params GUILayoutOption[] options)
        {
            Rect r = EditorGUILayout.BeginVertical(options);
            EditorGUI.ProgressBar(r, percentage, text);
            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            EditorGUILayout.EndVertical();
        }

        public static GUIContent Content(string text)
        {
            GUIText.image = null;
            GUIText.text = text;
            GUIText.tooltip = null;
            return GUIText;
        }

        public static GUIContent Content(string text, string tip, string ctrlText = null, string ctrlTip = null)
        {
            GUIText.image = null;
            GUIText.text = AI.ShowAdvanced() ? (string.IsNullOrEmpty(ctrlText) ? text : ctrlText) : text;
            GUIText.tooltip = AI.ShowAdvanced() ? (string.IsNullOrEmpty(ctrlTip) ? tip : ctrlTip) : tip;
            return GUIText;
        }

        public static GUIContent Content(Texture texture)
        {
            GUIImage.image = texture;
            GUIImage.text = null;
            GUIImage.tooltip = null;
            return GUIImage;
        }

        public static GUIContent Content(string text, Texture texture, string tip = null)
        {
            GUITextImage.image = texture;
            GUITextImage.text = " " + text; // otherwise text too close to image
            GUITextImage.tooltip = tip;
            return GUITextImage;
        }

        public static GUIContent IconContent(string name, string darkName, string tooltip = null)
        {
            if (EditorGUIUtility.isProSkin) return EditorGUIUtility.IconContent(darkName, tooltip);
            return EditorGUIUtility.IconContent(name, tooltip);
        }
    }
}
