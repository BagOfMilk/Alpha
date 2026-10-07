using System;
using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>Звуковий такт (Поправка №18.4, віха M1.19): одна назва — кілька варіантів файлу.</summary>
    public enum SoundCue
    {
        None = 0,
        // Інтерфейс.
        UiClick = 1, UiHover = 2, UiOpen = 3, UiClose = 4, UiConfirm = 5, UiError = 6, UiToggle = 7,
        // Село.
        FootstepGrass = 20, FootstepWood = 21, DoorOpen = 22, DoorClose = 23, Coins = 24, Cloth = 25,
        Chop = 26, Anvil = 27, BookOpen = 28, PageFlip = 29, Bell = 30,
        // Бій.
        DrawWeapon = 40, Swing = 41, HitFlesh = 42, HitArmor = 43, HitCover = 44, Block = 45, Fall = 46,
        // Джинґли.
        JingleGood = 60, JingleBad = 61, JingleNotice = 62, JingleNewDay = 63
    }

    /// <summary>Шар звуку — свій регулятор гучності (меню паузи).</summary>
    public enum SoundBus { Master = 0, Music = 1, Sfx = 2, Ui = 3, Ambience = 4 }

    /// <summary>Музика за станом гри.</summary>
    public enum MusicTrack { None = 0, VillageDay = 1, VillageEvening = 2, Battle = 3 }

    /// <summary>Атмосфера за фазою доби.</summary>
    public enum AmbienceBed { None = 0, Day = 1, Night = 2 }

    /// <summary>
    /// Таблиця звуку (віха M1.19): такт → файли (ключі <c>Assets/ThirdParty/CC0/Audio/audio_manifest.txt</c>),
    /// подія публічного журналу (<c>GameEvent.Key</c>) → такт, стан гри → музика й атмосфера. Чистий C#:
    /// режисер (<c>Gameplay/Audio/AudioDirector</c>) лише застосовує. Звук слухає ЛИШЕ публічні події й стан —
    /// жодних чисел Напруги (інваріант 3); охоронець — <c>SoundCueTableTests</c>.
    /// </summary>
    public static class SoundCueTable
    {
        private static readonly Dictionary<SoundCue, string[]> Files = new Dictionary<SoundCue, string[]>
        {
            { SoundCue.UiClick, K("click_001", "click_002", "click_003") },
            { SoundCue.UiHover, K("rollover1", "rollover2") },
            { SoundCue.UiOpen, K("open_001", "open_002") },
            { SoundCue.UiClose, K("close_001", "close_002") },
            { SoundCue.UiConfirm, K("confirmation_001", "confirmation_002") },
            { SoundCue.UiError, K("error_001", "error_002") },
            { SoundCue.UiToggle, K("toggle_001", "toggle_002") },
            { SoundCue.FootstepGrass, K("footstep_grass_000", "footstep_grass_001", "footstep_grass_002", "footstep_grass_003", "footstep_grass_004") },
            { SoundCue.FootstepWood, K("footstep_wood_000", "footstep_wood_001", "footstep_wood_002") },
            { SoundCue.DoorOpen, K("doorOpen_1", "doorOpen_2") },
            { SoundCue.DoorClose, K("doorClose_1", "doorClose_2") },
            { SoundCue.Coins, K("handleCoins", "handleCoins2") },
            { SoundCue.Cloth, K("cloth1", "cloth2", "cloth3") },
            { SoundCue.Chop, K("chop") },
            { SoundCue.Anvil, K("impactMetal_heavy_000", "impactMetal_heavy_001", "impactMetal_heavy_002") },
            { SoundCue.BookOpen, K("bookOpen") },
            { SoundCue.PageFlip, K("bookFlip1", "bookFlip2") },
            { SoundCue.Bell, K("impactBell_heavy_000") },
            { SoundCue.DrawWeapon, K("drawKnife1", "drawKnife2") },
            { SoundCue.Swing, K("knifeSlice", "knifeSlice2") },
            { SoundCue.HitFlesh, K("impactPunch_heavy_000", "impactPunch_heavy_001", "impactPunch_heavy_002") },
            { SoundCue.HitArmor, K("impactMetal_medium_000", "impactMetal_medium_001") },
            { SoundCue.HitCover, K("impactWood_heavy_000", "impactWood_heavy_001") },
            { SoundCue.Block, K("impactPlate_heavy_000", "impactPlate_heavy_001") },
            { SoundCue.Fall, K("impactSoft_heavy_000", "impactSoft_heavy_001") },
            { SoundCue.JingleGood, K("jingles_HIT00", "jingles_HIT01") },
            { SoundCue.JingleBad, K("jingles_PIZZI00", "jingles_PIZZI01", "jingles_PIZZI02") },
            { SoundCue.JingleNotice, K("jingles_STEEL00", "jingles_STEEL01", "jingles_STEEL02") },
            { SoundCue.JingleNewDay, K("jingles_STEEL03", "jingles_STEEL04") },
        };

        private static string[] K(params string[] names)
        {
            var r = new string[names.Length];
            for (int i = 0; i < names.Length; i++) r[i] = "Kenney/" + names[i];
            return r;
        }

        public static IReadOnlyList<string> FilesFor(SoundCue cue)
        {
            string[] f;
            return Files.TryGetValue(cue, out f) ? f : Array.Empty<string>();
        }

        public static IEnumerable<SoundCue> AllCues()
        {
            foreach (SoundCue c in Enum.GetValues(typeof(SoundCue)))
                if (c != SoundCue.None) yield return c;
        }

        public static SoundBus BusOf(SoundCue cue)
        {
            int v = (int)cue;
            return v < 20 ? SoundBus.Ui : SoundBus.Sfx;
        }

        /// <summary>Варіант файлу — детерміновано від лічильника (жодної випадковості, але й не одне й те саме).</summary>
        public static string Pick(SoundCue cue, int counter)
        {
            var f = FilesFor(cue);
            if (f.Count == 0) return null;
            int i = counter % f.Count;
            return f[i < 0 ? -i : i];
        }

        /// <summary>
        /// Подія журналу → такт. Ключі — з <c>GameSession.LogEvent</c> (публічний журнал). null-ключ чи
        /// невідома подія — без звуку. Префіксні ключі (<c>companion.rescued.</c>) збігаються за початком.
        /// </summary>
        public static SoundCue ForEvent(string key)
        {
            if (string.IsNullOrEmpty(key)) return SoundCue.None;
            switch (key)
            {
                case "equip.changed": return SoundCue.Cloth;
                case "forge.made": return SoundCue.Anvil;
                case "craft.upgraded": return SoundCue.Anvil;
                case "game.saved": return SoundCue.UiConfirm;
                case "game.loaded": return SoundCue.UiConfirm;
                case "city.building.ordered": return SoundCue.Chop;
                case "city.granted": return SoundCue.Bell;
                case "day.advanced": return SoundCue.JingleNewDay;
                case "council.decree":
                case "council.diplomacy":
                case "council.invest":
                case "council.raid.ordered":
                case "council.settlers.ordered":
                case "council.prepare_threat":
                case "council.outfit_expedition": return SoundCue.PageFlip;
                case "quest.offered": return SoundCue.BookOpen;
                case "expedition.departed":
                case "dungeon.depart": return SoundCue.DoorOpen;
                case "expedition.returned":
                case "dungeon.extract": return SoundCue.DoorClose;
                case "loot.dropped": return SoundCue.Coins;
                case "combat.battle.started":
                case "combat.training.started": return SoundCue.DrawWeapon;
                case "combat.attack.hit": return SoundCue.HitFlesh;
                case "combat.attack.crit": return SoundCue.HitArmor;
                case "combat.attack.graze": return SoundCue.Block;
                case "combat.attack.miss": return SoundCue.Swing;
                case "combat.overwatch.triggered": return SoundCue.Swing;
                case "companion.died": return SoundCue.Fall;
                case "companion.defected":
                case "companion.captured":
                case "dungeon.wiped":
                case "production.food_shortage":
                case "crisis.test.unmitigated": return SoundCue.JingleBad;
                case "enemy.surrendered":
                case "progression.level_up":
                case "crisis.test.mitigated":
                case "arc.chapter_completed": return SoundCue.JingleGood;
                case "crisis.test.warn":
                case "loyalty.band_changed":
                case "faction.standing_changed":
                case "arrivals.tavern.announced": return SoundCue.JingleNotice;
            }
            if (key.StartsWith("companion.rescued", StringComparison.Ordinal)) return SoundCue.JingleGood;
            return SoundCue.None;
        }

        /// <summary>Музика: бій — бойова; вечір і ніч — таверна; решта — день села; титул — таверна.</summary>
        public static MusicTrack MusicFor(bool inBattle, bool evening, bool title)
        {
            if (inBattle) return MusicTrack.Battle;
            if (title || evening) return MusicTrack.VillageEvening;
            return MusicTrack.VillageDay;
        }

        public static string FileOf(MusicTrack track)
        {
            switch (track)
            {
                case MusicTrack.VillageDay: return "Music/Loop_The_Bards_Tale";
                case MusicTrack.VillageEvening: return "Music/Loop_The_Old_Tower_Inn";
                case MusicTrack.Battle: return "Music/determined_pursuit_loop";
                default: return null;
            }
        }

        public static AmbienceBed AmbienceFor(bool inBattle, bool night, bool title)
        {
            if (title) return AmbienceBed.None;
            return night ? AmbienceBed.Night : AmbienceBed.Day;
        }

        public static string FileOf(AmbienceBed bed)
        {
            switch (bed)
            {
                case AmbienceBed.Day: return "Ambience/birds_and_wind";
                case AmbienceBed.Night: return "Ambience/crickets";
                default: return null;
            }
        }

        /// <summary>Вогнище Віча — завжди тихо тріщить під атмосферою села.</summary>
        public const string FireLoop = "Ambience/fire";

        /// <summary>Гучність шару за замовчуванням (0..1).</summary>
        public static float DefaultVolume(SoundBus bus)
        {
            switch (bus)
            {
                case SoundBus.Music: return 0.55f;
                case SoundBus.Ambience: return 0.6f;
                case SoundBus.Ui: return 0.7f;
                default: return 0.8f;
            }
        }
    }
}
