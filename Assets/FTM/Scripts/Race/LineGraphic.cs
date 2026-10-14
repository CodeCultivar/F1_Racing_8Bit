using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FTM
{
    /// <summary>Draws a thick polyline (track, pit lane) as a UI mesh. Points are in local pixels, y down.</summary>
    public class LineGraphic : MaskableGraphic
    {
        public List<Vector2> points = new List<Vector2>();
        public float width = 6f;
        public bool closed;
        public bool pairs;          // draw separate segments 0-1, 2-3, ... (dashes, curbs)

        public void SetPoints(IEnumerable<Vector2> pts, bool isClosed)
        {
            points = new List<Vector2>(pts);
            closed = isClosed;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = points.Count;
            if (n < 2) return;
            int segs = closed ? n : n - 1;
            float hw = width / 2f;
            var col = (Color32)color;
            for (int i = 0; i < segs; i++)
            {
                if (pairs && i % 2 == 1) continue;
                Vector2 a = Flip(points[i]), b = Flip(points[(i + 1) % n]);
                Vector2 d = b - a;
                if (d.sqrMagnitude < 0.0001f) continue;
                d.Normalize();
                Vector2 nrm = new Vector2(-d.y, d.x) * hw;
                Vector2 ext = d * Mathf.Min(hw * 0.5f, 2f); // small overlap hides joints
                Quad(vh, a - ext - nrm, a - ext + nrm, b + ext + nrm, b + ext - nrm, col);
                if (hw > 5f && !pairs) Disc(vh, b, hw, col);   // round joins for thick lines (no gaps on bends)
            }
        }

        static Vector2 Flip(Vector2 p) { return new Vector2(p.x, -p.y); }

        static void Disc(VertexHelper vh, Vector2 c, float r, Color32 col)
        {
            int i0 = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            const int seg = 10;
            for (int k = 0; k <= seg; k++)
            {
                float a = k * Mathf.PI * 2f / seg;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col, Vector2.zero);
            }
            for (int k = 0; k < seg; k++) vh.AddTriangle(i0, i0 + 1 + k, i0 + 2 + k);
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, col, Vector2.zero);
            vh.AddVert(b, col, Vector2.zero);
            vh.AddVert(c, col, Vector2.zero);
            vh.AddVert(d, col, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
