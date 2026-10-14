using System;
using System.Collections.Generic;
using UnityEngine;

namespace FTM
{
    [Serializable]
    class TrackFile
    {
        public string id = "", name = "";
        public float length, step;
        public bool ccw;
        public float[] x = new float[0], y = new float[0], v = new float[0];
    }

    /// <summary>
    /// A real circuit outline (from the MIT-licensed f1-circuits data set) resampled every few metres,
    /// with a reference speed for every point. Distance 0 is the start/finish line, in racing direction.
    /// </summary>
    public class Track
    {
        public string id, name;
        public float L, step;
        public int n;
        public Vector2[] pts;
        public float[] vref;
        public byte[] kind;          // 0 straight, 1 braking zone, 2 corner
        public Vector2[] normalIn;   // unit vector pointing to the inside of the circuit
        public float pitIn, pitOut;  // lap distances of pit entry (before the line) and pit exit (after it)
        public float pitLen;
        public float[] sectorAt;     // lap distance of the S1/S2 and S2/S3 boundaries

        public const float PitOffset = 22f;
        public const float VMax = 92f;

        static readonly Dictionary<string, Track> cache = new Dictionary<string, Track>();

        public static Track Load(string id)
        {
            Track t;
            if (cache.TryGetValue(id, out t)) return t;
            var ta = Resources.Load<TextAsset>("FTM/Tracks/" + id);
            if (ta == null) return null;
            var f = JsonUtility.FromJson<TrackFile>(ta.text);
            t = new Track { id = f.id, name = f.name, step = f.step, n = f.x.Length };
            t.L = t.n * t.step;
            t.pts = new Vector2[t.n];
            t.vref = f.v;
            for (int i = 0; i < t.n; i++) t.pts[i] = new Vector2(f.x[i], f.y[i]);
            t.kind = new byte[t.n];
            t.normalIn = new Vector2[t.n];
            for (int i = 0; i < t.n; i++)
            {
                float v = t.vref[i], vNext = t.vref[(i + 2) % t.n];
                t.kind[i] = (byte)(vNext < v - 0.4f ? 1 : v < 74f ? 2 : 0);
                Vector2 d = (t.pts[(i + 1) % t.n] - t.pts[(i - 1 + t.n) % t.n]).normalized;
                Vector2 left = new Vector2(-d.y, d.x);
                t.normalIn[i] = f.ccw ? left : -left;
            }
            t.pitIn = t.L - 380f;
            t.pitOut = 320f;
            t.pitLen = (t.L - t.pitIn) + t.pitOut;
            t.sectorAt = new[] { t.L / 3f, 2f * t.L / 3f };
            cache[id] = t;
            return t;
        }

        public float Wrap(double s)
        {
            double r = s % L;
            if (r < 0) r += L;
            return (float)r;
        }

        int Index(float s) { return Mathf.Clamp((int)(s / step), 0, n - 1); }

        public Vector2 Pos(double dist)
        {
            float s = Wrap(dist);
            float f = s / step;
            int i = (int)f % n;
            int j = (i + 1) % n;
            return Vector2.Lerp(pts[i], pts[j], f - Mathf.Floor(f));
        }

        public float VRef(float s) { return vref[Index(s)]; }
        public byte Kind(float s) { return kind[Index(s)]; }

        /// <summary>Position in the pit lane, given metres travelled since pit entry.</summary>
        public Vector2 PitPos(float sPit)
        {
            float lapS = Wrap(pitIn + sPit);
            int i = Index(lapS);
            float ramp = Mathf.Min(Mathf.SmoothStep(0, 1, sPit / 90f), Mathf.SmoothStep(0, 1, (pitLen - sPit) / 90f));
            return Pos(lapS) + normalIn[i] * PitOffset * ramp;
        }

        /// <summary>Metres from pit entry to a team's pit box (boxes spread either side of the line).</summary>
        public float BoxAt(int teamIndex, int teamCount)
        {
            float centre = L - pitIn;              // the start/finish line, measured from pit entry
            float spacing = 26f;
            return centre - (teamCount - 1) * spacing / 2f + teamIndex * spacing;
        }

        public IEnumerable<Vector2> PitPolyline()
        {
            for (float s = 0; s <= pitLen; s += step * 2) yield return PitPos(s);
            yield return PitPos(pitLen);
        }

        public int Sector(float lapS) { return lapS < sectorAt[0] ? 0 : lapS < sectorAt[1] ? 1 : 2; }
    }
}
