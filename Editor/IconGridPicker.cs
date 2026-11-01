using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FolderTagger
{
    /// <summary>Dropdown grid of the textures in the tool's Icons folder.</summary>
    class IconGridPicker : EditorWindow
    {
        const float Cell = 40f;
        const float IconInset = 8f;
        const string SearchControl = "IconGridPickerSearch";

        Action<Texture2D> _onPick;
        List<Texture2D> _icons;
        string _search = "";
        Vector2 _scroll;
        bool _focused;

        public static void Show(Rect activatorScreenRect, Action<Texture2D> onPick)
        {
            var picker = CreateInstance<IconGridPicker>();
            picker._onPick = onPick;
            picker._icons = LoadIcons();
            picker.ShowAsDropDown(activatorScreenRect, new Vector2(8 * Cell + 24f, 340f));
        }

        static List<Texture2D> LoadIcons()
        {
            var folder = FolderTagDatabase.IconFolder;
            if (!AssetDatabase.IsValidFolder(folder)) return new List<Texture2D>();
            return AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(t => t != null)
                .ToList();
        }

        void OnEnable() => wantsMouseMove = true;

        void OnGUI()
        {
            // The callback is not serialized, so the picker cannot survive a domain reload.
            if (_icons == null || _onPick == null)
            {
                Close();
                return;
            }
            var e = Event.current;
            if (e.type == EventType.MouseMove) Repaint();

            bool pickedNone = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUI.SetNextControlName(SearchControl);
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("なし", EditorStyles.toolbarButton, GUILayout.Width(40f))) pickedNone = true;
            }
            if (!_focused)
            {
                EditorGUI.FocusTextInControl(SearchControl);
                _focused = true;
            }

            var shown = string.IsNullOrEmpty(_search)
                ? _icons
                : _icons.Where(t => t.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            Texture2D picked = null;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 16f) / Cell));
            int rows = (shown.Count + columns - 1) / columns;
            var area = GUILayoutUtility.GetRect(columns * Cell, rows * Cell);
            var tint = EditorGUIUtility.isProSkin ? Color.white : new Color(0.15f, 0.15f, 0.15f);
            for (int i = 0; i < shown.Count; i++)
            {
                var cell = new Rect(area.x + i % columns * Cell, area.y + i / columns * Cell, Cell, Cell);
                if (e.type == EventType.Repaint)
                {
                    if (cell.Contains(e.mousePosition))
                        EditorGUI.DrawRect(cell, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.1f) : new Color(0f, 0f, 0f, 0.1f));
                    var inner = new Rect(cell.x + IconInset, cell.y + IconInset, Cell - IconInset * 2f, Cell - IconInset * 2f);
                    GUI.DrawTexture(inner, shown[i], ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
                }
                GUI.Label(cell, new GUIContent(string.Empty, shown[i].name));
                if (e.type == EventType.MouseDown && e.button == 0 && cell.Contains(e.mousePosition))
                {
                    picked = shown[i];
                    e.Use();
                }
            }
            EditorGUILayout.EndScrollView();

            if (picked == null && !pickedNone) return;
            var onPick = _onPick;
            Close();
            onPick(picked);
        }
    }
}
