using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FTM
{
    /// <summary>
    /// "Follow Selected" camera: a close top-down view of the live simulation. Grey asphalt with white edge
    /// lines and red-and-white curbs in the corners, 8-bit cars in team livery with name tags. The camera
    /// slides smoothly after the selected car; the screen never rotates (the cars do).
    /// </summary>
    public class FollowCam
    {
        public const float Zoom = 6f;          // pixels per metre
        const float HalfWidth = 7f;            // half track width in metres

        readonly RaceState s;
        readonly Track t;
        public readonly RectTransform root;
        readonly RectTransform world;
        readonly float vw, vh;
        float minX, maxY;
        float zoomNow = 1f;      // 1 at 1x; zooms out a little at 3x / 5x so the view isn't a blur
        readonly List<RectTransform> cars = new List<RectTransform>();
        readonly List<RectTransform> tags = new List<RectTransform>();

        static LineGraphic Line(Transform parent, string name, float w, float h, Color c, float width)
        {
            var go = Ui.Group(parent, name, 0, 0, w, h).gameObject;
            go.AddComponent<CanvasRenderer>();
            var lg = go.AddComponent<LineGraphic>();
            lg.color = c; lg.width = width; lg.raycastTarget = false;
            return lg;
        }

        public FollowCam(RaceState state, Track track, RectTransform area, float w, float h, System.Action<int> select)
        {
            s = state; t = track; vw = w - 4; vh = h - 4;
            root = Ui.Group(area, "Follow", 2, 2, vw, vh);
            root.gameObject.AddComponent<RectMask2D>();
            var bg = root.gameObject.AddComponent<Image>();
            bg.color = Theme.Hex("#4F942E");

            minX = float.MaxValue; maxY = float.MinValue;
            float maxX = float.MinValue, minY = float.MaxValue;
            foreach (var p in t.pts) { minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            minX -= 60; maxY += 60; maxX += 60; minY -= 60;
            float ww = (maxX - minX) * Zoom, wh = (maxY - minY) * Zoom;
            world = Ui.Group(root, "World", 0, 0, ww, wh);

            // run-off, asphalt, pit lane
            var runoff = Line(world, "Runoff", ww, wh, Theme.Hex("#5FA83A"), (HalfWidth + 6) * 2 * Zoom);
            runoff.SetPoints(Pts(t.pts), true);
            var pitLane = Line(world, "PitLane", ww, wh, Theme.Hex("#6E747A"), 9 * Zoom);
            pitLane.SetPoints(Pts(t.PitPolyline()), false);
            var asphalt = Line(world, "Asphalt", ww, wh, Theme.Hex("#5B6168"), HalfWidth * 2 * Zoom);
            asphalt.SetPoints(Pts(t.pts), true);

            // white edge lines
            var left = new List<Vector2>(); var right = new List<Vector2>();
            for (int i = 0; i < t.n; i++) { left.Add(t.pts[i] + t.normalIn[i] * HalfWidth); right.Add(t.pts[i] - t.normalIn[i] * HalfWidth); }
            Line(world, "EdgeIn", ww, wh, Color.white, 3).SetPoints(Pts(left), true);
            Line(world, "EdgeOut", ww, wh, Color.white, 3).SetPoints(Pts(right), true);

            // red-and-white curbs on both sides through the corners (2 m blocks)
            var red = new List<Vector2>(); var white = new List<Vector2>();
            int blocks = Mathf.Max(1, Mathf.RoundToInt(4f / t.step));
            for (int i = 0; i + blocks < t.n; i += blocks)
            {
                if (t.kind[i] != 2) continue;
                bool r = (i / blocks) % 2 == 0;
                foreach (float side in new[] { 1f, -1f })
                {
                    var a = t.pts[i] + t.normalIn[i] * (HalfWidth + 0.8f) * side;
                    var b = t.pts[i + blocks] + t.normalIn[i + blocks] * (HalfWidth + 0.8f) * side;
                    var list = r ? red : white;
                    list.Add(W(a)); list.Add(W(b));
                }
            }
            var redL = Line(world, "CurbRed", ww, wh, Theme.Red, 1.6f * Zoom); redL.pairs = true; redL.SetPoints(red, false);
            var whiteL = Line(world, "CurbWhite", ww, wh, Color.white, 1.6f * Zoom); whiteL.pairs = true; whiteL.SetPoints(white, false);

            // start / finish line
            var sfA = t.pts[0] + t.normalIn[0] * HalfWidth; var sfB = t.pts[0] - t.normalIn[0] * HalfWidth;
            Line(world, "StartLine", ww, wh, Color.white, 6).SetPoints(new[] { W(sfA), W(sfB) }, false);

            // cars
            for (int i = 0; i < s.cars.Count; i++)
            {
                var car = s.cars[i];
                Color tc = Theme.Hex(car.color);
                var img = Ui.Icon(world, PixelArt.Car(tc), 0, 0, 5.6f * Zoom, 2.6f * Zoom);
                img.preserveAspect = false;
                var rt = img.rectTransform;
                rt.pivot = new Vector2(0.5f, 0.5f);
                cars.Add(rt);
                int idx = i;
                var tag = Ui.Button(world, "", BtnStyle.Dark, 0, 0, car.tla.Length > 3 ? 54 : 40, 22, () => select(idx));
                tag.customFill = tc; tag.Apply();
                var tx = Ui.Label(tag.transform, car.tla, 14, true, Theme.TextOn(tc), 0, 0, car.tla.Length > 3 ? 54 : 40, 22, TextAnchor.MiddleCenter);
                tx.horizontalOverflow = HorizontalWrapMode.Overflow;
                var trt = (RectTransform)tag.transform;
                trt.pivot = new Vector2(0.5f, 0f);
                tags.Add(trt);
            }
            root.gameObject.SetActive(false);
        }

        Vector2 W(Vector2 m) { return new Vector2((m.x - minX) * Zoom, (maxY - m.y) * Zoom); }

        IEnumerable<Vector2> Pts(IEnumerable<Vector2> metres) { foreach (var m in metres) yield return W(m); }

        /// <summary>Car position drawn between simulation steps (lead = sim seconds since the last step), so motion is smooth at any speed.</summary>
        void CarPose(CarState car, float lead, out Vector2 pos, out float heading)
        {
            Vector2 a, b;
            if (car.status == 1 || car.status == 2 || car.status == 5)
            {
                float ps = Mathf.Min(t.pitLen, car.vInit ? car.vPit : car.pitS + (car.status == 1 ? car.speed * lead : 0));
                a = t.PitPos(ps);
                b = t.PitPos(Mathf.Min(t.pitLen, ps + 4f));
                if ((b - a).sqrMagnitude < 0.01f) b = a + (t.PitPos(ps) - t.PitPos(Mathf.Max(0, ps - 4f)));
            }
            else
            {
                double drawDist = car.vInit ? car.vDist : car.dist + (car.status == 0 ? car.speed * lead : 0);
                a = t.Pos(drawDist);
                b = t.Pos(drawDist + 4.0);
                int i = Mathf.Clamp((int)(t.Wrap(car.dist) / t.step), 0, t.n - 1);
                float lateral = 0;
                if (car.dist < 0) lateral = car.grid % 2 == 0 ? 3f : -3f;          // two-wide grid
                else lateral = car.vLat;                                            // eases out to the side while passing
                a += t.normalIn[i] * lateral; b += t.normalIn[i] * lateral;
            }
            pos = W(a);
            Vector2 d = W(b) - pos;
            heading = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        }

        static readonly Vector2[] TagSlots =
        {
            new Vector2(0, 26), new Vector2(0, -30), new Vector2(34, 2), new Vector2(-34, 2),
            new Vector2(34, 30), new Vector2(-34, 30), new Vector2(34, -26), new Vector2(-34, -26), new Vector2(0, 54), new Vector2(0, -54)
        };

        int lastTop = -1;
        readonly Vector2[] screenPos = new Vector2[64];

        public void Refresh(int selected, float realDt, float lead)
        {
            if (!root.gameObject.activeSelf || selected < 0) return;
            // zoom: 1x close, 3x / 5x a bit further out
            float want = s.speed >= 5 ? 0.55f : s.speed >= 3 ? 0.75f : 1f;
            zoomNow = Mathf.MoveTowards(zoomNow, want, realDt * 1.5f);
            world.localScale = new Vector3(zoomNow, zoomNow, 1);

            Vector2 target; float heading;
            CarPose(s.cars[selected], lead, out target, out heading);
            // the camera stays locked on the selected car (no lag, no wobble)
            world.anchoredPosition = new Vector2(vw / 2f - target.x * zoomNow, target.y * zoomNow - vh / 2f);

            for (int i = 0; i < s.cars.Count; i++)
            {
                var car = s.cars[i];
                Vector2 p; float h;
                CarPose(car, lead, out p, out h);
                cars[i].anchoredPosition = new Vector2(p.x, -p.y);
                cars[i].localEulerAngles = new Vector3(0, 0, -h);
                cars[i].GetComponent<Image>().color = car.status == 3 ? new Color(0.5f, 0.5f, 0.5f, 0.7f) : Color.white;
                screenPos[i] = (p - target) * zoomNow;           // relative to the screen centre
            }

            // name tags: keep them a constant size and stop them piling on top of each other
            var placed = new List<Rect>();
            var order = new List<int>();
            order.Add(selected);
            for (int i = 0; i < s.cars.Count; i++) if (i != selected) order.Add(i);
            foreach (int i in order)
            {
                var car = s.cars[i];
                var tag = tags[i];
                bool visible = car.status != 3 && car.status != 5 && Mathf.Abs(screenPos[i].x) < vw / 2f + 60 && Mathf.Abs(screenPos[i].y) < vh / 2f + 60;
                if (!visible) { if (tag.gameObject.activeSelf) tag.gameObject.SetActive(false); continue; }
                Vector2 size = tag.sizeDelta;
                bool found = false;
                Vector2 slotUsed = TagSlots[0];
                foreach (var slot in TagSlots)
                {
                    var r = new Rect(screenPos[i].x + slot.x - size.x / 2f, screenPos[i].y - slot.y, size.x, size.y);
                    if (!placed.Exists(q => q.Overlaps(r))) { placed.Add(r); slotUsed = slot; found = true; break; }
                }
                if (!found && i != selected) { if (tag.gameObject.activeSelf) tag.gameObject.SetActive(false); continue; }
                if (!tag.gameObject.activeSelf) tag.gameObject.SetActive(true);
                // tags live in the world, so undo the zoom on them
                tag.localScale = new Vector3(1f / zoomNow, 1f / zoomNow, 1);
                Vector2 wp = screenPos[i] / zoomNow + target;
                tag.anchoredPosition = new Vector2(wp.x + slotUsed.x / zoomNow, -wp.y + slotUsed.y / zoomNow);
            }
            if (lastTop != selected)
            {
                cars[selected].SetAsLastSibling();
                tags[selected].SetAsLastSibling();
                lastTop = selected;
            }
        }

        public void Show(bool on)
        {
            root.gameObject.SetActive(on);
            lastTop = -1;
        }
    }
}
