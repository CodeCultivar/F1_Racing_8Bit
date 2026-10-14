using System;
using System.IO;
using UnityEngine;

namespace FTM
{
    public static class ConfigLoader
    {
        static GameConfig _cfg;
        public static string LoadError;

        public static GameConfig Config
        {
            get
            {
                if (_cfg == null) Load();
                return _cfg;
            }
        }

        public static void Load()
        {
            LoadError = null;
            var ta = Resources.Load<TextAsset>("FTM/GameConfig");
            if (ta == null) { LoadError = "GameConfig.json not found"; _cfg = new GameConfig(); return; }
            try { _cfg = JsonUtility.FromJson<GameConfig>(ta.text) ?? new GameConfig(); }
            catch (Exception e)
            {
                LoadError = "GameConfig.json has an error: " + e.Message;
                Debug.LogError("FTM: " + LoadError);
                _cfg = new GameConfig();
            }
        }
    }

    /// <summary>3 independent career slots stored as JSON files, plus shared audio settings.</summary>
    public static class SaveSystem
    {
        public const int Slots = 3;

        static string Dir { get { return Path.Combine(Application.persistentDataPath, "Careers"); } }
        static string PathFor(int slot) { return Path.Combine(Dir, "career_" + slot + ".json"); }

        public static bool Exists(int slot) { return File.Exists(PathFor(slot)); }

        public static CareerSave Load(int slot)
        {
            try
            {
                if (!Exists(slot)) return null;
                var c = JsonUtility.FromJson<CareerSave>(File.ReadAllText(PathFor(slot)));
                if (c != null) c.slot = slot;
                return c;
            }
            catch (Exception e)
            {
                Debug.LogError("FTM: could not read save slot " + slot + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Writes to a temp file first, then swaps it in, so a failed save never damages the last good one.</summary>
        public static bool TrySave(CareerSave c, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Dir);
                string path = PathFor(c.slot);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(c, true));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogError("FTM: save failed: " + e.Message);
                return false;
            }
        }

        public static void Delete(int slot)
        {
            try { if (Exists(slot)) File.Delete(PathFor(slot)); }
            catch (Exception e) { Debug.LogError("FTM: delete failed: " + e.Message); }
        }

        /// <summary>The most recently played career, or -1 if there are no saves.</summary>
        public static int MostRecentSlot()
        {
            int best = -1; long bestTicks = long.MinValue;
            for (int s = 1; s <= Slots; s++)
            {
                var c = Load(s);
                if (c != null && c.lastPlayedTicks > bestTicks) { best = s; bestTicks = c.lastPlayedTicks; }
            }
            return best;
        }

        public static AudioSettings LoadAudio()
        {
            return new AudioSettings { music = PlayerPrefs.GetInt("ftm_music", 80), sfx = PlayerPrefs.GetInt("ftm_sfx", 80) };
        }

        public static void SaveAudio(AudioSettings a)
        {
            PlayerPrefs.SetInt("ftm_music", a.music);
            PlayerPrefs.SetInt("ftm_sfx", a.sfx);
            PlayerPrefs.Save();
        }
    }
}
