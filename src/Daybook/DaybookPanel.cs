using System.Collections.Generic;
using SuperFantasyKingdom.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace Daybook
{
    public sealed class DaybookPanel : MonoBehaviour
    {
        private static readonly Color32 Ink = new Color32(48, 35, 24, 255);
        private static readonly Color32 Gold = new Color32(255, 224, 158, 255);
        private static readonly Color32 Gain = new Color32(162, 243, 137, 255);
        private static readonly Color32 Loss = new Color32(255, 169, 151, 255);

        private sealed class Row
        {
            internal LedgerEntry Entry;
            internal Text Compact, Dawn, Earned, Spent, Now, Net;
            internal int? LastDawn, LastNow;
            internal long LastEarned, LastSpent;
            internal bool Painted;
        }

        private readonly List<Row> rows = new List<Row>();
        private RectTransform frame, strip, book, viewport;
        private Text fontSource, historyCaption;
        private float bookHeight;
        private bool detailed, controller;
        private ButtonHint open, older, newer, close;

        private sealed class ButtonHint
        {
            internal RectTransform Root;
            internal Image Icon;
            internal Text Label;
            internal string Action, Verb;
        }

        internal static DaybookPanel Attach(UIHeader header, ResourceLedger ledger)
        {
            var resources = header.transform.Find("PanelResources");
            var amount = resources?.Find("Wood/Amount")?.GetComponent<Text>();
            var border = resources?.GetComponent<Image>();
            if (amount == null || border == null)
                return null;

            var root = new GameObject("Daybook", typeof(RectTransform), typeof(LayoutElement));
            root.GetComponent<LayoutElement>().ignoreLayout = true;
            var panel = root.AddComponent<DaybookPanel>();
            panel.fontSource = amount;
            panel.frame = (RectTransform)root.transform;
            panel.frame.SetParent(resources, false);
            panel.frame.anchorMin = Vector2.zero;
            panel.frame.anchorMax = Vector2.right;
            panel.frame.pivot = new Vector2(0.5f, 1);
            panel.frame.anchoredPosition = new Vector2(0, -4);
            var canvas = header.GetComponentInParent<Canvas>();
            if (canvas != null)
                panel.viewport = (RectTransform)canvas.rootCanvas.transform;

            var surface = root.AddComponent<Image>();
            surface.raycastTarget = false;
            surface.sprite = border.sprite;
            surface.type = border.type;
            surface.pixelsPerUnitMultiplier = border.pixelsPerUnitMultiplier;
            surface.color = border.color;
            panel.strip = panel.Inset("Daily changes");
            panel.book = panel.Inset("Daily ledger");
            panel.BuildRows(ledger);
            panel.SetView(false, false);
            return panel;
        }

        private RectTransform Inset(string name)
        {
            var image = GraphicChild<Image>(frame, name);
            image.color = Ink;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(5, 5);
            rect.offsetMax = new Vector2(-5, -5);
            return rect;
        }

        private void BuildRows(ResourceLedger ledger)
        {
            ColumnTitle("Dawn", 0.33f, 0.46f);
            ColumnTitle("Earned", 0.46f, 0.62f);
            ColumnTitle("Spent", 0.62f, 0.76f);
            ColumnTitle("Now", 0.76f, 0.88f);
            ColumnTitle("Net", 0.88f, 1);
            float y = 0;
            string group = null;
            for (int i = 0; i < ledger.Entries.Count; i++)
            {
                var entry = ledger.Entries[i];
                if (entry.Group != group)
                {
                    group = entry.Group;
                    var heading = Label(book, group, 24, TextAnchor.MiddleLeft);
                    Place(heading.rectTransform, 0, 0.33f, y, 26, 5, 2);
                    heading.color = Gold;
                    y += 26;
                }

                var row = new Row { Entry = entry };
                if (i < ledger.HeaderCount)
                {
                    float left = (float)i / ledger.HeaderCount;
                    float right = (float)(i + 1) / ledger.HeaderCount;
                    row.Compact = Label(strip, "Net " + entry.Type, 26, TextAnchor.MiddleCenter);
                    Place(row.Compact.rectTransform, left, right, 0, 26);
                }

                var icon = GraphicChild<Image>(book, entry.Type + " icon");
                icon.sprite = entry.Data != null ? entry.Data.icon : null;
                icon.enabled = icon.sprite != null;
                icon.preserveAspect = true;
                Place(icon.rectTransform, 0, 0, y, 24, 3, -27);
                var name = entry.Data != null ? entry.Data.GetName() : null;
                if (string.IsNullOrEmpty(name))
                    name = entry.Type == SuperFantasyKingdom.ResourceType.SuperMetal ? "Super Metal" : entry.Type.ToString();
                var title = Label(book, name, 23, TextAnchor.MiddleLeft);
                Place(title.rectTransform, 0, 0.33f, y, 24, 30, 2);
                row.Dawn = Number("Dawn", 0.33f, 0.46f, y);
                row.Earned = Number("Earned", 0.46f, 0.62f, y);
                row.Spent = Number("Spent", 0.62f, 0.76f, y);
                row.Now = Number("Now", 0.76f, 0.88f, y);
                row.Net = Number("Net", 0.88f, 1, y);
                rows.Add(row);
                y += 24;
            }
            historyCaption = Label(book, "", 21, TextAnchor.MiddleCenter);
            historyCaption.color = Gold;
            Place(historyCaption.rectTransform, 0, 1, y, 26);
            bookHeight = y + 36;
            open = MakeHint(strip, ControllerLedger.Details, "Hold for ledger", 0.19f, 1, 26);
            older = MakeHint(book, ControllerLedger.Older, "Older", 0.03f, 0.32f, y + 26);
            newer = MakeHint(book, ControllerLedger.Newer, "Newer", 0.33f, 0.62f, y + 26);
            close = MakeHint(book, ControllerLedger.Details, "Release to close", 0.63f, 1, y + 26);
        }

        private ButtonHint MakeHint(RectTransform parent, string action, string verb,
            float left, float right, float top)
        {
            var root = (RectTransform)new GameObject(verb, typeof(RectTransform)).transform;
            root.SetParent(parent, false);
            Place(root, left, right, top, 26);
            var icon = GraphicChild<Image>(root, "Button");
            icon.preserveAspect = true;
            Place(icon.rectTransform, 0, 0, 0, 24, 0, -26);
            var label = Label(root, verb, 20, TextAnchor.MiddleLeft);
            return new ButtonHint { Root = root, Icon = icon, Label = label, Action = action, Verb = verb };
        }

        internal void UpdateControllerHints(PlayerInput player, bool hasHistory)
        {
            if (!detailed)
            {
                ShowHint(open, player);
                return;
            }
            older.Root.gameObject.SetActive(hasHistory);
            newer.Root.gameObject.SetActive(hasHistory);
            if (hasHistory)
            {
                ShowHint(older, player);
                ShowHint(newer, player);
            }
            ShowHint(close, player);
        }

        private static void ShowHint(ButtonHint hint, PlayerInput player)
        {
            var sprite = ControllerLedger.Glyph(player, hint.Action, out string binding);
            hint.Icon.sprite = sprite;
            hint.Icon.enabled = sprite != null;
            hint.Label.text = sprite == null && !string.IsNullOrEmpty(binding)
                ? binding + "  " + hint.Verb : hint.Verb;
            Place(hint.Label.rectTransform, 0, 1, 0, 26, sprite != null ? 29 : 0, 2);
        }

        private void ColumnTitle(string title, float left, float right)
        {
            var label = Label(book, title, 23, TextAnchor.MiddleRight);
            Place(label.rectTransform, left, right, 0, 26, 1, 5);
            label.color = Gold;
        }

        private Text Number(string name, float left, float right, float y)
        {
            var label = Label(book, name, 24, TextAnchor.MiddleRight);
            Place(label.rectTransform, left, right, y, 24, 1, 5);
            return label;
        }

        internal bool SyncView(bool expanded, bool usingController)
        {
            bool changed = expanded != detailed || controller != usingController;
            if (changed)
                SetView(expanded, usingController);
            if (expanded && viewport != null)
            {
                // Fit the expanded ledger on shorter windows in the same pixel space as the HUD.
                float top = viewport.InverseTransformPoint(frame.position).y;
                float unit = Mathf.Abs(viewport.InverseTransformVector(frame.parent.TransformVector(Vector3.up)).y);
                float room = top - viewport.rect.yMin - 8;
                if (room > 0 && unit > 0)
                    frame.localScale = Vector3.one * Mathf.Min(1, room / (frame.sizeDelta.y * unit));
            }
            return changed;
        }

        private void SetView(bool expanded, bool usingController)
        {
            detailed = expanded;
            controller = usingController;
            open.Root.gameObject.SetActive(controller);
            older.Root.gameObject.SetActive(controller);
            newer.Root.gameObject.SetActive(controller);
            close.Root.gameObject.SetActive(controller);
            strip.gameObject.SetActive(!expanded);
            book.gameObject.SetActive(expanded);
            frame.localScale = Vector3.one;
            frame.pivot = new Vector2(1, 1);
            frame.sizeDelta = new Vector2(expanded ? 140 : 0, (expanded ? bookHeight : 36) + (controller ? 26 : 0));
            frame.anchoredPosition = new Vector2(0, -4);
        }

        internal void Refresh(DayRecord history, string caption)
        {
            historyCaption.text = caption;
            foreach (var row in rows)
            {
                var entry = row.Entry.Balance;
                if (history != null && !history.Rows.TryGetValue((int)row.Entry.Type, out entry))
                    entry = new Balance();
                if (row.Painted && row.LastDawn == entry.Dawn && row.LastNow == entry.Now &&
                    row.LastEarned == entry.Earned && row.LastSpent == entry.Spent)
                    continue;
                row.Painted = true;
                row.LastDawn = entry.Dawn;
                row.LastNow = entry.Now;
                row.LastEarned = entry.Earned;
                row.LastSpent = entry.Spent;
                row.Earned.text = entry.Dawn.HasValue ? entry.Earned.ToString() : "\u2013";
                row.Spent.text = entry.Dawn.HasValue ? entry.Spent.ToString() : "\u2013";
                long? net = (long?)entry.Now - entry.Dawn;
                string delta = net?.ToString("+0;-0;0") ?? "–";
                Color32 color = net > 0 ? Gain : net < 0 ? Loss : (Color32)Color.white;
                row.Dawn.text = entry.Dawn?.ToString() ?? "–";
                row.Now.text = entry.Now?.ToString() ?? "–";
                row.Net.text = delta;
                row.Net.color = color;
                if (row.Compact != null)
                {
                    row.Compact.text = delta;
                    row.Compact.color = color;
                }
            }
        }

        private static T GraphicChild<T>(RectTransform parent, string name) where T : Graphic
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            var graphic = child.AddComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private Text Label(RectTransform parent, string text, int size, TextAnchor alignment)
        {
            var label = GraphicChild<Text>(parent, text);
            label.font = fontSource.font;
            label.fontStyle = fontSource.fontStyle;
            label.fontSize = label.resizeTextMaxSize = size;
            label.resizeTextMinSize = 12;
            label.resizeTextForBestFit = true;
            label.alignment = alignment;
            label.supportRichText = false;
            label.text = text;
            return label;
        }

        private static void Place(RectTransform rect, float left, float right, float top,
            float height, float insetLeft = 0, float insetRight = 0)
        {
            rect.anchorMin = new Vector2(left, 1);
            rect.anchorMax = new Vector2(right, 1);
            rect.offsetMin = new Vector2(insetLeft, -top - height);
            rect.offsetMax = new Vector2(-insetRight, -top);
        }
    }
}
