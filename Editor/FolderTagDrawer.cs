using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FolderTagger
{
    /// <summary>Tints tagged folders in the Project window and overlays the tag icon on their top-left corner.</summary>
    [InitializeOnLoad]
    static class FolderTagDrawer
    {
        const float GridLabelHeight = 14f;
        const float TreeIndent = 14f;
        const float RowIconSize = 16f;
        const float RowBadgeSize = 11f;
        const float GridBadgeScale = 0.32f;

        static readonly Dictionary<Texture, Texture2D> s_WhiteFolders = new Dictionary<Texture, Texture2D>();
        static readonly HashSet<Texture> s_PendingFolders = new HashSet<Texture>();
        static Texture2D s_Circle;

        /// <summary>Screen position of the last click in the Project window, used to place the tag picker.</summary>
        internal static Vector2 LastClickScreenPosition;

        static FolderTagDrawer()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseTextures;
        }

        static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown)
                LastClickScreenPosition = GUIUtility.GUIToScreenPoint(e.mousePosition);
            if (e.type != EventType.Repaint) return;

            var db = FolderTagDatabase.Find();
            if (db == null) return;
            var tag = db.GetTagForFolder(guid);
            if (tag == null) return;

            bool isRow = rect.height <= 20f;
            var icon = isRow && !db.showIconInListView ? null : tag.icon;
            DrawFolder(GetIconRect(rect, isRow), tag.color, icon, isRow);
        }

        static Rect GetIconRect(Rect rect, bool isRow)
        {
            if (isRow)
            {
                // Tree rows (one-column view and the folder tree) start at 2 + n * 14;
                // rows of the two-column file list draw their icon 3px further right.
                if (!Mathf.Approximately(rect.x % TreeIndent, 2f)) rect.x += 3f;
                return new Rect(rect.x, rect.y, RowIconSize, RowIconSize);
            }
            float size = Mathf.Min(rect.width, rect.height - GridLabelHeight);
            return new Rect(rect.x + (rect.width - size) * 0.5f, rect.y, size, size);
        }

        /// <summary>Draws a folder in <paramref name="color"/> with <paramref name="icon"/> as a badge on its top-left corner.</summary>
        internal static void DrawFolder(Rect rect, Color color, Texture2D icon, bool small)
        {
            var folder = GetWhiteFolder();
            if (folder != null)
                GUI.DrawTexture(rect, folder, ScaleMode.ScaleToFit, true, 0f, color, 0f, 0f);
            if (icon == null) return;

            float gridBadgeSize = Mathf.Round(rect.width * GridBadgeScale);
            var badge = small
                ? new Rect(rect.x - 2f, rect.y - 1f, RowBadgeSize, RowBadgeSize)
                : new Rect(rect.x + rect.width * 0.04f, rect.y + rect.height * 0.12f, gridBadgeSize, gridBadgeSize);
            DrawBadge(badge, color, icon);
        }

        /// <summary>Draws <paramref name="icon"/> in white on a circle of a darkened <paramref name="color"/>.</summary>
        internal static void DrawBadge(Rect rect, Color color, Texture2D icon)
        {
            var background = Color.Lerp(color, Color.black, 0.55f);
            background.a = 1f;
            GUI.DrawTexture(rect, Circle, ScaleMode.StretchToFill, true, 0f, background, 0f, 0f);
            if (icon == null) return;

            float inset = rect.width * 0.17f;
            var inner = new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);
            GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit, true, 0f, Color.white, 0f, 0f);
        }

        /// <summary>
        /// Unity's folder icon brightened to white so that tinting it gives the pure tag colour.
        /// Returns null until the copy is ready; it is built outside of repaint because it renders on the GPU.
        /// </summary>
        static Texture2D GetWhiteFolder()
        {
            var source = EditorGUIUtility.IconContent("Folder Icon").image;
            if (source == null) return null;
            if (s_WhiteFolders.TryGetValue(source, out var white) && white != null) return white;

            if (s_PendingFolders.Add(source))
            {
                EditorApplication.delayCall += () =>
                {
                    s_PendingFolders.Remove(source);
                    if (source == null) return;
                    s_WhiteFolders[source] = CreateWhiteCopy(source);
                    FolderTagDatabase.NotifyChanged();
                };
            }
            return null;
        }

        static Texture2D CreateWhiteCopy(Texture source)
        {
            int width = source.width, height = source.height;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            // Scale so the brightest opaque pixel becomes white, keeping the icon's own shading.
            var pixels = copy.GetPixels32();
            int max = 1;
            foreach (var p in pixels)
                if (p.a > 128) max = Mathf.Max(max, p.r, p.g, p.b);
            float scale = 255f / max;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                pixels[i] = new Color32(
                    (byte)Mathf.Min(255f, p.r * scale),
                    (byte)Mathf.Min(255f, p.g * scale),
                    (byte)Mathf.Min(255f, p.b * scale),
                    p.a);
            }
            copy.SetPixels32(pixels);
            copy.Apply(false);
            return copy;
        }

        static Texture2D Circle
        {
            get
            {
                if (s_Circle != null) return s_Circle;
                const int size = 64;
                s_Circle = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                float center = (size - 1) * 0.5f, radius = size * 0.5f - 1f;
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f));
                }
                s_Circle.SetPixels32(pixels);
                s_Circle.Apply(false);
                return s_Circle;
            }
        }

        static void ReleaseTextures()
        {
            foreach (var texture in s_WhiteFolders.Values)
                if (texture != null) Object.DestroyImmediate(texture);
            s_WhiteFolders.Clear();
            if (s_Circle != null) Object.DestroyImmediate(s_Circle);
        }
    }
}
