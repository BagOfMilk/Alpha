using System;
using System.Collections.Generic;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Гучність шарів звуку (меню паузи) і черга тактів інтерфейсу (віха M1.19). Чистий C#: кнопки IMGUI й
    /// UI Toolkit кладуть такт у чергу (<see cref="Request"/>), режисер звуку розбирає її щокадру. Збереження
    /// гучностей між запусками — справа режисера (<see cref="Dirty"/>).
    /// </summary>
    public static class SoundSettings
    {
        public const float Step = 0.1f;
        private static readonly float[] Volumes = new float[Enum.GetValues(typeof(SoundBus)).Length];
        private static readonly Queue<SoundCue> Pending = new Queue<SoundCue>();
        private static bool _initialized;

        /// <summary>Гучності змінились і ще не збережені.</summary>
        public static bool Dirty { get; set; }

        private static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            foreach (SoundBus b in Enum.GetValues(typeof(SoundBus)))
                Volumes[(int)b] = b == SoundBus.Master ? 1f : SoundCueTable.DefaultVolume(b);
        }

        public static float Get(SoundBus bus)
        {
            Init();
            return Volumes[(int)bus];
        }

        public static void Set(SoundBus bus, float value)
        {
            Init();
            float v = value < 0f ? 0f : value > 1f ? 1f : value;
            v = (float)Math.Round(v, 2);
            if (Math.Abs(Volumes[(int)bus] - v) < 0.001f) return;
            Volumes[(int)bus] = v;
            Dirty = true;
        }

        public static void Nudge(SoundBus bus, int direction) => Set(bus, Get(bus) + direction * Step);

        /// <summary>Підсумкова гучність такту: шар × загальна.</summary>
        public static float Effective(SoundBus bus) => Get(SoundBus.Master) * (bus == SoundBus.Master ? 1f : Get(bus));

        /// <summary>Відсоток для показу в меню.</summary>
        public static int Percent(SoundBus bus) => (int)Math.Round(Get(bus) * 100f);

        public static string TextKey(SoundBus bus) => "ui.sound." + bus.ToString().ToLowerInvariant();

        public static void Request(SoundCue cue)
        {
            if (cue == SoundCue.None) return;
            if (Pending.Count < 8) Pending.Enqueue(cue); // десять кнопок за кадр — досить восьми звуків
        }

        public static bool TryDequeue(out SoundCue cue)
        {
            if (Pending.Count == 0) { cue = SoundCue.None; return false; }
            cue = Pending.Dequeue();
            return true;
        }
    }
}
