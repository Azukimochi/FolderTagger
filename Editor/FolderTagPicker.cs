using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FolderTagger
{
    /// <summary>Dropdown listing the tags; picking one assigns it to the folders the dropdown was opened for.</summary>
    class FolderTagPicker : EditorWindow
    {
        const float Width = 220f;
        const float RowHeight = 22f;
        const float BadgeSize = 18f;

        static GUIStyle s_RowLabel;
        static GUIStyle s_RowLabelBold;

        string[] _folderGuids;
        Vector2 _scroll;

        public static void Show(string[] folderGuids, Vector2 screenPosition)
        {
            if (folderGuids == null || folderGuids.Length == 0) return;
            var db = FolderTagDatabase.GetOrCreate();
            if (db == null) return;

            var picker = CreateInstance<FolderTagPicker>();
            picker._folderGuids = folderGuids;
            float height = Mathf.Min(420f, (db.tags.Count + 2) * RowHeight + 36f);
            picker.ShowAsDropDown(new Rect(screenPosition, Vector2.one), new Vector2(Width, height));
        }

        void OnEnable() => wantsMouseMove = true;

        void OnGUI()
        {
            var db = FolderTagDatabase.Find();
            // The folder list is not serialized, so the picker cannot survive a domain reload.
            if (db == null || _folderGuids == null)
            {
                Close();
                return;
            }
            if (Event.current.type == EventType.MouseMove) Repaint();

            var currentIds = _folderGuids.Select(g => db.GetTagForFolder(g)?.id).Distinct().ToArray();
            string currentId = currentIds.Length == 1 ? currentIds[0] : null;

            string header = _folderGuids.Length == 1
                ? Path.GetFileName(AssetDatabase.GUIDToAssetPath(_folderGuids[0]))
                : $"{_folderGuids.Length} 個のフォルダ";
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            Action pending = null;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var tag in db.tags)
            {
                var tagId = tag.id;
                if (Row(tag.name, tag.color, tag.icon, tag.id == currentId))
                    pending = () => db.Assign(_folderGuids, tagId);
            }
            EditorGUILayout.EndScrollView();

            var line = GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.3f));
            if (Row("タグを外す", null, null, false)) pending = () => db.Assign(_folderGuids, null);
            if (Row("タグを編集…", null, null, false)) pending = FolderTaggerWindow.Open;

            if (pending == null) return;
            Close();
            pending();
        }

        static bool Row(string label, Color? color, Texture2D icon, bool selected)
        {
            if (s_RowLabel == null)
            {
                s_RowLabel = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                s_RowLabelBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            }

            var rect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                if (rect.Contains(e.mousePosition))
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.08f) : new Color(0f, 0f, 0f, 0.08f));
                if (color.HasValue)
                {
                    var badge = new Rect(rect.x + 4f, rect.y + (RowHeight - BadgeSize) * 0.5f, BadgeSize, BadgeSize);
                    FolderTagDrawer.DrawBadge(badge, color.Value, icon);
                }
                var labelRect = new Rect(rect.x + 28f, rect.y, rect.width - 48f, rect.height);
                GUI.Label(labelRect, label, selected ? s_RowLabelBold : s_RowLabel);
                if (selected) GUI.Label(new Rect(rect.xMax - 20f, rect.y, 20f, rect.height), "✓", s_RowLabelBold);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }
    }
}
