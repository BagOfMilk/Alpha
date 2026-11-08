using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Режим вікна (Налаштування → Графіка).</summary>
    public enum RingDisplayMode { Fullscreen = 0, Windowed = 1 }

    /// <summary>Рівень графіки: Авто — автопідбір під машину (GraphicsTierPicker), далі — рівні якості URP проєкту.</summary>
    public enum RingGraphics { Auto = 0, Low = 1, Medium = 2, High = 3 }

    /// <summary>
    /// Частота екшн-кадрів матчу (Налаштування → Гра; TRPG_DESIGN §4.12 «частота екшн-кадрів і уповільнення (як було)»):
    /// камера через плече на великому прийомі. Значення стабільні — пишуться в сховище числом.
    /// </summary>
    public enum RingActionShots { Off = 0, Rare = 1, Normal = 2, Often = 3 }

    /// <summary>Дії, які гравець може перепризначити (Налаштування → Керування). Цифри 1–9 і миша — сталі.</summary>
    public enum RingAction { Up = 0, Down, Left, Right, Confirm, Back, TabPrev, TabNext, Log }

    /// <summary>Сховище налаштувань (у грі — PlayerPrefs, у тестах — словник). Ключі й значення — рядки.</summary>
    public interface IRingSettingsStore
    {
        string Get(string key);
        void Set(string key, string value);
        void Save();
    }

    /// <summary>
    /// Значення налаштувань меню «Рингу» (ТЗ B08 §4): без UnityEngine, тому тестуються звичайним .NET. Клавіші — імена
    /// <c>UnityEngine.KeyCode</c> рядком (модель не знає рушія; хост розбирає ім'я).
    /// </summary>
    public sealed class RingSettingsValues
    {
        public const int VolumeSteps = 10;
        public static readonly int[] FrameCaps = { 30, 60, 0 };          // 0 — без обмеження
        public static readonly int[] UiScales = { 90, 100, 110, 120 };    // відсоток від еталона 900 рядків

        public RingDisplayMode Display = RingDisplayMode.Fullscreen;
        /// <summary>Індекс у списку роздільностей монітора; -1 — рідна.</summary>
        public int Resolution = -1;
        public bool VSync = true;
        public int FrameCap = 60;
        public RingGraphics Graphics = RingGraphics.Auto;

        public int Master = 8, Music = 6, Sfx = 8, Voice = 8;            // 0…10
        public bool MuteInBackground = true;

        public bool ReduceMotion;
        public bool ScreenShake = true;
        public bool Flashes = true;
        public int UiScale = 100;

        public string Language = "uk";

        // ---- подача матчу (Налаштування → Гра; §4.12) ----
        /// <summary>QTE-натиск у послідовності прийому — лише видовище (рішення власника 08.11.2026): вимкнено — натиску немає.</summary>
        public bool Qte = true;
        /// <summary>Як часто великий прийом показує екшн-кадр; частку для накопичувача дає <see cref="ActionShotChance"/>.</summary>
        public RingActionShots ActionShots = RingActionShots.Normal;
        /// <summary>Сповільнення на фінішері (крупний план лишається, змінюється лише швидкість часу).</summary>
        public bool FinisherSlowMo = true;

        /// <summary>
        /// Частка накопичувача екшн-кадрів за рівнем (без випадковості: кадр, коли накопичене ≥ 1; два поспіль — ні, §4.12).
        /// «Звично» — 0,34, як у бойовій камері, з якої механізм узято (≈ один кадр на три великі прийоми). ПЛЕЙСХОЛДЕР —
        /// камера може тюнити числа, сенс рівнів лишається: Off — ніколи, Rare &lt; Normal &lt; Often.
        /// </summary>
        public static float ActionShotChance(RingActionShots level)
        {
            switch (level)
            {
                case RingActionShots.Off: return 0f;
                case RingActionShots.Rare: return 0.2f;
                case RingActionShots.Often: return 0.5f;
                default: return 0.34f;
            }
        }

        public readonly Dictionary<RingAction, string> Keys = DefaultKeys();

        public static Dictionary<RingAction, string> DefaultKeys()
        {
            return new Dictionary<RingAction, string>
            {
                { RingAction.Up, "UpArrow" }, { RingAction.Down, "DownArrow" },
                { RingAction.Left, "LeftArrow" }, { RingAction.Right, "RightArrow" },
                { RingAction.Confirm, "Return" }, { RingAction.Back, "Escape" },
                { RingAction.TabPrev, "Q" }, { RingAction.TabNext, "E" }, { RingAction.Log, "L" },
            };
        }

        public RingSettingsValues Clone()
        {
            // Глибока копія: словник клавіш — свій у кожної копії (чернетка не змінює застосоване).
            var fresh = new RingSettingsValues();
            fresh.CopyFrom(this);
            return fresh;
        }

        public void CopyFrom(RingSettingsValues o)
        {
            Display = o.Display; Resolution = o.Resolution; VSync = o.VSync; FrameCap = o.FrameCap; Graphics = o.Graphics;
            Master = o.Master; Music = o.Music; Sfx = o.Sfx; Voice = o.Voice; MuteInBackground = o.MuteInBackground;
            ReduceMotion = o.ReduceMotion; ScreenShake = o.ScreenShake; Flashes = o.Flashes; UiScale = o.UiScale;
            Language = o.Language;
            Qte = o.Qte; ActionShots = o.ActionShots; FinisherSlowMo = o.FinisherSlowMo;
            if (!ReferenceEquals(Keys, o.Keys))
            {
                Keys.Clear();
                foreach (var kv in o.Keys) Keys[kv.Key] = kv.Value;
            }
        }

        public bool SameAs(RingSettingsValues o)
        {
            if (Display != o.Display || Resolution != o.Resolution || VSync != o.VSync || FrameCap != o.FrameCap || Graphics != o.Graphics) return false;
            if (Master != o.Master || Music != o.Music || Sfx != o.Sfx || Voice != o.Voice || MuteInBackground != o.MuteInBackground) return false;
            if (ReduceMotion != o.ReduceMotion || ScreenShake != o.ScreenShake || Flashes != o.Flashes || UiScale != o.UiScale) return false;
            if (!string.Equals(Language, o.Language, StringComparison.Ordinal)) return false;
            if (Qte != o.Qte || ActionShots != o.ActionShots || FinisherSlowMo != o.FinisherSlowMo) return false;
            if (Keys.Count != o.Keys.Count) return false;
            foreach (var kv in Keys)
            {
                string v;
                if (!o.Keys.TryGetValue(kv.Key, out v) || !string.Equals(v, kv.Value, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>Привести до допустимих значень (зіпсований чи старий запис у сховищі).</summary>
        public void Clamp()
        {
            Master = ClampVol(Master); Music = ClampVol(Music); Sfx = ClampVol(Sfx); Voice = ClampVol(Voice);
            if (Array.IndexOf(FrameCaps, FrameCap) < 0) FrameCap = 60;
            if (Array.IndexOf(UiScales, UiScale) < 0) UiScale = 100;
            if (Resolution < -1) Resolution = -1;
            if (!Enum.IsDefined(typeof(RingDisplayMode), Display)) Display = RingDisplayMode.Fullscreen;
            if (!Enum.IsDefined(typeof(RingGraphics), Graphics)) Graphics = RingGraphics.Auto;
            if (!Enum.IsDefined(typeof(RingActionShots), ActionShots)) ActionShots = RingActionShots.Normal;
            if (string.IsNullOrEmpty(Language)) Language = "uk";
            var def = DefaultKeys();
            foreach (var kv in def)
                if (!Keys.ContainsKey(kv.Key) || string.IsNullOrEmpty(Keys[kv.Key])) Keys[kv.Key] = kv.Value;
        }

        private static int ClampVol(int v) { return v < 0 ? 0 : v > VolumeSteps ? VolumeSteps : v; }
    }

    /// <summary>
    /// Чернетка й застосоване (ТЗ B08 §4): гравець змінює чернетку; гучність чути одразу (хост слухає <see cref="Draft"/>),
    /// «Застосувати» переносить у застосоване й пише в сховище, «Скасувати» повертає, «За замовчуванням» скидає чернетку.
    /// Вихід із незбереженими змінами — діалог (<see cref="IsDirty"/>).
    /// </summary>
    public sealed class RingSettingsModel
    {
        private const string Prefix = "ring.settings.";
        private const string KeyPrefix = "ring.key.";

        /// <summary>Ключ сховища, за яким видно, що гравець уже застосовував налаштування (тоді старт гри застосовує й режим вікна).</summary>
        public const string SavedMarkerKey = Prefix + "display";

        public RingSettingsValues Applied { get; private set; }
        public RingSettingsValues Draft { get; private set; }

        public RingSettingsModel() : this(new RingSettingsValues()) { }

        public RingSettingsModel(RingSettingsValues applied)
        {
            Applied = applied;
            Applied.Clamp();
            Draft = Applied.Clone();
        }

        public bool IsDirty { get { return !Draft.SameAs(Applied); } }

        /// <summary>Відкрили екран налаштувань: чернетка = застосоване.</summary>
        public void BeginEdit() { Draft = Applied.Clone(); }

        public void Apply(IRingSettingsStore store)
        {
            Draft.Clamp();
            Applied = Draft.Clone();
            if (store != null) Write(Applied, store);
        }

        public void Cancel() { Draft = Applied.Clone(); }

        /// <summary>Скинути чернетку до заводських (застосовується лише після «Застосувати»).</summary>
        public void ResetDraft() { Draft = new RingSettingsValues(); }

        /// <summary>
        /// Призначити клавішу дії. Якщо клавіша вже в іншої дії — дії міняються клавішами (жодна не лишається без клавіші).
        /// Повертає дію, з якою помінялись, або null.
        /// </summary>
        public RingAction? Rebind(RingAction action, string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return null;
            string old;
            Draft.Keys.TryGetValue(action, out old);
            RingAction? swapped = null;
            foreach (var kv in Draft.Keys)
            {
                if (kv.Key == action || !string.Equals(kv.Value, keyName, StringComparison.Ordinal)) continue;
                swapped = kv.Key;
                break;
            }
            if (swapped.HasValue) Draft.Keys[swapped.Value] = old;
            Draft.Keys[action] = keyName;
            return swapped;
        }

        // ---------------- сховище ----------------

        public static RingSettingsModel Load(IRingSettingsStore store)
        {
            var v = new RingSettingsValues();
            if (store != null) Read(v, store);
            return new RingSettingsModel(v);
        }

        public static void Write(RingSettingsValues v, IRingSettingsStore s)
        {
            s.Set(Prefix + "display", ((int)v.Display).ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "resolution", v.Resolution.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "vsync", v.VSync ? "1" : "0");
            s.Set(Prefix + "fps", v.FrameCap.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "graphics", ((int)v.Graphics).ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "vol.master", v.Master.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "vol.music", v.Music.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "vol.sfx", v.Sfx.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "vol.voice", v.Voice.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "bgmute", v.MuteInBackground ? "1" : "0");
            s.Set(Prefix + "reducemotion", v.ReduceMotion ? "1" : "0");
            s.Set(Prefix + "shake", v.ScreenShake ? "1" : "0");
            s.Set(Prefix + "flashes", v.Flashes ? "1" : "0");
            s.Set(Prefix + "uiscale", v.UiScale.ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "language", v.Language ?? "uk");
            s.Set(Prefix + "qte", v.Qte ? "1" : "0");
            s.Set(Prefix + "actionshots", ((int)v.ActionShots).ToString(CultureInfo.InvariantCulture));
            s.Set(Prefix + "slowmo", v.FinisherSlowMo ? "1" : "0");
            foreach (var kv in v.Keys) s.Set(KeyPrefix + kv.Key, kv.Value);
            s.Save();
        }

        public static void Read(RingSettingsValues v, IRingSettingsStore s)
        {
            v.Display = (RingDisplayMode)Int(s, Prefix + "display", (int)v.Display);
            v.Resolution = Int(s, Prefix + "resolution", v.Resolution);
            v.VSync = Int(s, Prefix + "vsync", v.VSync ? 1 : 0) != 0;
            v.FrameCap = Int(s, Prefix + "fps", v.FrameCap);
            v.Graphics = (RingGraphics)Int(s, Prefix + "graphics", (int)v.Graphics);
            v.Master = Int(s, Prefix + "vol.master", v.Master);
            v.Music = Int(s, Prefix + "vol.music", v.Music);
            v.Sfx = Int(s, Prefix + "vol.sfx", v.Sfx);
            v.Voice = Int(s, Prefix + "vol.voice", v.Voice);
            v.MuteInBackground = Int(s, Prefix + "bgmute", v.MuteInBackground ? 1 : 0) != 0;
            v.ReduceMotion = Int(s, Prefix + "reducemotion", v.ReduceMotion ? 1 : 0) != 0;
            v.ScreenShake = Int(s, Prefix + "shake", v.ScreenShake ? 1 : 0) != 0;
            v.Flashes = Int(s, Prefix + "flashes", v.Flashes ? 1 : 0) != 0;
            v.UiScale = Int(s, Prefix + "uiscale", v.UiScale);
            v.Qte = Int(s, Prefix + "qte", v.Qte ? 1 : 0) != 0;
            v.ActionShots = (RingActionShots)Int(s, Prefix + "actionshots", (int)v.ActionShots);
            v.FinisherSlowMo = Int(s, Prefix + "slowmo", v.FinisherSlowMo ? 1 : 0) != 0;
            string lang = s.Get(Prefix + "language");
            if (!string.IsNullOrEmpty(lang)) v.Language = lang;
            foreach (RingAction a in Enum.GetValues(typeof(RingAction)))
            {
                string k = s.Get(KeyPrefix + a);
                if (!string.IsNullOrEmpty(k)) v.Keys[a] = k;
            }
            v.Clamp();
        }

        private static int Int(IRingSettingsStore s, string key, int fallback)
        {
            string raw = s.Get(key);
            int r;
            return raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }
    }

    /// <summary>
    /// Застосовані налаштування подачі матчу для решти гри — режисер бою, камера, FX (як <c>RingAccessibility</c> для
    /// доступності). Ставить <c>RingSettingsRuntime.ApplyAll</c> на старті й після «Застосувати»; читати в момент події
    /// (почався прийом, фінішер), не кешувати. Без запуску меню (тести, огляди) — заводські значення.
    /// </summary>
    public static class RingMatchPrefs
    {
        /// <summary>Показувати QTE-натиск у прийомі. Вимкнено — натиску немає й ажіотажу від нього теж (результату кидка він не змінює ніколи).</summary>
        public static bool Qte = true;
        /// <summary>Рівень частоти екшн-кадрів.</summary>
        public static RingActionShots ActionShots = RingActionShots.Normal;
        /// <summary>Сповільнення часу на фінішері.</summary>
        public static bool FinisherSlowMo = true;

        /// <summary>Частка накопичувача екшн-кадрів для поточного рівня (0 — кадрів немає).</summary>
        public static float ActionShotChance { get { return RingSettingsValues.ActionShotChance(ActionShots); } }

        public static void SetFrom(RingSettingsValues v)
        {
            Qte = v.Qte;
            ActionShots = v.ActionShots;
            FinisherSlowMo = v.FinisherSlowMo;
        }
    }
}
