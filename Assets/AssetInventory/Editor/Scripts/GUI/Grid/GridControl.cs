using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetInventory
{
    internal sealed class GridControl
    {
        public event Action<AssetInfo> OnDoubleClick;
        public event Action<int> OnKeyboardSelection;
        public event Action<GenericMenu, IReadOnlyList<AssetInfo>, int> OnContextMenuPopulate;

        public IEnumerable<AssetInfo> packages;
        private GUIContent[] _contents;
        public GUIContent[] contents
        {
            get => _contents;
            set
            {
                if (_contents != null)
                {
                    foreach (GUIContent content in _contents)
                    {
                        if (content != null && content.image != null)
                        {
                            // Skip built-in Unity icons which shouldn't be destroyed
                            if (!AssetDatabase.GetAssetPath(content.image).StartsWith("Library/"))
                            {
                                UnityEngine.Object.DestroyImmediate(content.image);
                            }
                        }
                    }
                }
                _contents = value;
            }
        }
        public List<AssetInfo> selectionItems;
        public int selectionCount;
        public int selectionTile;
        public int selectionPackageCount;
        public long selectionSize;

        public int noTextBelow;
        public bool enlargeTiles;
        public bool centerTiles;
        public bool onlySingleSelection;

        public bool IsMouseOverGrid;

        // Keyboard navigation properties
        private bool HasKeyboardFocus { get; set; }
        private int _cellsPerRow = 1;
        private int _totalRows = 1;
        private int _currentRow;
        private int _currentCol;
        private bool _keyboardNavigationOccurred;
        private float _actualTileWidth;
        private float _actualTileHeight;
        private float _leftOffset;

        private GUIContent[] Selection
        {
            get
            {
                if (_selection == null || (contents != null && _selection.Length != contents.Length))
                {
                    _selection = new GUIContent[contents != null ? contents.Length : 0];
                    MarkGridSelection(true);
                }
                return _selection;
            }
        }
        private Func<AssetInfo, string> _textGenerator;
        private GUIContent[] _selection;
        private int _selectionMin;
        private int _selectionMax;
        private int _lastSelectionTile;
        private List<AssetInfo> _allPackages;
        private Action _bulkHandler;
        private Rect _lastRect;

        public void Init(List<AssetInfo> allPackages, IEnumerable<AssetInfo> visiblePackages, Action bulkHandler, Func<AssetInfo, string> textGenerator = null)
        {
            packages = visiblePackages;
            _textGenerator = textGenerator;
            _allPackages = allPackages;
            _bulkHandler = bulkHandler;
            _selection = new GUIContent[contents.Length];
            MarkGridSelection(true);
            CalculateBulkSelection();
        }

        public void Draw(float width, int inspectorCount, int tileSize, float tileAspectRatio, GUIStyle tileStyle, GUIStyle selectedTileStyle)
        {
            float actualWidth = width - UIStyles.INSPECTOR_WIDTH * inspectorCount - UIStyles.BORDER_WIDTH;
            int cells = Mathf.Clamp(Mathf.FloorToInt(actualWidth / (tileSize + AI.Config.tileMargin)), 1, 99);
            if (cells < 2) cells = 2;

            // Store cells per row for keyboard navigation
            _cellsPerRow = cells;

            if (enlargeTiles)
            {
                // enlarge tiles dynamically so they take the full width
                tileSize = Mathf.FloorToInt((actualWidth - cells * AI.Config.tileMargin - 2 * AI.Config.tileMargin) / cells);
            }

            tileStyle.fixedHeight = tileSize / tileAspectRatio;
            tileStyle.fixedWidth = tileSize;
            tileStyle.margin = new RectOffset(AI.Config.tileMargin, AI.Config.tileMargin, AI.Config.tileMargin, AI.Config.tileMargin); // set again due to initial style only being set once so changes would not reflect
            selectedTileStyle.fixedHeight = tileStyle.fixedHeight + tileStyle.margin.top;
            selectedTileStyle.fixedWidth = tileStyle.fixedWidth + tileStyle.margin.left;

            // Save the actual tile width/height for scroll calculations (includes padding and margins)
            _actualTileWidth = tileStyle.fixedWidth + AI.Config.tileMargin;
            _actualTileHeight = tileStyle.fixedHeight + AI.Config.tileMargin;

            if (_textGenerator != null && contents != null && contents.Length > 0)
            {
                // remove text if tiles are too small
                if (tileSize < noTextBelow)
                {
                    if (!string.IsNullOrEmpty(contents[0].text))
                    {
                        contents.ForEach(c => c.text = string.Empty);
                    }
                }

                // create text on-demand if tiles are big enough
                if (tileSize >= noTextBelow)
                {
                    if (string.IsNullOrEmpty(contents[0].text))
                    {
                        for (int i = 0; i < contents.Length; i++)
                        {
                            contents[i].text = _textGenerator(packages.ElementAt(i));
                        }
                    }
                }
            }

            // Pre-handle right-click before SelectionGrid can consume the event
            _leftOffset = centerTiles ? (actualWidth - tileSize * cells) / 2f : 0f;
            HandleContextMenuPreGrid(cells);

            GUILayout.BeginHorizontal();
            if (centerTiles) GUILayout.Space(_leftOffset);
            selectionTile = GUILayout.SelectionGrid(selectionTile, contents, cells, tileStyle);

            _lastRect = UIStyles.GetCurrentVisibleRect(); // GetLastRect would include invisible scroll area as well
            if (Event.current.type == EventType.Repaint)
            {
                IsMouseOverGrid = _lastRect.Contains(Event.current.mousePosition);
            }

            if (selectionCount > 1)
            {
                // draw selection on top if there are more than one selected, otherwise don't for performance
                // use real last rect to support scrolling
                GUI.SelectionGrid(GUILayoutUtility.GetLastRect(), selectionTile, Selection, cells, selectedTileStyle);
            }
            GUILayout.EndHorizontal();

            if (Event.current.type == EventType.Layout)
            {
                // handle double-clicks
                if (Event.current.clickCount > 1)
                {
                    if (IsMouseOverGrid) OnDoubleClick?.Invoke(packages.ElementAt(selectionTile));
                }
            }

            // Post-grid context menu handling no longer needed; pre-grid handler covers it

            // Update keyboard focus state and grid layout info
            UpdateKeyboardFocus();
            UpdateGridLayoutInfo();

            // Handle keyboard commands for navigation
            HandleKeyboardCommands();
        }

        private void HandleContextMenuPreGrid(int cells)
        {
            Event evt = Event.current;
            if (evt == null) return;
            if (contents == null || contents.Length == 0) return;

            // Intercept right mouse down or ContextClick before SelectionGrid handles it
            if ((evt.type == EventType.MouseDown && evt.button == 1) || evt.type == EventType.ContextClick)
            {
                if (IsMouseOverGrid)
                {
                    int clickedIndex = selectionTile;
                    if (TryGetIndexUnderMouse(cells, out int idx)) clickedIndex = idx;

                    bool clickedIsSelected = clickedIndex >= 0 && clickedIndex < Selection.Length && Selection[clickedIndex] == UIStyles.selectedTileContent;
                    if (!clickedIsSelected && clickedIndex >= 0 && clickedIndex < contents.Length)
                    {
                        selectionTile = clickedIndex;
                        MarkGridSelection(true);
                        _selectionMin = selectionTile;
                        _selectionMax = selectionTile;
                        _lastSelectionTile = selectionTile;
                        CalculateBulkSelection();
                    }

                    if (OnContextMenuPopulate != null)
                    {
                        GenericMenu menu = new GenericMenu();
                        OnContextMenuPopulate.Invoke(menu, selectionItems, clickedIndex);
                        menu.ShowAsContext();
                        evt.Use();
                    }
                }
            }
        }

        private bool TryGetIndexUnderMouse(int cells, out int index)
        {
            index = -1;
            if (_lastRect.width <= 0f || _actualTileHeight <= 0f) return false;

            Vector2 mouse = Event.current.mousePosition;
            float localX = mouse.x - _lastRect.x - _leftOffset;
            float localY = mouse.y - _lastRect.y;

            if (localX < 0f || localY < 0f) return false;

            int col = Mathf.FloorToInt(localX / _actualTileWidth);
            int row = Mathf.FloorToInt(localY / _actualTileHeight);

            if (col < 0 || col >= cells || row < 0) return false;

            int idx = row * cells + col;
            if (idx < 0 || contents == null || idx >= contents.Length) return false;

            index = idx;
            return true;
        }

        private void UpdateKeyboardFocus()
        {
            // Check if the grid has keyboard focus
            bool isTextEditing = EditorGUIUtility.editingTextField;
            HasKeyboardFocus = !isTextEditing;
        }

        private void UpdateGridLayoutInfo()
        {
            // Calculate total rows for navigation
            if (contents != null && contents.Length > 0)
            {
                _totalRows = Mathf.CeilToInt((float)contents.Length / _cellsPerRow);
                _currentRow = selectionTile / _cellsPerRow;
                _currentCol = selectionTile % _cellsPerRow;
            }
        }

        public void LimitSelection(int count)
        {
            if (selectionTile >= count) selectionTile = 0;
        }

        private void MarkGridSelection(bool clearSelection)
        {
            if (clearSelection) Selection.Populate(UIStyles.emptyTileContent);

            if (selectionTile >= Selection.Length) selectionTile = 0;
            if (selectionTile >= 0 && Selection.Length > 0) Selection[selectionTile] = UIStyles.selectedTileContent;
        }

        public void HandleMouseClicks()
        {
            if (selectionTile >= 0)
            {
                // Remove focus from any active text fields to enable keyboard control
                GUI.FocusControl("");

                if (onlySingleSelection || (!Event.current.control && !Event.current.shift))
                {
                    // regular click, no ctrl/shift
                    MarkGridSelection(true);

                    _selectionMin = selectionTile;
                    _selectionMax = selectionTile;
                }
                else if (Event.current.control)
                {
                    // toggle existing selection
                    Selection[selectionTile] = Selection[selectionTile] == UIStyles.selectedTileContent ? UIStyles.emptyTileContent : UIStyles.selectedTileContent;
                }
                else if (Event.current.shift)
                {
                    // shift click - add all between clicks
                    if (_selectionMin != -1 && _selectionMax != -1 && selectionTile >= _selectionMin && selectionTile <= _selectionMax)
                    {
                        Selection.Populate(UIStyles.emptyTileContent);
                        _selectionMin = Mathf.Min(_lastSelectionTile, selectionTile);
                        _selectionMax = Mathf.Max(_lastSelectionTile, selectionTile);
                    }
                    int minI = Mathf.Min(_lastSelectionTile, selectionTile);
                    int maxI = Mathf.Max(_lastSelectionTile, selectionTile);
                    if (minI < 0) minI = 0;
                    if (maxI < 0) maxI = 0;

                    if (Selection.Length > 0)
                    {
                        for (int i = minI; i <= maxI; i++)
                        {
                            Selection[i] = UIStyles.selectedTileContent;
                        }
                    }
                }

                _selectionMin = Mathf.Min(_selectionMin, selectionTile);
                _selectionMax = Mathf.Max(_selectionMax, selectionTile);
                _lastSelectionTile = selectionTile;

                CalculateBulkSelection();
            }
        }

        public void HandleKeyboardCommands()
        {
            if (!HasKeyboardFocus || contents == null || contents.Length == 0) return;

            // Only process KeyDown events, not KeyUp or other event types
            if (Event.current.type != EventType.KeyDown) return;

            // Handle Ctrl+A for select all
            if (!onlySingleSelection && Event.current.modifiers == EventModifiers.Control && Event.current.keyCode == KeyCode.A)
            {
                // select all
                MarkGridSelection(true);

                _selectionMin = 0;
                _selectionMax = selectionCount - 1;

                CalculateBulkSelection();
                return;
            }

            bool handled = false;
            switch (Event.current.keyCode)
            {
                case KeyCode.LeftArrow:
                    NavigateLeft();
                    handled = true;
                    break;

                case KeyCode.RightArrow:
                    NavigateRight();
                    handled = true;
                    break;

                case KeyCode.UpArrow:
                    NavigateUp();
                    handled = true;
                    break;

                case KeyCode.DownArrow:
                    NavigateDown();
                    handled = true;
                    break;

                case KeyCode.Home:
                    NavigateToStart();
                    handled = true;
                    break;

                case KeyCode.End:
                    NavigateToEnd();
                    handled = true;
                    break;

                case KeyCode.PageUp:
                    NavigatePageUp();
                    handled = true;
                    break;

                case KeyCode.PageDown:
                    NavigatePageDown();
                    handled = true;
                    break;
            }

            if (handled)
            {
                // Use the event since we're only processing keyboard events now
                Event.current.Use();

                // Mark that keyboard navigation occurred
                _keyboardNavigationOccurred = true;

                // Ensure row/col values are consistent with selectionTile
                _currentRow = selectionTile / _cellsPerRow;
                _currentCol = selectionTile % _cellsPerRow;

                // Ensure selectionTile is within bounds
                selectionTile = Mathf.Clamp(selectionTile, 0, contents.Length - 1);

                MarkGridSelection(true);
                CalculateBulkSelection();

                // Trigger selection change event for keyboard navigation
                if (packages != null && selectionTile >= 0 && selectionTile < packages.Count())
                {
                    OnKeyboardSelection?.Invoke(selectionTile);
                }
            }
        }

        private void NavigateLeft()
        {
            if (selectionTile > 0)
            {
                selectionTile--;
                UpdateGridLayoutInfo();
            }
        }

        private void NavigateRight()
        {
            if (selectionTile < contents.Length - 1)
            {
                selectionTile++;
                UpdateGridLayoutInfo();
            }
        }

        private void NavigateUp()
        {
            if (_currentRow > 0 && selectionTile >= _cellsPerRow)
            {
                selectionTile -= _cellsPerRow;
                _currentRow--;
                if (selectionTile < 0) selectionTile = 0;
            }
        }

        private void NavigateDown()
        {
            if (_currentRow < _totalRows - 1 && selectionTile + _cellsPerRow < contents.Length)
            {
                selectionTile += _cellsPerRow;
                _currentRow++;
                if (selectionTile >= contents.Length) selectionTile = contents.Length - 1;
            }
        }

        private void NavigateToStart()
        {
            selectionTile = 0;
            _currentRow = 0;
            _currentCol = 0;
        }

        private void NavigateToEnd()
        {
            selectionTile = contents.Length - 1;
            _currentRow = _totalRows - 1;
            _currentCol = (contents.Length - 1) % _cellsPerRow;
        }

        private void NavigatePageUp()
        {
            int pageSize = _cellsPerRow * 3; // Navigate 3 rows up
            if (selectionTile >= pageSize)
            {
                selectionTile -= pageSize;
            }
            else
            {
                selectionTile = 0;
            }

            _currentRow = selectionTile / _cellsPerRow;
            _currentCol = selectionTile % _cellsPerRow;
        }

        private void NavigatePageDown()
        {
            int pageSize = _cellsPerRow * 3; // Navigate 3 rows down
            if (selectionTile + pageSize < contents.Length)
            {
                selectionTile += pageSize;
            }
            else
            {
                selectionTile = contents.Length - 1;
            }

            _currentRow = selectionTile / _cellsPerRow;
            _currentCol = selectionTile % _cellsPerRow;
        }

        private void CalculateBulkSelection()
        {
            selectionItems = Selection
                .Select((item, index) => item == UIStyles.selectedTileContent ? packages.ElementAt(index) : null)
                .Where(item => item != null)
                .ToList();
            selectionCount = selectionItems.Count;
            selectionSize = selectionItems.Sum(item => item.Size);
            selectionPackageCount = selectionItems.GroupBy(item => item.AssetId).Count();
            selectionItems.ForEach(info => info.CheckIfInProject());
            AI.ResolveParents(selectionItems, _allPackages);

            _bulkHandler?.Invoke();
        }

        public void DeselectAll()
        {
            selectionTile = 0;
            _selectionMin = selectionTile;
            _selectionMax = selectionTile;
            _lastSelectionTile = selectionTile;
            MarkGridSelection(true);
            CalculateBulkSelection();
        }

        /// <summary>
        /// Ensures the selected tile is visible in the scroll view by updating the scroll position
        /// </summary>
        /// <param name="scrollPosition">Current scroll position (passed by reference)</param>
        /// <param name="viewHeight">Height of the visible view area</param>
        public void EnsureSelectedTileVisible(ref Vector2 scrollPosition, float viewHeight)
        {
            if (contents == null || contents.Length == 0 || selectionTile < 0 || selectionTile >= contents.Length) return;

            // Use the saved tile height from the last draw operation
            if (_actualTileHeight <= 0f) return; // No valid tile height available yet

            // Calculate the row of the selected tile
            int selectedRow = selectionTile / _cellsPerRow;

            // Calculate the Y position of the selected tile using saved tile height
            float tileY = selectedRow * _actualTileHeight;

            // Calculate the visible range
            float visibleMinY = scrollPosition.y;
            float visibleMaxY = scrollPosition.y + viewHeight;

            // Check if the selected tile is above the visible area
            if (tileY < visibleMinY)
            {
                // Scroll up to show the selected tile at the top with some padding
                scrollPosition.y = Mathf.Max(0, tileY - 20);
            }
            // Check if the selected tile is below the visible area
            else if (tileY + _actualTileHeight > visibleMaxY)
            {
                // Scroll down to show the selected tile fully visible
                // Position the tile so it's visible in the middle of the view
                scrollPosition.y = tileY - (viewHeight / 2) + (_actualTileHeight / 2);

                // Ensure we don't scroll past the end of the content
                if (contents != null && contents.Length > 0)
                {
                    float totalContentHeight = Mathf.CeilToInt((float)contents.Length / _cellsPerRow) * _actualTileHeight;
                    scrollPosition.y = Mathf.Min(scrollPosition.y, totalContentHeight - viewHeight);
                }
            }
        }

        /// <summary>
        /// Checks if keyboard navigation occurred and resets the flag
        /// </summary>
        /// <returns>True if keyboard navigation occurred since last check</returns>
        public bool CheckAndResetKeyboardNavigation()
        {
            bool occurred = _keyboardNavigationOccurred;
            _keyboardNavigationOccurred = false;
            return occurred;
        }

        public void Select(AssetInfo info)
        {
            if (packages != null)
            {
                selectionTile = packages.ToList().FindIndex(p => p.AssetId == info.AssetId);
            }
            else
            {
                selectionTile = 0;
            }
            MarkGridSelection(true);

            _selectionMin = selectionTile;
            _selectionMax = selectionTile;

            CalculateBulkSelection();
        }
    }
}