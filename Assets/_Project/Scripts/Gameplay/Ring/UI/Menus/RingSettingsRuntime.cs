using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>Сховище налаштувань гравця — PlayerPrefs (ключі <c>ring.settings.*</c>, <c>ring.key.*</c>; Alpha-ключів не чіпаємо).</summary>
    public sealed class PlayerPrefsSettingsStore : IRingSettingsStore
    {
        public string Get(string key) { return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, null) : null; }
        public void Set(string key, string value) { PlayerPrefs.SetString(key, value ?? ""); }
        public void Save() { PlayerPrefs.Save(); }
    }

    /// <summary>Сховище в пам'яті: огляди (-ring-shots, -ring-menu-shots) не пишуть у налаштування гравця.</summary>
    public sealed class MemorySettingsStore : IRingSettingsStore
    {
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public string Get(string key) { string v; return Data.TryGetValue(key, out v) ? v : null; }
        public void Set(string key, string value) { Data[key] = value; }
        public void Save() { }
    }

    /// <summary>
    /// Налаштування в грі (ТЗ B08 §4): читає збережене при старті, застосовує до екрана, графіки, звуку й доступності;
    /// гучність чернетки чути одразу (<see cref="PreviewAudio"/>). Огляди не змінюють режим вікна (їм його дає командний рядок).
    /// </summary>
    public sealed class RingSettingsRuntime
    {
        public readonly RingSettingsModel Model;
        public readonly IRingSettingsStore Store;
        private readonly bool _touchDisplay;
        private readonly bool _savedByPlayer;
        private readonly int _defaultQuality;
        private List<RingResolution> _resolutions;

        public RingSettingsRuntime(IRingSettingsStore store, bool touchDisplay)
        {
            Store = store;
            _touchDisplay = touchDisplay;
            Model = RingSettingsModel.Load(store);
            // Режим вікна й кадри — лише якщо гравець уже щось застосовував (інакше лишаємо, як запустили: командний рядок,
            // -screen-fullscreen 0 оглядів і плейтесту); «Авто» графіки — рівень, з яким гра стартувала (або -gfx).
            _savedByPlayer = store != null && store.Get(RingSettingsModel.SavedMarkerKey) != null && !HasArg("-screen-fullscreen") && !HasArg("-screen-width");
            _defaultQuality = QualitySettings.GetQualityLevel();
            if (HasArg("-gfx")) { GraphicsTier.InitOnce(); _defaultQuality = QualitySettings.GetQualityLevel(); }
        }

        private static bool HasArg(string name) { return RingLaunchArgs.Has(name); }

        /// <summary>Множник масштабу UI з налаштувань (90–120 %).</summary>
        public float UiScale { get { return Model.Applied.UiScale / 100f; } }

        /// <summary>Роздільності монітора без повторів (ширина × висота), від меншої до більшої.</summary>
        public List<RingResolution> Resolutions
        {
            get
            {
                if (_resolutions != null) return _resolutions;
                _resolutions = new List<RingResolution>();
                var seen = new HashSet<long>();
                foreach (var r in Screen.resolutions)
                {
                    if (r.width < 960 || r.height < 540) continue;
                    long key = (long)r.width * 100000 + r.height;
                    if (seen.Add(key)) _resolutions.Add(new RingResolution { W = r.width, H = r.height });
                }
                _resolutions.Sort((a, b) => a.W != b.W ? a.W.CompareTo(b.W) : a.H.CompareTo(b.H));
                return _resolutions;
            }
        }

        public void ApplyAll() { ApplyAll(false); }

        /// <summary><paramref name="byPlayer"/> — гравець натиснув «Застосувати» (тоді й режим вікна), інакше — старт гри.</summary>
        public void ApplyAll(bool byPlayer)
        {
            var v = Model.Applied;
            ApplyGraphics(v);                                   // рівень якості першим: SetQualityLevel скидає vSyncCount рівня
            if (_touchDisplay && (byPlayer || _savedByPlayer)) ApplyDisplay(v);
            PreviewAudio(v);
            ApplyAccessibility(v);
        }

        /// <summary>Гучність, фоновий звук — одразу (ще до «Застосувати»): гравець чує зміну (ТЗ: «зміну гучності чути одразу»).</summary>
        public void PreviewAudio(RingSettingsValues v) { RingAudioMix.Current.SetFrom(v); }

        public static void ApplyAccessibility(RingSettingsValues v)
        {
            MenuAnimator.Reduced = v.ReduceMotion;
            RingAccessibility.ReduceMotion = v.ReduceMotion;
            RingAccessibility.ScreenShake = v.ScreenShake && !v.ReduceMotion;
            RingAccessibility.Flashes = v.Flashes;
            RingAccessibility.ApplyCommandLine();
        }

        private void ApplyDisplay(RingSettingsValues v)
        {
            try
            {
                var mode = v.Display == RingDisplayMode.Windowed ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
                int w, h;
                var list = Resolutions;
                if (v.Resolution >= 0 && v.Resolution < list.Count) { w = list[v.Resolution].W; h = list[v.Resolution].H; }
                else if (mode == FullScreenMode.Windowed) { w = 1600; h = 900; }
                else { var cur = Screen.currentResolution; w = cur.width; h = cur.height; }
                if (w > 0 && h > 0) Screen.SetResolution(w, h, mode);
                QualitySettings.vSyncCount = v.VSync ? 1 : 0;
                Application.targetFrameRate = v.FrameCap > 0 ? v.FrameCap : -1;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ринг] налаштування екрана не застосовано: " + e.Message);
            }
        }

        private void ApplyGraphics(RingSettingsValues v)
        {
            GraphicsLevel level;
            switch (v.Graphics)
            {
                case RingGraphics.Low: level = GraphicsLevel.Low; break;
                case RingGraphics.Medium: level = GraphicsLevel.Medium; break;
                case RingGraphics.High: level = GraphicsLevel.High; break;
                default:
                    // «Авто» — рівень, з яким гра стартувала (налаштування проєкту або -gfx): без несподіваного пониження
                    if (QualitySettings.GetQualityLevel() != _defaultQuality) QualitySettings.SetQualityLevel(_defaultQuality, true);
                    return;
            }
            // Лише на цей запуск: вибір «Рингу» живе в ring.settings.graphics, ключ Alpha (alpha.gfx) не чіпаємо.
            GraphicsTier.UseForSession(level);
        }

        /// <summary>Назва роздільності для рядка налаштувань (ключ «Рідна» — у тексті гри).</summary>
        public string ResolutionLabel(int index, string nativeLabel)
        {
            var list = Resolutions;
            if (index < 0 || index >= list.Count) return nativeLabel;
            return list[index].W + " × " + list[index].H;
        }
    }

    /// <summary>Прапорці доступності для решти гри (3D-ефекти, камера): ставить <see cref="RingSettingsRuntime"/>.</summary>
    public static class RingAccessibility
    {
        public static bool ReduceMotion;
        public static bool ScreenShake = true;
        public static bool Flashes = true;

        /// <summary>
        /// Огляд без зміни налаштувань гравця: <c>-ring-a11y noflash,noshake,calm</c> (через кому) вимикає спалахи,
        /// тремтіння, стоп-кадри й сповільнення лише на цей запуск — щоб зняти кадри фінішера з перемикачами.
        /// </summary>
        public static void ApplyCommandLine()
        {
            var args = RingLaunchArgs.All();   // у редакторі — ще й Temp/ring-editor.args
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-ring-a11y") continue;
                foreach (var f in args[i + 1].Split(','))
                {
                    if (f == "noflash") Flashes = false;
                    else if (f == "noshake") ScreenShake = false;
                    else if (f == "calm") { ReduceMotion = true; ScreenShake = false; }
                }
            }
        }
    }
}

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>Роздільність екрана (ширина × висота) без рушійного типу.</summary>
    public struct RingResolution
    {
        public int W, H;
    }
}
