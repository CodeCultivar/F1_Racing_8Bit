using UnityEngine;

namespace FTM
{
    /// <summary>Drives the race screen every frame.</summary>
    public class RaceRunner : MonoBehaviour
    {
        public RaceScreen screen { get; set; }
        public float acc;
        void Update() { if (screen != null) screen.Tick(Time.unscaledDeltaTime); }
    }
}
