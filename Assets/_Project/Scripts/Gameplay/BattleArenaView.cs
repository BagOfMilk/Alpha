using System;
using System.Collections.Generic;
using Game.Core.Session.Views;

namespace Game.Gameplay
{
    // ArmedAction і IBattleHudData переїхали в Gameplay/UI/BattleContract.cs (Бій v2, docs/COMBAT_V2.md §7.4).

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
                // підсвітка поверх справжнього кольору тайла.
                //
                // Бій v2, раунд 2 (доручення власника, п.3): колір і частка —
                // ТОЧНО за таблицею §2 COMBAT_V2.md («Досяжний тайл — синій
                // 0.30, 0.60, 1.00, α 0.35»), а не довільна зелень, що на
                // знімках читалась як «блідо-зелене» й губилась поруч із
                // травою під час ходу гравця.
                var baseTint = CoverTint(cover);
                const float highlight = 0.35f;
                float r = Lerp(baseTint.R, 0.30f, highlight);
                float g = Lerp(baseTint.G, 0.60f, highlight);
                float b = Lerp(baseTint.B, 1.00f, highlight);
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

        // ================= Бій v2: порядкові номери дублікатів (§7.4, §6 ResolveDisplayName) =================

        /// <summary>
        /// Римська цифра порядкового номера (<c>BattleUnitView.Ordinal</c>,
        /// 1→"I", 2→"II", …) — 0 чи менше (ім'я унікальне в бою) дає порожній
        /// рядок. Стандартний алгоритм: вистачає з великим запасом на будь-яку
        /// реалістичну кількість дублікатів одного ворога в одному бою.
        /// </summary>
        public static string OrdinalRoman(int ordinal)
        {
            if (ordinal <= 0) return string.Empty;

            int n = ordinal;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < RomanValues.Length && n > 0; i++)
                while (n >= RomanValues[i])
                {
                    sb.Append(RomanSymbols[i]);
                    n -= RomanValues[i];
                }
            return sb.ToString();
        }

        private static readonly int[] RomanValues = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly string[] RomanSymbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        /// <summary>Ім'я з порядковим номером, якщо він є («Розвідник орди» + 2 → «Розвідник орди II»); ordinal ≤ 0 повертає ім'я як є.</summary>
        public static string WithOrdinal(string name, int ordinal)
        {
            string roman = OrdinalRoman(ordinal);
            return roman.Length == 0 || string.IsNullOrEmpty(name) ? name : name + " " + roman;
        }

        // ================= Бій v2: підсвітка тайла з наміром гравця (§2, §5 «Наведення на недосяжний тайл») =================

        /// <summary>
        /// Розширення <see cref="TintFor"/> під намір гравця: озброєна
        /// здібність підсвічує свою дальність (бузковий), приціл дозору —
        /// свій конус (бірюзовий), тайли шляху під ворожим дозором —
        /// попереджувальний помаранчевий, а наведений тайл поза досяжністю
        /// (<paramref name="isHoveredUnreachable"/>) — тривожний червоний,
        /// НЕ той самий колір, що валідний наведений тайл (REFS «Наведення на
        /// недосяжний тайл підсвічується так само, як на досяжний» — major).
        /// Пріоритет той самий, що в <see cref="TintFor"/>, для решти
        /// випадків (непрохідність/поточний юніт/наведений-досяжний/
        /// досяжний), нові сенси — нижчі за них, вищі за голе укриття.
        /// </summary>
        public static TileTint TintForIntent(string cover, bool walkable, bool isReachable, bool isCurrentUnit,
            bool isHovered, bool isHoveredUnreachable, bool isAbilityRange, bool isOverwatchAim, bool isOverwatchThreat)
        {
            if (!walkable) return TintFor(cover, false, isReachable, isCurrentUnit, isHovered);
            if (isCurrentUnit) return TintFor(cover, true, isReachable, true, isHovered);

            if (isHovered && isHoveredUnreachable && !isReachable)
                return Blend(CoverTint(cover), 0.92f, 0.28f, 0.22f, 0.55f);

            if (isHovered || isReachable) return TintFor(cover, true, isReachable, false, isHovered);

            if (isOverwatchAim) return Blend(CoverTint(cover), 0.35f, 0.85f, 0.95f, 0.40f);
            if (isAbilityRange) return Blend(CoverTint(cover), 0.65f, 0.50f, 0.95f, 0.35f);
            if (isOverwatchThreat) return Blend(CoverTint(cover), 0.95f, 0.55f, 0.15f, 0.30f);

            return CoverTint(cover);
        }

        private static TileTint Blend(TileTint baseTint, float r, float g, float b, float amount)
            => new TileTint(Lerp(baseTint.R, r, amount), Lerp(baseTint.G, g, amount), Lerp(baseTint.B, b, amount), 1f);

        // ================= Бій v2: камера (§4) — чиста тригонометрія, застосовує контролер =================

        /// <summary>
        /// Зсув камери від точки фокусу для ортографічної тактичної камери:
        /// нахил <paramref name="tiltDegrees"/> (0 — вздовж землі, 90 — прямо
        /// згори) і поворот навколо цілі <paramref name="yawDegrees"/> (0,
        /// 90, 180, 270 — Q/E повертають на ±90°). Камера сідає у
        /// <c>focus + результат</c> і дивиться назад на фокус — сам
        /// <c>Quaternion.LookRotation</c> рахує контролер (тип рушія).
        /// </summary>
        public static WorldPos CameraOffsetFromFocus(float tiltDegrees, float yawDegrees, float distance)
        {
            double tilt = tiltDegrees * DegToRad;
            double yaw = yawDegrees * DegToRad;

            float horizontal = (float)(distance * System.Math.Cos(tilt));
            float height = (float)(distance * System.Math.Sin(tilt));
            float dx = (float)(horizontal * System.Math.Sin(yaw));
            float dz = (float)(-horizontal * System.Math.Cos(yaw));
            return new WorldPos(dx, height, dz);
        }

        private const double DegToRad = System.Math.PI / 180.0;

        /// <summary>Утримує точку панорамування камери в межах грида з запасом <paramref name="margin"/> світових одиниць з кожного боку.</summary>
        public static WorldPos ClampPanTarget(float x, float z, int gridWidth, int gridHeight, float margin, float tileSize = TileSize)
        {
            float minX = -margin, maxX = gridWidth * tileSize + margin;
            float minZ = -margin, maxZ = gridHeight * tileSize + margin;
            return new WorldPos(ClampF(x, minX, maxX), 0f, ClampF(z, minZ, maxZ));
        }

        private static float ClampF(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        // ================= Бій v2, раунд 2: кадр рахує вільну від HUD область (§4) =================

        /// <summary>
        /// Прямокутник у GUI-просторі (0,0 — лівий верхній кут, вісь Y вниз,
        /// як у <c>IBattleHudData.SetHudRects</c>) — своя легка структура
        /// замість <c>UnityEngine.Rect</c>, щоб цей файл лишався без жодного
        /// типу рушія (той самий принцип, що <see cref="WorldPos"/>).
        /// </summary>
        public readonly struct GuiRect
        {
            public readonly float X, Y, Width, Height;
            public GuiRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        }

        /// <summary>Відступи вільної (не заслоненої HUD) області від країв екрана, GUI-простір.</summary>
        public readonly struct HudMargins
        {
            public readonly float Top, Bottom, Left, Right;
            public HudMargins(float top, float bottom, float left, float right) { Top = top; Bottom = bottom; Left = left; Right = right; }
        }

        /// <summary>
        /// Розумний дефолт полів HUD, поки перший кадр <c>DrawHud</c> ще не
        /// повідомив справжні прямокутники через <c>SetHudRects</c> (доручення
        /// власника, Бій v2 раунд 2, п.4: «верх 60px, низ 180px, праворуч
        /// 380px на 1080p, пропорційно») — контрольна висота 1080, відступ
        /// масштабується висотою екрана, як і решта розкладки HUD
        /// (<c>Widgets.ScaleForScreen</c>).
        /// </summary>
        public static HudMargins DefaultHudMargins(float screenWidth, float screenHeight)
        {
            float scale = screenHeight > 0f ? screenHeight / 1080f : 1f;
            return new HudMargins(60f * scale, 180f * scale, 0f, 380f * scale);
        }

        /// <summary>
        /// Реальні відступи з прямокутників, які HUD щокадру повідомляє
        /// через <c>SetHudRects</c> (верхня смуга/нижня панель дій/права
        /// панель журналу — банер, оверлеї й спливаючі написи туди свідомо не
        /// входять, §3 «не блокує кліки»): бере лише ті прямокутники, що
        /// впритул до відповідного краю екрана, і повертає, наскільки далеко
        /// вони від нього сягають. Порожній список (перший кадр бою) —
        /// <see cref="DefaultHudMargins"/>.
        ///
        /// Форма прямокутника визначає, якому краю він служить (ширший за
        /// висоту — горизонтальна смуга, верх/низ; вищий за ширину —
        /// вертикальна панель, ліворуч/праворуч): без цього широка верхня
        /// смуга (X=0 ДО самого правого краю) читалась би одночасно і як
        /// «впритул до лівого», і як «впритул до правого» країв, роздуваючи
        /// обидва бокові відступи на всю ширину екрана.
        /// </summary>
        public static HudMargins MarginsFromRects(float screenWidth, float screenHeight, IReadOnlyList<GuiRect> rects)
        {
            if (rects == null || rects.Count == 0) return DefaultHudMargins(screenWidth, screenHeight);

            const float edge = 64f;
            float top = 0f, bottom = 0f, left = 0f, right = 0f;
            for (int i = 0; i < rects.Count; i++)
            {
                var r = rects[i];
                if (r.Width <= 0f || r.Height <= 0f) continue;

                if (r.Width >= r.Height)
                {
                    if (r.Y <= edge) top = Math.Max(top, r.Y + r.Height);
                    else if (r.Y + r.Height >= screenHeight - edge) bottom = Math.Max(bottom, screenHeight - r.Y);
                }
                else
                {
                    if (r.X <= edge) left = Math.Max(left, r.X + r.Width);
                    else if (r.X + r.Width >= screenWidth - edge) right = Math.Max(right, screenWidth - r.X);
                }
            }
            return new HudMargins(top, bottom, left, right);
        }

        /// <summary>Центр вільної (не заслоненої HUD) області екрана, GUI-простір.</summary>
        public static (float X, float Y) FreeAreaCenter(float screenWidth, float screenHeight, HudMargins margins)
        {
            float freeWidth = Math.Max(1f, screenWidth - margins.Left - margins.Right);
            float freeHeight = Math.Max(1f, screenHeight - margins.Top - margins.Bottom);
            return (margins.Left + freeWidth * 0.5f, margins.Top + freeHeight * 0.5f);
        }

        /// <summary>
        /// Ортографічний розмір, що вписує ПОВНИЙ грід (доручення власника:
        /// «Початкове кадрування — увесь грід у вільній області») у вільну від
        /// HUD частину екрана, а не в увесь екран, як голий
        /// <see cref="FrameGrid"/>: за меншою вільною областю потрібен більший
        /// розмір, інакше протилежний від HUD край гріда все одно ховається
        /// під панеллю.
        /// </summary>
        public static float OrthographicSizeForFreeArea(int gridWidth, int gridHeight, float screenWidth, float screenHeight,
            HudMargins margins, float tileSize = TileSize, float padding = 1.5f)
        {
            var baseFrame = FrameGrid(gridWidth, gridHeight, tileSize, padding);
            if (screenHeight <= 0f) return baseFrame.OrthographicSize;

            float freeWidth = Math.Max(1f, screenWidth - margins.Left - margins.Right);
            float freeHeight = Math.Max(1f, screenHeight - margins.Top - margins.Bottom);

            int w = Math.Max(1, gridWidth), h = Math.Max(1, gridHeight);
            float halfW = w * tileSize * 0.5f, halfH = h * tileSize * 0.5f;

            float sizeForHeight = (halfH + padding) * screenHeight / freeHeight;
            float sizeForWidth = (halfW + padding) * screenHeight / freeWidth;
            return Math.Max(baseFrame.OrthographicSize, Math.Max(sizeForHeight, sizeForWidth));
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
