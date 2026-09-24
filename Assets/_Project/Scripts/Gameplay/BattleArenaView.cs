using System;
using System.Collections.Generic;
using Game.Core.Session.Views;

namespace Game.Gameplay
{
    /// <summary>
    /// Озброєна дія гравця в бою. <see cref="None"/> — «розумний клік»: тайл
    /// у зоні досяжності = рух, ворог = атака (серед явних кнопок HUD —
    /// лише Дозор/Кінець ходу/Автобій, §Input TEST_BUILD.md). Тут, а не в
    /// <c>BattleArenaController.cs</c>, — щоб <see cref="IBattleHudData"/>
    /// (і лінт <c>BattleHudScreen.cs</c>) бачили тип без лінт-виключеного файлу.
    /// </summary>
    public enum ArmedAction { None, Ability, OverwatchAim }

    /// <summary>
    /// Зріз <see cref="BattleArenaController"/>, потрібний
    /// <see cref="Game.Gameplay.UI.BattleHudScreen"/> для малювання. Окремий
    /// інтерфейс, а не прямий тип контролера, — тому що контролер (
    /// <c>MonoBehaviour</c>, Physics/Renderer) лінт-виключений, а HUD-екран
    /// (чистий IMGUI) — ні: лінт мусить бачити тип параметра
    /// <c>BattleHudScreen.Draw</c>, не бачачи самого класу-реалізації.
    /// </summary>
    public interface IBattleHudData
    {
        BattleView View { get; }
        bool ResultPending { get; }
        string ResultOutcomeKey { get; }
        string ResultRounds { get; }
        IReadOnlyList<string> ResultCasualtyLines { get; }
        IReadOnlyList<string> LogLines { get; }
        ArmedAction Armed { get; }
        string ArmedAbilityId { get; }
        string HoveredUnitId { get; }
        int HoveredHitChance { get; }
        bool IsPlayerTurn { get; }

        string ResolveDisplayName(BattleUnitView unit);
        void ArmAbility(string abilityId);
        void ArmOverwatchAim();
        void CancelArmed();
        void RequestEndTurn();
        void RequestAutoResolve();
        void AcknowledgeResult();
    }

    /// <summary>
    /// Чиста математика показу бою (пакет E2): тайл→світ, підсвітка тайлів,
    /// кадрування камери, детермінована палітра юнітів. ЖОДНОГО типу движка
    /// (той самий принцип, що й <see cref="VillageView"/>) — свої легкі
    /// структури замість <c>UnityEngine.Vector3</c>/<c>Color</c>, тому файл
    /// компілюється й лінтом, і headless-тестами без жодної заглушки.
    ///
    /// <see cref="BattleArenaController"/> (Gameplay, MonoBehaviour,
    /// лінт-виключений — Physics/Renderer/Camera) лише застосовує те, що тут
    /// пораховано: жодної раскладкової математики в самому контролері.
    /// </summary>
    public static class BattleArenaView
    {
        /// <summary>
        /// Фіксований каталог здібностей (§Core/Combat/DefaultCombatContent.AbilityCatalog) —
        /// BattleView не показує, які здібності доступні поточному юніту
        /// (відомий розрив, див. звіт пакета E2); HUD пропонує всі чотири й
        /// покладається на CombatActionResult ядра, щоб відхилити недоступну.
        /// Id — водночас і параметр CombatUseAbility, і ключ UkrainianText
        /// (AbilityDefinition.Id = "ability.lunge" і т. д., збігається навмисно).
        /// </summary>
        public static readonly string[] KnownAbilityIds =
        {
            "ability.lunge", "ability.set_trap", "ability.move_order", "ability.volley"
        };

        /// <summary>Розмір однієї клітини гріда в світових одиницях.</summary>
        public const float TileSize = 1f;

        /// <summary>Висота, на якій «плаває» підпис імені над юнітом.</summary>
        public const float NameLabelHeight = 2.1f;

        /// <summary>Наскільки нижче нормальної пози опускається повалений юніт (для читності стану на кадрі).</summary>
        public const float DownedSink = 0.25f;

        // ================= тайл → світ =================

        /// <summary>Центр клітини (x,y) грида у світових координатах (Y=0 — площина арени).</summary>
        public static WorldPos TileToWorld(int x, int y, float tileSize = TileSize)
            => new WorldPos((x + 0.5f) * tileSize, 0f, (y + 0.5f) * tileSize);

        public static WorldPos TileToWorld(GridPosView pos, float tileSize = TileSize)
            => TileToWorld(pos.X, pos.Y, tileSize);

        // ================= кадрування камери =================

        /// <summary>
        /// Камера арени — ортографічна, згори (§ BattleArenaBuilder). Центр —
        /// середина грида, розмір — половина довшої сторони плюс запас, щоб
        /// крайні тайли не впиралися в рамку екрана.
        /// </summary>
        public static CameraFrame FrameGrid(int width, int height, float tileSize = TileSize, float padding = 1.5f)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);

            float centerX = w * tileSize * 0.5f;
            float centerZ = h * tileSize * 0.5f;
            float longer = Math.Max(w, h) * tileSize * 0.5f;

            return new CameraFrame(centerX, centerZ, longer + padding);
        }

        // ================= тайли: колір за станом =================

        /// <summary>
        /// Пріоритет підсвітки (найвищий переможе): поточний юніт стоїть тут
        /// &gt; тайл під курсором &gt; досяжний тайл &gt; власний колір укриття
        /// &gt; непрохідний. Один тайл ніколи не показує два сенси одразу —
        /// гравець читає найважливіший.
        /// </summary>
        public static TileTint TintFor(string cover, bool walkable, bool isReachable, bool isCurrentUnit, bool isHovered)
        {
            if (!walkable) return new TileTint(0.07f, 0.06f, 0.05f, 1f);
            if (isCurrentUnit) return new TileTint(0.95f, 0.83f, 0.32f, 1f);
            if (isHovered) return new TileTint(0.70f, 0.74f, 0.95f, 1f);
            if (isReachable)
            {
                // Фікс-ревью (ціль А «HUD/арена», owner: "reachable tiles as a
                // subtle translucent overlay"): тайл — ОДИН Quad із матеріалом
                // Opaque URP/Lit (BattleArenaController._tileMaterial, спільний
                // на всі тайли — і на укриття, і на "поточний"/"наведений", де
                // альфа=1 таки мусить лишатись суцільною). Opaque-поверхня
                // альфа-канал _BaseColor ІГНОРУЄ на рендері — попередня версія
                // (альфа 0.92, потім 0.45) не була видно взагалі: "прозорий"
                // тайл рендерився суцільним кольором підсвітки, ховаючи
                // укриття під собою так само, як і повна заливка. Замість
                // реальної альфа-змішки (окремий blend-матеріал зламав би
                // сортування копланарних тайлів під ортографічною камерою
                // згори) — змішуємо колір ТУТ, у C#, з базовим кольором
                // укриття: результат непрозорий (альфа завжди 1, як і
                // рендериться насправді), але видима "прозорість" та сама —
                // 45% підсвітки поверх 55% справжнього кольору тайла.
                var baseTint = CoverTint(cover);
                const float highlight = 0.45f;
                float r = Lerp(baseTint.R, 0.46f, highlight);
                float g = Lerp(baseTint.G, 0.80f, highlight);
                float b = Lerp(baseTint.B, 0.48f, highlight);
                return new TileTint(r, g, b, 1f);
            }

            return CoverTint(cover);
        }

        /// <summary>Природний колір самого тайла за типом укриття — трава/земля/камінь замість однакової темної оливи для всього поля.</summary>
        private static TileTint CoverTint(string cover)
        {
            switch (cover)
            {
                case "Full": return new TileTint(0.36f, 0.36f, 0.40f, 1f);  // сірий камінь
                case "Half": return new TileTint(0.45f, 0.40f, 0.26f, 1f);  // суха земля/тин
                default: return new TileTint(0.27f, 0.42f, 0.22f, 1f);      // трава
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ================= юніти: детермінована палітра =================

        /// <summary>
        /// Колір юніта за id/стороною — детермінований (інваріант 1, жодного
        /// <c>Random</c>): іменні персонажі мають підібраний колір, решта —
        /// хеш-відтінок у смузі, властивій їхньому «табору», щоб союзники,
        /// орда, бояри й Бурунда лишались візуально різними групами навіть
        /// без підписів.
        ///
        /// <paramref name="unitId"/> — <c>BattleUnitView.Id</c> (напр. "u_maksym",
        /// "defector_myroslava"); <paramref name="side"/> — "Player"|"Enemy"|
        /// "FromDefector"; <paramref name="displayNameKey"/> — те, що сьогодні
        /// лежить у <c>BattleUnitView.DisplayNameKey</c> (для ворогів це
        /// короткий id визначення на кшталт "horde_scout", НЕ ключ таблиці —
        /// див. <c>BattleArenaController.ResolveNameKey</c>) і використовується
        /// тут лише для визначення «табору» ворога.
        /// </summary>
        public static PaletteColor CharacterTint(string unitId, string side, string displayNameKey)
        {
            string companionId = TryStripKnownPrefix(unitId, "u_") ?? TryStripKnownPrefix(unitId, "defector_");
            bool marked = string.Equals(side, "FromDefector", StringComparison.Ordinal);

            if (companionId != null)
            {
                var curated = CuratedCompanionColor(companionId);
                if (curated.HasValue) return new PaletteColor(curated.Value.R, curated.Value.G, curated.Value.B, marked);

                // Невідомий напарник (не з іменного каста) — союзна смуга відтінків (бірюза/зелень).
                float hue = 0.35f + Hash01(companionId) * 0.20f;
                HsvToRgb(hue, 0.55f, 0.85f, out float r, out float g, out float b);
                return new PaletteColor(r, g, b, marked);
            }

            if (string.Equals(side, "Enemy", StringComparison.Ordinal) || marked)
            {
                string faction = displayNameKey ?? unitId ?? string.Empty;
                float hue; float sat = 0.65f; float val = 0.80f;

                if (faction.IndexOf("burunda", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hue = 0.98f; sat = 0.75f; val = 0.55f; // темно-багряний — окремо стоїть навіть серед ворогів
                }
                else if (faction.IndexOf("tuhar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         faction.IndexOf("boyar", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hue = 0.78f + Hash01(faction) * 0.06f; // бояри Тугара — фіолетова смуга
                }
                else
                {
                    hue = 0.02f + Hash01(faction) * 0.08f; // орда — червоно-жовтогаряча смуга
                }

                HsvToRgb(hue, sat, val, out float er, out float eg, out float eb);
                return new PaletteColor(er, eg, eb, marked);
            }

            // Невідома сторона — нейтральний сірий, видно, що це аномалія, не бляклість навмисно.
            return new PaletteColor(0.6f, 0.6f, 0.6f, marked);
        }

        private static (float R, float G, float B)? CuratedCompanionColor(string companionId)
        {
            switch (companionId)
            {
                case "protagonist": return (0.86f, 0.70f, 0.27f); // тепле золото
                case "maksym": return (0.27f, 0.56f, 0.31f);      // лісова зелень
                case "myroslava": return (0.64f, 0.22f, 0.27f);   // глибокий багрянець
                case "zakhar": return (0.36f, 0.46f, 0.56f);      // сталево-синій
                default: return null;
            }
        }

        private static string TryStripKnownPrefix(string id, string prefix)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(prefix)) return null;
            return id.StartsWith(prefix, StringComparison.Ordinal) ? id.Substring(prefix.Length) : null;
        }

        // ================= хеш і HSV (без UnityEngine.Random — інваріант 1) =================

        /// <summary>Стабільний хеш рядка в [0,1) — FNV-1a, той самий результат щоразу для того самого id.</summary>
        public static float Hash01(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;

            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++)
                {
                    h ^= s[i];
                    h *= 16777619;
                }
                return (h % 100000u) / 100000f;
            }
        }

        public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            h = h - (float)Math.Floor(h); // загорнути в [0,1)
            s = Clamp01(s);
            v = Clamp01(v);

            float i = (float)Math.Floor(h * 6f);
            float f = h * 6f - i;
            float p = v * (1f - s);
            float q = v * (1f - f * s);
            float t = v * (1f - (1f - f) * s);

            switch (((int)i) % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ================= AP-бар: скільки з нього зайняте резервом дозору =================

        /// <summary>
        /// Частка смужки AP, зайнята резервом дозору (0..1) — <c>ApReserved</c>
        /// малюється ІНШИМ кольором усередині тієї самої смужки, а не окремим
        /// рядком: гравець одразу бачить, скільки з максимуму «заморожено».
        /// </summary>
        public static float ReservedFraction(int apMax, int apReserved)
        {
            if (apMax <= 0) return 0f;
            return Clamp01((float)apReserved / apMax);
        }

        public static float FilledFraction(int apCurrent, int apMax)
        {
            if (apMax <= 0) return 0f;
            return Clamp01((float)apCurrent / apMax);
        }

        // ================= стани бою: значок -> ключ UkrainianText =================

        /// <summary>
        /// Дебаг §6.1 №32 (24.09.2026): <c>BattleUnitView.Statuses</c> ніде не
        /// малювався в HUD — ключі <c>combat.status.*</c> в UkrainianText вже
        /// існували (E3), але жоден екран їх не читав, тож навіть коли Core
        /// коректно заповнював статуси, гравець їх не бачив. Явне зіставлення
        /// (не PascalCase->snake_case конвертер): нове значення StatusType без
        /// свого рядка тут краще впасти на очевидний фолбек, ніж мовчки дати
        /// зламаний ключ.
        /// </summary>
        public static string StatusLabelKey(string statusTypeName)
        {
            switch (statusTypeName)
            {
                case "Bleeding": return "combat.status.bleeding";
                case "Stunned": return "combat.status.stunned";
                case "Suppressed": return "combat.status.suppressed";
                case "KnockedDown": return "combat.status.knocked_down";
                case "Marked": return "combat.status.marked";
                case "Burning": return "combat.status.burning";
                case "Poisoned": return "combat.status.poisoned";
                default: return null;
            }
        }
    }

    // ================= легкі структури-результати (жодного типу движка) =================

    public readonly struct WorldPos
    {
        public readonly float X, Y, Z;

        public WorldPos(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }

    public readonly struct CameraFrame
    {
        public readonly float CenterX, CenterZ, OrthographicSize;

        public CameraFrame(float centerX, float centerZ, float orthographicSize)
        {
            CenterX = centerX; CenterZ = centerZ; OrthographicSize = orthographicSize;
        }
    }

    /// <summary>RGBA 0..1 — той самий діапазон, що <c>UnityEngine.Color</c>, без залежності від нього.</summary>
    public readonly struct TileTint
    {
        public readonly float R, G, B, A;

        public TileTint(float r, float g, float b, float a)
        {
            R = r; G = g; B = b; A = a;
        }
    }

    /// <summary><see cref="Marked"/> — юніт зі статусом "FromDefector": та сама постать, окрема позначка (кільце/значок), не інший колір.</summary>
    public readonly struct PaletteColor
    {
        public readonly float R, G, B;
        public readonly bool Marked;

        public PaletteColor(float r, float g, float b, bool marked)
        {
            R = r; G = g; B = b; Marked = marked;
        }
    }
}
