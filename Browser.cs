using UnityEngine;

namespace CosmeticExporter
{
    internal sealed class Browser
    {
        private const float Width = 740, Height = 660;
        private readonly Plugin owner;
        private Rect window = new Rect(60, 60, Width, Height);
        private Vector2 scroll;
        private string query = "";
        private GUIStyle title, label, muted, button, field;
        private readonly GUI.WindowFunction drawWindow;
        public float Visibility;

        public Browser(Plugin owner)
        {
            this.owner = owner;
            drawWindow = Window;
        }

        public void Draw(bool open)
        {
            var skin = GUI.skin;
            var matrix = GUI.matrix;
            var color = GUI.color;
            var content = GUI.contentColor;
            var background = GUI.backgroundColor;
            var depth = GUI.depth;
            var enabled = GUI.enabled;
            try
            {
                GUI.skin = null;
                GUI.color = GUI.contentColor = GUI.backgroundColor = Color.white;
                GUI.enabled = true;
                GUI.depth = -1000;
                Styles();
                float scale = Mathf.Clamp(Mathf.Min(Screen.width / 850f, Screen.height / 760f), .25f, 1.35f);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                if (!open)
                {
                    Fill(new Rect(14, Screen.height / scale - 43, 215, 28), .11f, 7);
                    GUI.Label(new Rect(25, Screen.height / scale - 40, 205, 24), "F6  ·  Cosmetic Exporter", label);
                }
                if (Visibility <= .001f) return;
                GUI.color = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, Visibility));
                window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width / scale - Width));
                window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height / scale - Height));
                for (int i = 9; i >= 1; i--)
                {
                    var shadow = new Rect(window.x - i, window.y + 5 - i, Width + i * 2, Height + i * 2);
                    GUI.DrawTexture(shadow, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                        new Color(0, 0, 0, .045f), 0, 12 + i);
                }
                window = GUI.Window(0x43E1007, window, drawWindow, GUIContent.none, GUIStyle.none);
            }
            finally
            {
                GUI.skin = skin;
                GUI.matrix = matrix;
                GUI.color = color;
                GUI.contentColor = content;
                GUI.backgroundColor = background;
                GUI.depth = depth;
                GUI.enabled = enabled;
            }
        }

        private void Window(int id)
        {
            Fill(new Rect(0, 0, Width, Height), .115f, 12);
            GUI.Label(new Rect(22, 14, 400, 36), "Cosmetic Exporter", title);
            GUI.Label(new Rect(Width - 115, 23, 50, 22), "F6", muted);
            if (GUI.Button(new Rect(Width - 52, 17, 32, 30), "X", button)) owner.SetOpen(false);
            GUI.Label(new Rect(22, 58, Width - 44, 22), owner.Matches.Count + " / " + owner.Entries.Count + " cosmetics", muted);
            GUI.SetNextControlName("CosmeticSearch");
            string changed = GUI.TextField(new Rect(22, 87, Width - 44, 37), query, field);
            if (changed != query)
            {
                query = changed;
                owner.Filter(query);
                scroll = Vector2.zero;
            }
            if (query.Length == 0 && GUI.GetNameOfFocusedControl() != "CosmeticSearch")
                GUI.Label(new Rect(34, 94, Width - 68, 24), "Search name or cosmetic ID", muted);

            var list = new Rect(22, 138, Width - 44, 372);
            Fill(list, .085f, 7);
            float rowWidth = list.width - 20;
            scroll = GUI.BeginScrollView(list, scroll, new Rect(0, 0, rowWidth, owner.Matches.Count * 48f));
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / 48));
            int last = Mathf.Min(owner.Matches.Count, first + 10);
            for (int i = first; i < last; i++)
            {
                var item = owner.Matches[i];
                var area = new Rect(5, i * 48 + 3, rowWidth - 10, 43);
                bool selected = owner.Selected?.Id == item.Id;
                float shade = selected ? .29f : area.Contains(Event.current.mousePosition) ? .22f : .16f;
                Fill(area, shade, 5);
                if (GUI.Button(area, GUIContent.none, GUIStyle.none)) owner.Selected = item;
                GUI.Label(new Rect(area.x + 10, area.y + 1, area.width - 20, 23), item.Name, label);
                GUI.Label(new Rect(area.x + 10, area.y + 22, area.width - 20, 18), item.Id, muted);
            }
            GUI.EndScrollView();
            GUI.enabled = !owner.Busy && owner.Selected != null;
            string export = owner.Busy ? "Exporting..." : owner.Selected == null ? "Select a cosmetic" : "Export selected to GLB";
            if (GUI.Button(new Rect(22, 525, Width - 44, 40), export, button)) owner.Export();
            GUI.enabled = true;
            GUI.Label(new Rect(22, 576, Width - 44, 36), owner.Status, label);
            GUI.Label(new Rect(22, 621, Width - 44, 24), "Saved to  Gorilla Tag / CosmeticExporter / Exports", muted);
            GUI.DragWindow(new Rect(0, 0, Width - 130, 52));
        }

        private void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            muted = new GUIStyle(label) { fontSize = 12 };
            title.normal.textColor = label.normal.textColor = new Color(.94f, .94f, .94f);
            muted.normal.textColor = new Color(.65f, .65f, .65f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 16, padding = new RectOffset(11, 11, 8, 8) };
        }

        private static void Fill(Rect area, float gray, float radius)
        {
            GUI.DrawTexture(area, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(gray, gray, gray), 0, radius);
        }
    }
}
