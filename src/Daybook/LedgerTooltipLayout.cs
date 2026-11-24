using HarmonyLib;
using SuperFantasyKingdom;
using UnityEngine;

namespace Daybook
{
    // The native world tooltip follows the cursor and can cover ledger values while Alt is held.
    internal sealed class LedgerTooltipLayout
    {
        private static readonly System.Reflection.FieldInfo TipRect = AccessTools.Field(typeof(TooltipManager), "rectTransform");
        private readonly Vector3[] corners = new Vector3[4];
        private TooltipManager owner;
        private RectTransform tip;
        private Vector3 original, shifted;
        private bool moved;

        internal void Restore()
        {
            // A native re-anchor or another UI update takes precedence over our previous offset.
            if (moved && tip != null && tip.localPosition == shifted) tip.localPosition = original;
            moved = false;
        }

        internal void Place(RectTransform ledger, RectTransform viewport)
        {
            Restore();
            if (ledger == null || viewport == null) return;
            var manager = TooltipManager.Instance;
            if (manager == null || !manager.IsOpen()) return;
            if (owner != manager || tip == null)
            {
                owner = manager;
                tip = TipRect?.GetValue(manager) as RectTransform;
            }
            if (tip == null || !tip.gameObject.activeInHierarchy) return;
            var bounds = Bounds(tip, viewport);
            var book = Bounds(ledger, viewport);
            if (!bounds.Overlaps(book)) return;

            float gap = 4 * Mathf.Abs(viewport.InverseTransformVector(ledger.TransformVector(Vector3.up)).y);
            Vector2 best = Vector2.zero;
            float distance = float.PositiveInfinity;
            void TryOffset(float x, float y)
            {
                var candidate = new Vector2(x, y);
                var rect = new Rect(bounds.position + candidate, bounds.size);
                var screen = viewport.rect;
                if (rect.xMin < screen.xMin || rect.yMin < screen.yMin ||
                    rect.xMax > screen.xMax || rect.yMax > screen.yMax || candidate.sqrMagnitude >= distance) return;
                best = candidate;
                distance = candidate.sqrMagnitude;
            }

            TryOffset(0, book.yMin - bounds.yMax - gap);
            TryOffset(book.xMax - bounds.xMin + gap, 0);
            TryOffset(book.xMin - bounds.xMax - gap, 0);
            if (best == Vector2.zero) return;
            original = tip.localPosition;
            tip.position += viewport.TransformVector(best);
            shifted = tip.localPosition;
            moved = true;
        }

        internal bool OverlapsVisible(RectTransform frame, RectTransform obstacle, RectTransform viewport)
        {
            return obstacle != null && obstacle.gameObject.activeInHierarchy &&
                Bounds(frame, viewport).Overlaps(Bounds(obstacle, viewport));
        }

        private Rect Bounds(RectTransform target, RectTransform viewport)
        {
            target.GetWorldCorners(corners);
            var min = viewport.InverseTransformPoint(corners[0]);
            var max = viewport.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
