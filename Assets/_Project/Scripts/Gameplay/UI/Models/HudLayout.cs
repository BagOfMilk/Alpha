using System;

namespace Game.Gameplay.UI
{
    /// <summary>Прямокутник у пікселях екрана, початок — лівий верхній кут (як у IMGUI і UI Toolkit).</summary>
    public struct HudBox
    {
        public float X, Y, Width, Height;

        public HudBox(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public bool Contains(float px, float py) => px >= X && px < X + Width && py >= Y && py < Y + Height;

        public bool Overlaps(HudBox o) => X < o.X + o.Width && o.X < X + Width && Y < o.Y + o.Height && o.Y < Y + Height;
    }

    /// <summary>Розкладка шапки і стрічки для одного розміру екрана — у пікселях.</summary>
    public struct HudFrame
    {
        /// <summary>Множник «одиниць» еталона 1280×720 у пікселі (HUD_DESIGN §7).</summary>
        public float Scale;
        public HudBox Header;
        public HudBox Feed;
        /// <summary>Решта екрана під IMGUI-вміст (хаб, ніч, данж) — НЕ перетинається з шапкою і стрічкою (критерій 5 §8).</summary>
        public HudBox Body;
    }

    /// <summary>
    /// Єдина розкладка шапки і стрічки (docs/HUD_DESIGN.md §5.1, §7) — чиста
    /// математика без рушія. Нею користуються обидва шари: UI Toolkit ставить
    /// розміри елементів в одиницях (панель множить на <see cref="HudFrame.Scale"/>),
    /// IMGUI бере <see cref="HudFrame.Body"/> у пікселях. Так дві технології
    /// ділять екран без перетину, і клік одного не падає в іншу.
    /// </summary>
    public static class HudLayout
    {
        public const float ReferenceWidth = 1280f;
        public const float ReferenceHeight = 720f;
        public const float MinScale = 1f;
        public const float MaxScale = 2f;

        /// <summary>Висота шапки в одиницях еталона 1280×720.</summary>
        public const float HeaderUnits = 52f;
        /// <summary>Ширина стрічки в одиницях: 290 на 1280 px, ≈360 на 1600 (§5.1).</summary>
        public const float FeedUnits = 290f;
        /// <summary>На прогулянці стрічка коротка — не більше цієї частки висоти (решта екрана — село).</summary>
        public const float ExploreFeedHeightShare = 0.36f;
        /// <summary>Відступ стрічки від краю на прогулянці, одиниці.</summary>
        public const float ExploreFeedMarginUnits = 12f;

        /// <summary>scale = clamp(min(W/1280, H/720), 1, 2) — від меншої сторони, щоб ультраширокі й 16:10 не обрізали текст (§7).</summary>
        public static float ScaleFor(float width, float height)
        {
            if (width <= 0f || height <= 0f) return MinScale;
            float s = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
            return Math.Max(MinScale, Math.Min(MaxScale, s));
        }

        /// <summary>Розкладка екрана міста: шапка на всю ширину, стрічка праворуч до низу, тіло — решта.</summary>
        public static HudFrame For(float width, float height, bool exploring = false) =>
            ForScale(width, height, exploring, ScaleFor(width, height));

        /// <summary>
        /// Те саме з явним масштабом: IMGUI малює у віртуальному екрані (множник інтерфейсу <c>UiScale</c>), а
        /// масштаб бере справжнього — тоді шапка IMGUI і шапка UI Toolkit однієї висоти.
        /// </summary>
        public static HudFrame ForScale(float width, float height, bool exploring, float scale)
        {
            float headerH = (float)Math.Round(HeaderUnits * scale);
            float feedW = (float)Math.Round(Math.Min(FeedUnits * scale, width * 0.4f));

            var frame = new HudFrame
            {
                Scale = scale,
                Header = new HudBox(0f, 0f, width, headerH)
            };

            if (exploring)
            {
                float margin = (float)Math.Round(ExploreFeedMarginUnits * scale);
                float feedH = (float)Math.Round((height - headerH) * ExploreFeedHeightShare);
                frame.Feed = new HudBox(width - feedW - margin, headerH + margin, feedW, feedH);
                frame.Body = new HudBox(0f, headerH, width, Math.Max(0f, height - headerH));
            }
            else
            {
                frame.Feed = new HudBox(width - feedW, headerH, feedW, Math.Max(0f, height - headerH));
                frame.Body = new HudBox(0f, headerH, Math.Max(0f, width - feedW), Math.Max(0f, height - headerH));
            }
            return frame;
        }

        /// <summary>Пікселі → одиниці панелі (панель UI Toolkit множить одиниці на Scale).</summary>
        public static float ToUnits(float pixels, float scale) => scale > 0f ? pixels / scale : pixels;
    }
}
