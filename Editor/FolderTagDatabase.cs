using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FolderTagger
{
    [Serializable]
    public class TagDefinition
    {
        public string id;
        public string name;
        public Color color = Color.white;
        public Texture2D icon;
    }

    [Serializable]
    public class FolderAssignment
    {
        public string folderGuid;
        public string tagId;
    }

    /// <summary>
    /// Project-wide store of tag definitions and folder-to-tag assignments.
    /// Folders are keyed by asset GUID so assignments survive renames and moves.
    /// </summary>
    public class FolderTagDatabase : ScriptableObject
    {
        const string AsmdefName = "FolderTagger.Editor";
        const string DefaultPackageRoot = "Packages/com.azukimochi.folder-tagger";
        // Lives under Assets because a package installed from Git is read-only.
        const string DatabaseFolder = "Assets/FolderTagger";
        const string DatabasePath = DatabaseFolder + "/FolderTagDatabase.asset";

        static readonly (string name, string icon, Color color)[] Presets =
        {
            ("アバター", "person", new Color(0.45f, 0.70f, 1.00f)),
            ("服", "apparel", new Color(1.00f, 0.55f, 0.75f)),
            ("髪", "face_3", new Color(0.75f, 0.55f, 1.00f)),
            ("小物", "diamond", new Color(1.00f, 0.82f, 0.35f)),
            ("テクスチャ", "palette", new Color(0.45f, 0.88f, 0.65f)),
            ("ギミック", "animation", new Color(1.00f, 0.62f, 0.35f)),
            ("ツール", "build", new Color(0.62f, 0.68f, 0.75f)),
            ("お気に入り", "star", new Color(1.00f, 0.40f, 0.40f)),
            ("作業中", "construction", new Color(0.35f, 0.85f, 0.90f)),
        };

        public List<TagDefinition> tags = new List<TagDefinition>();
        public List<FolderAssignment> assignments = new List<FolderAssignment>();
        public bool showIconInListView = true;

        /// <summary>Raised after any change made through this class or by undo/redo.</summary>
        public static event Action Changed;

        static FolderTagDatabase s_Instance;
        static bool s_Searched;

        [NonSerialized] Dictionary<string, TagDefinition> _tagById;
        [NonSerialized] Dictionary<string, TagDefinition> _tagByFolder;

        /// <summary>The folder holding this tool, found via its asmdef so it works wherever the package is installed.</summary>
        public static string PackageRoot
        {
            get
            {
                foreach (var guid in AssetDatabase.FindAssets(AsmdefName + " t:AssemblyDefinitionAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) != AsmdefName) continue;
                    // <root>/Editor/FolderTagger.Editor.asmdef
                    var root = Path.GetDirectoryName(Path.GetDirectoryName(path));
                    if (!string.IsNullOrEmpty(root)) return root.Replace('\\', '/');
                }
                return DefaultPackageRoot;
            }
        }

        public static string IconFolder => PackageRoot + "/Icons";

        /// <summary>Returns the database if one exists, without creating it. Cheap enough to call every repaint.</summary>
        public static FolderTagDatabase Find()
        {
            if (s_Instance != null) return s_Instance;
            if (s_Searched) return null;
            s_Searched = true;
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(FolderTagDatabase)))
            {
                var db = AssetDatabase.LoadAssetAtPath<FolderTagDatabase>(AssetDatabase.GUIDToAssetPath(guid));
                if (db == null) continue;
                s_Instance = db;
                return db;
            }
            return null;
        }

        public static FolderTagDatabase GetOrCreate()
        {
            var db = Find();
            if (db != null) return db;

            // A file that exists but has not been imported yet must never be overwritten.
            if (File.Exists(DatabasePath)) return AssetDatabase.LoadAssetAtPath<FolderTagDatabase>(DatabasePath);

            if (!AssetDatabase.IsValidFolder(DatabaseFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(DatabaseFolder), Path.GetFileName(DatabaseFolder));

            db = CreateInstance<FolderTagDatabase>();
            db.AddPresets(IconFolder);
            AssetDatabase.CreateAsset(db, DatabasePath);
            AssetDatabase.SaveAssets();
            s_Instance = db;
            NotifyChanged();
            return db;
        }

        internal static void ResetLookup() => s_Searched = false;

        internal static void NotifyChanged()
        {
            Changed?.Invoke();
            EditorApplication.RepaintProjectWindow();
        }

        void AddPresets(string iconFolder)
        {
            foreach (var (name, icon, color) in Presets)
            {
                tags.Add(new TagDefinition
                {
                    id = NewId(),
                    name = name,
                    color = color,
                    icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconFolder + "/" + icon + ".png"),
                });
            }
        }

        static string NewId() => Guid.NewGuid().ToString("N");

        public TagDefinition GetTag(string tagId)
        {
            EnsureCache();
            return tagId != null && _tagById.TryGetValue(tagId, out var tag) ? tag : null;
        }

        public TagDefinition GetTagForFolder(string folderGuid)
        {
            EnsureCache();
            return folderGuid != null && _tagByFolder.TryGetValue(folderGuid, out var tag) ? tag : null;
        }

        public int CountFolders(string tagId)
        {
            int count = 0;
            foreach (var a in assignments)
                if (a.tagId == tagId) count++;
            return count;
        }

        void EnsureCache()
        {
            if (_tagByFolder != null) return;
            _tagById = new Dictionary<string, TagDefinition>();
            foreach (var tag in tags)
                if (tag != null && !string.IsNullOrEmpty(tag.id)) _tagById[tag.id] = tag;
            _tagByFolder = new Dictionary<string, TagDefinition>();
            foreach (var a in assignments)
                if (a != null && a.folderGuid != null && a.tagId != null && _tagById.TryGetValue(a.tagId, out var tag))
                    _tagByFolder[a.folderGuid] = tag;
        }

        /// <summary>Drops the lookup cache. Needed whenever the lists are changed outside this class.</summary>
        public void Invalidate()
        {
            _tagById = null;
            _tagByFolder = null;
        }

        /// <summary>Assigns <paramref name="tagId"/> to the folders, or clears their tag when it is null.</summary>
        public void Assign(IEnumerable<string> folderGuids, string tagId)
        {
            Undo.RecordObject(this, tagId == null ? "フォルダのタグを外す" : "フォルダにタグを割り当て");
            foreach (var guid in folderGuids)
            {
                assignments.RemoveAll(a => a.folderGuid == guid);
                if (tagId != null) assignments.Add(new FolderAssignment { folderGuid = guid, tagId = tagId });
            }
            Commit();
        }

        public void AddTag(string name, Color color)
        {
            Undo.RecordObject(this, "タグを追加");
            tags.Add(new TagDefinition { id = NewId(), name = name, color = color });
            Commit();
        }

        /// <summary>Deletes the tag together with every assignment that uses it.</summary>
        public void RemoveTag(string tagId)
        {
            Undo.RecordObject(this, "タグを削除");
            tags.RemoveAll(t => t.id == tagId);
            assignments.RemoveAll(a => a.tagId == tagId);
            Commit();
        }

        public void SetIcon(string tagId, Texture2D icon)
        {
            var tag = GetTag(tagId);
            if (tag == null) return;
            Undo.RecordObject(this, "タグのアイコンを変更");
            tag.icon = icon;
            Commit();
        }

        /// <summary>Removes assignments whose folder was deleted or whose tag no longer exists.</summary>
        public void RemoveStaleAssignments()
        {
            EnsureCache();
            var tagIds = _tagById;
            Undo.RecordObject(this, "割り当てを整理");
            assignments.RemoveAll(a =>
                a.tagId == null || !tagIds.ContainsKey(a.tagId) ||
                !AssetDatabase.IsValidFolder(AssetDatabase.GUIDToAssetPath(a.folderGuid)));
            Commit();
        }

        void Commit()
        {
            Invalidate();
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
            NotifyChanged();
        }

        void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;

        void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

        void OnUndoRedo()
        {
            Invalidate();
            NotifyChanged();
        }

        // Called after SerializedObject edits and after the asset is reloaded from disk.
        void OnValidate() => Invalidate();
    }
}
