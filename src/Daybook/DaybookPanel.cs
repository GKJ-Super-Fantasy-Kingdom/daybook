using System.Collections.Generic;
using SuperFantasyKingdom;
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
            internal Text Title, Compact, Dawn, Earned, Spent, Now, Net;
            internal int? LastDawn, LastNow;
            internal long LastEarned, LastSpent;
            internal bool Painted, ShowGross, Partial;
        }

        private readonly List<Row> rows = new List<Row>();
        private RectTransform frame, strip, book, viewport;
        private Text fontSource, historyCaption, coverageNote;
        private float bookHeight;
        private int revision = -1;
        private DayRecord selected;
        private bool detailed, historyHintsVisible, noteVisible;
        private readonly LedgerTooltipLayout tooltipLayout = new LedgerTooltipLayout();

        private void OnEnable() => Canvas.willRenderCanvases += PlaceTooltip;

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= PlaceTooltip;
            tooltipLayout.Restore();
        }

        private CanvasGroup visibility;
        private void PlaceTooltip()
        {
            if (frame == null || viewport == null || visibility == null) return;
            var overlays = OverlayManager.Instance;
            var obstructed = false;
            if (overlays != null && overlays.IsOpen())
            {
                var selection = overlays.GetOverlaySelection()?.parent as RectTransform;
                var tabs = overlays.GetOverlayTabsSelection() as RectTransform;
                obstructed = tooltipLayout.OverlapsVisible(frame, selection, viewport) ||
                    tooltipLayout.OverlapsVisible(frame, tabs, viewport);
            }
            float alpha = obstructed ? 0 : 1;
            if (visibility.alpha != alpha) visibility.alpha = alpha;
            tooltipLayout.Place(detailed && !obstructed ? frame : null, viewport);
        }
        private ButtonHint older, newer;

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
            if (amount == null && resources != null) amount = resources.GetComponentInChildren<Text>(true);
            var border = resources?.GetComponent<Image>();
            if (amount == null || border == null)
                return null;

            var root = new GameObject("Daybook", typeof(RectTransform), typeof(LayoutElement));
            root.GetComponent<LayoutElement>().ignoreLayout = true;
            var panel = root.AddComponent<DaybookPanel>();
            panel.visibility = root.AddComponent<CanvasGroup>();
            panel.visibility.blocksRaycasts = false;
            panel.visibility.interactable = false;
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
            panel.SelectRows(ledger, null);
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

        internal bool SelectRows(ResourceLedger ledger, DayRecord history)
        {
            if (selected == history && revision == ledger.Revision) return false;
            selected = history;
            revision = ledger.Revision;
            foreach (var container in new[] { strip, book })
                foreach (Transform child in container)
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            rows.Clear();
            BuildRows(history == null ? ledger.Entries : ledger.HistoryRows(history),
                history == null ? ledger.HeaderCount : 0);
            SetView(detailed, historyHintsVisible);
            return true;
        }

        private void BuildRows(List<LedgerEntry> entries, int headerCount)
        {
            ColumnTitle("Dawn", 0.33f, 0.46f);
            ColumnTitle("Earned", 0.46f, 0.62f);
            ColumnTitle("Spent", 0.62f, 0.76f);
            ColumnTitle("Now", 0.76f, 0.88f);
            ColumnTitle("Net", 0.88f, 1);
            float y = 0;
            string group = null;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Group != group)
                {
                    group = entry.Group;
                    // Group names are also stored in existing journals. Format only Daybook's own headings.
                    var titleText = group switch
                    {
                        "Food and supplies" => "Food and Supplies",
                        "Other stock" => "Other Stock",
                        "Archived stock" => "Archived Stock",
                        _ => group
                    };
                    var heading = Label(book, titleText, 24, TextAnchor.MiddleLeft);
                    Place(heading.rectTransform, 0, 0.33f, y, 26, 5, 2);
                    heading.color = Gold;
                    y += 26;
                }

                var row = new Row { Entry = entry };
                if (i < headerCount)
                {
                    float left = (float)i / headerCount;
                    float right = (float)(i + 1) / headerCount;
                    row.Compact = Label(strip, "Net " + entry.Type, 26, TextAnchor.MiddleCenter);
                    Place(row.Compact.rectTransform, left, right, 0, 26);
                }

                var icon = GraphicChild<Image>(book, entry.Type + " icon");
                icon.sprite = entry.Icon;
                icon.enabled = icon.sprite != null;
                icon.preserveAspect = true;
                Place(icon.rectTransform, 0, 0, y, 24, 3, -27);
                row.Title = Label(book, entry.Label, 23, TextAnchor.MiddleLeft);
                Place(row.Title.rectTransform, 0, 0.33f, y, 24, icon.enabled ? 30 : 5, 2);
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
            coverageNote = Label(book, "* Counts start at first observation and exclude gaps.", 20, TextAnchor.MiddleCenter);
            coverageNote.color = Gold;
            bookHeight = y + 36;
            older = MakeHint(book, ControllerLedger.Older, "Older", 0.18f, 0.50f, y + 26);
            newer = MakeHint(book, ControllerLedger.Newer, "Newer", 0.55f, 0.90f, y + 26);
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

        internal void UpdateControllerHints(PlayerInput player, bool canGoOlder, bool canGoNewer)
        {
            ShowHint(older, player, canGoOlder);
            ShowHint(newer, player, canGoNewer);
        }

        private static void ShowHint(ButtonHint hint, PlayerInput player, bool available)
        {
            string binding = "";
            var sprite = available ? ControllerLedger.Glyph(player, hint.Action, out binding) : null;
            bool visible = sprite != null || !string.IsNullOrEmpty(binding);
            hint.Root.gameObject.SetActive(visible);
            if (!visible) return;
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

        internal bool SyncView(bool expanded, bool showHistoryHints, bool recording)
        {
            bool showNote = expanded && recording && rows.Exists(row => row.Entry.Balance.IsPartial);
            bool changed = expanded != detailed || historyHintsVisible != showHistoryHints || showNote != noteVisible;
            noteVisible = showNote;
            if (changed)
                SetView(expanded, showHistoryHints);
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

        private void SetView(bool expanded, bool showHistoryHints)
        {
            detailed = expanded;
            historyHintsVisible = showHistoryHints;
            older.Root.gameObject.SetActive(showHistoryHints);
            newer.Root.gameObject.SetActive(showHistoryHints);
            strip.gameObject.SetActive(!expanded);
            book.gameObject.SetActive(expanded);
            coverageNote.gameObject.SetActive(noteVisible);
            float controlsHeight = showHistoryHints ? 26 : 0;
            Place(coverageNote.rectTransform, 0, 1, bookHeight - 10 + controlsHeight, 24, 5, 5);
            frame.localScale = Vector3.one;
            frame.pivot = new Vector2(1, 1);
            frame.sizeDelta = new Vector2(expanded ? 140 : 0, expanded ? bookHeight + controlsHeight + (noteVisible ? 24 : 0) : 36);
            frame.anchoredPosition = new Vector2(0, -4);
        }

        internal void Refresh(string caption, bool recording)
        {
            historyCaption.text = caption;
            foreach (var row in rows)
            {
                var entry = row.Entry.Balance;
                bool showGross = recording && entry.HasSample;
                bool partial = showGross && entry.IsPartial;
                if (row.Painted && row.ShowGross == showGross && row.Partial == partial && row.LastDawn == entry.Dawn && row.LastNow == entry.Now &&
                    row.LastEarned == entry.Earned && row.LastSpent == entry.Spent)
                    continue;
                if (!row.Painted || row.Partial != partial)
                    row.Title.text = partial ? row.Entry.Label + "*" : row.Entry.Label;
                row.Painted = true;
                row.ShowGross = showGross;
                row.Partial = partial;
                row.LastDawn = entry.Dawn;
                row.LastNow = entry.Now;
                row.LastEarned = entry.Earned;
                row.LastSpent = entry.Spent;
                row.Earned.text = showGross ? entry.Earned.ToString() : "\u2013";
                row.Spent.text = showGross ? entry.Spent.ToString() : "\u2013";
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
