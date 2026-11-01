using System;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace FolderTagger
{
    /// <summary>Settings window: edits the tags and lists every folder assignment.</summary>
    class FolderTaggerWindow : EditorWindow
    {
        const float PreviewSize = 36f;
        const float Padding = 4f;
        const float ColorWidth = 70f;
        const float PickButtonWidth = 64f;
        const float CountWidth = 64f;

        FolderTagDatabase _db;
        SerializedObject _serialized;
        ReorderableList _tagList;
        Vector2 _scroll;
        bool _showAssignments = true;
        int _filter; // 0 = all tags, otherwise index into tags + 1
        Action _pending;

        [MenuItem("Tools/Folder Tagger")]
        public static void Open() => GetWindow<FolderTaggerWindow>("Folder Tagger");

        void OnEnable()
        {
            FolderTagDatabase.Changed += Repaint;
            Selection.selectionChanged += Repaint;
        }

        void OnDisable()
        {
            FolderTagDatabase.Changed -= Repaint;
            Selection.selectionChanged -= Repaint;
        }

        bool EnsureInitialized()
        {
            if (_db == null) _db = FolderTagDatabase.GetOrCreate();
            if (_db == null) return false;
            if (_serialized == null || _serialized.targetObject != _db)
            {
                _serialized = new SerializedObject(_db);
                _tagList = CreateTagList();
            }
            return true;
        }

        void OnGUI()
        {
            if (!EnsureInitialized())
            {
                EditorGUILayout.HelpBox("データベースを作成できませんでした。", MessageType.Error);
                return;
            }

            _serialized.Update();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSelection();
            EditorGUILayout.Space(8f);
            _tagList.DoLayoutList();
            EditorGUILayout.PropertyField(
                _serialized.FindProperty(nameof(FolderTagDatabase.showIconInListView)),
                new GUIContent("リスト表示でもアイコンを表示"));
            EditorGUILayout.Space(8f);
            DrawAssignments();

            EditorGUILayout.EndScrollView();

            if (_serialized.ApplyModifiedProperties())
            {
                _db.Invalidate();
                FolderTagDatabase.NotifyChanged();
            }

            // Direct edits run after the SerializedObject is applied so neither overwrites the other.
            if (_pending == null) return;
            var pending = _pending;
            _pending = null;
            pending();
            _serialized.Update();
        }

        void DrawSelection()
        {
            var guids = FolderTaggerMenu.GetSelectedFolderGuids();
            EditorGUILayout.LabelField("選択中のフォルダ", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                string label = guids.Length switch
                {
                    0 => "Projectウィンドウでフォルダを選択してください",
                    1 => AssetDatabase.GUIDToAssetPath(guids[0]),
                    _ => $"{guids.Length} 個のフォルダ",
                };
                EditorGUILayout.LabelField(label);

                using (new EditorGUI.DisabledScope(guids.Length == 0))
                {
                    var assignRect = GUILayoutUtility.GetRect(new GUIContent("タグを割り当て…"), GUI.skin.button, GUILayout.Width(110f));
                    if (GUI.Button(assignRect, "タグを割り当て…"))
                        FolderTagPicker.Show(guids, GUIUtility.GUIToScreenPoint(new Vector2(assignRect.x, assignRect.yMax)));
                    if (GUILayout.Button("タグを外す", GUILayout.Width(80f)))
                        _pending = () => _db.Assign(guids, null);
                }
            }
        }

        ReorderableList CreateTagList()
        {
            var list = new ReorderableList(_serialized, _serialized.FindProperty(nameof(FolderTagDatabase.tags)), true, true, true, true)
            {
                elementHeight = PreviewSize + Padding * 2f,
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "タグ"),
                drawElementCallback = DrawTagElement,
            };
            list.onAddCallback = _ => _pending = () => _db.AddTag("新しいタグ", Color.HSVToRGB(UnityEngine.Random.value, 0.5f, 1f));
            list.onRemoveCallback = l =>
            {
                var element = l.serializedProperty.GetArrayElementAtIndex(l.index);
                var id = element.FindPropertyRelative(nameof(TagDefinition.id)).stringValue;
                var name = element.FindPropertyRelative(nameof(TagDefinition.name)).stringValue;
                _pending = () => RemoveTag(id, name);
            };
            return list;
        }

        void DrawTagElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = _tagList.serializedProperty.GetArrayElementAtIndex(index);
            var idProperty = element.FindPropertyRelative(nameof(TagDefinition.id));
            var nameProperty = element.FindPropertyRelative(nameof(TagDefinition.name));
            var colorProperty = element.FindPropertyRelative(nameof(TagDefinition.color));
            var iconProperty = element.FindPropertyRelative(nameof(TagDefinition.icon));

            var preview = new Rect(rect.x, rect.y + Padding, PreviewSize, PreviewSize);
            if (Event.current.type == EventType.Repaint)
                FolderTagDrawer.DrawFolder(preview, colorProperty.colorValue, iconProperty.objectReferenceValue as Texture2D, false);

            float line = EditorGUIUtility.singleLineHeight;
            float x = preview.xMax + 8f;
            float width = rect.xMax - x;
            float top = rect.y + Padding;
            float bottom = top + line + 2f;

            EditorGUI.PropertyField(new Rect(x, top, width - ColorWidth - 4f, line), nameProperty, GUIContent.none);
            EditorGUI.PropertyField(new Rect(rect.xMax - ColorWidth, top, ColorWidth, line), colorProperty, GUIContent.none);

            var iconRect = new Rect(x, bottom, width - PickButtonWidth - CountWidth - 8f, line);
            EditorGUI.ObjectField(iconRect, iconProperty, typeof(Texture2D), GUIContent.none);

            var pickRect = new Rect(iconRect.xMax + 4f, bottom, PickButtonWidth, line);
            if (GUI.Button(pickRect, new GUIContent("選択…", "同梱アイコンから選ぶ"), EditorStyles.miniButton))
            {
                var id = idProperty.stringValue;
                var screenRect = new Rect(GUIUtility.GUIToScreenPoint(pickRect.position), pickRect.size);
                IconGridPicker.Show(screenRect, texture => _db.SetIcon(id, texture));
            }

            var countRect = new Rect(rect.xMax - CountWidth, bottom, CountWidth, line);
            EditorGUI.LabelField(countRect, $"{_db.CountFolders(idProperty.stringValue)} フォルダ", EditorStyles.miniLabel);
        }

        void RemoveTag(string id, string name)
        {
            int count = _db.CountFolders(id);
            if (count > 0 && !EditorUtility.DisplayDialog(
                    "タグを削除",
                    $"「{name}」は {count} 個のフォルダに割り当てられています。\n削除するとそれらの割り当ても外れます。",
                    "削除", "キャンセル"))
                return;
            _db.RemoveTag(id);
        }

        void DrawAssignments()
        {
            _showAssignments = EditorGUILayout.Foldout(_showAssignments, $"割り当て一覧 ({_db.assignments.Count})", true);
            if (!_showAssignments) return;

            var tagNames = _db.tags.Select(t => t.name).ToArray();
            var filterNames = new[] { "すべて" }.Concat(tagNames).ToArray();
            _filter = EditorGUILayout.Popup("タグで絞り込み", Mathf.Clamp(_filter, 0, filterNames.Length - 1), filterNames);
            string filterId = _filter > 0 ? _db.tags[_filter - 1].id : null;

            var rows = _db.assignments
                .Where(a => filterId == null || a.tagId == filterId)
                .Select(a => (assignment: a, path: AssetDatabase.GUIDToAssetPath(a.folderGuid)))
                .OrderBy(r => r.path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            bool anyStale = false;
            foreach (var (assignment, path) in rows)
            {
                var tag = _db.GetTag(assignment.tagId);
                bool exists = AssetDatabase.IsValidFolder(path);
                anyStale |= !exists || tag == null;
                var guid = assignment.folderGuid;

                using (new EditorGUILayout.HorizontalScope())
                {
                    var badge = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f));
                    if (tag != null && Event.current.type == EventType.Repaint)
                        FolderTagDrawer.DrawBadge(badge, tag.color, tag.icon);

                    using (new EditorGUI.DisabledScope(!exists))
                    {
                        var label = exists ? path : $"（見つかりません）{guid}";
                        if (GUILayout.Button(new GUIContent(label, "クリックでProjectウィンドウに表示"), EditorStyles.label, GUILayout.MinWidth(50f)))
                        {
                            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                            Selection.activeObject = folder;
                            EditorGUIUtility.PingObject(folder);
                        }
                    }

                    int index = tag == null ? -1 : _db.tags.IndexOf(tag);
                    int newIndex = EditorGUILayout.Popup(index, tagNames, GUILayout.Width(100f));
                    if (newIndex != index && newIndex >= 0)
                    {
                        var tagId = _db.tags[newIndex].id;
                        _pending = () => _db.Assign(new[] { guid }, tagId);
                    }
                    if (GUILayout.Button(new GUIContent("×", "タグを外す"), GUILayout.Width(22f)))
                        _pending = () => _db.Assign(new[] { guid }, null);
                }
            }

            if (rows.Count == 0)
                EditorGUILayout.HelpBox("割り当てられたフォルダはありません。\nProjectウィンドウでフォルダを右クリック →「Folder Tagger」から割り当てられます。", MessageType.Info);
            if (anyStale && GUILayout.Button("見つからないフォルダの割り当てを整理"))
                _pending = () => _db.RemoveStaleAssignments();
        }
    }
}
