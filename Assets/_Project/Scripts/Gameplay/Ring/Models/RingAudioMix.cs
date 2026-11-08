using System;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Аудіо-шини «Рингу» (ТЗ B08 §5): гучність кожного звуку = Master × своя шина.</summary>
    public enum RingBus { Master = 0, Music, Sfx, Voice }

    /// <summary>Музика меню — один лейтмотив у трьох варіаціях (docs/ring/audio/MENU_AUDIO.md).</summary>
    public enum MenuMusic { None = 0, Login, Loading, Settings }

    /// <summary>Звуки меню — один тембр (docs/ring/audio/MENU_AUDIO.md). Порядок = порядок кліпів у RingMenuAssets.</summary>
    public enum MenuSfx { Move = 0, Confirm, Back, Error, Toggle, Slider, Tab, Transition, LoadDone, LoginOk }

    /// <summary>
    /// Програвач меню (реалізація — Unity-компонент <c>RingMenuAudio</c>). Екрани кличуть його через
    /// <see cref="RingMenuServices.Audio"/>; без компонента (тести, лінт, сцена без звуку) виклики нічого не роблять.
    /// </summary>
    public interface IRingMenuAudio
    {
        /// <summary>Перейти на трек меню з кросфейдом (<see cref="RingAudioMix.CrossfadeSeconds"/>); None — плавно вимкнути.</summary>
        void PlayMusic(MenuMusic track);

        void Sfx(MenuSfx cue);

        /// <summary>Тестовий звук шини після зміни гучності в налаштуваннях (музику чути й так — вона грає).</summary>
        void TestBus(RingBus bus);
    }

    /// <summary>Точка доступу екранів до сервісів сцени (звук). Ставить компонент сцени в Awake, знімає в OnDestroy.</summary>
    public static class RingMenuServices
    {
        public static IRingMenuAudio Audio;

        /// <summary>Зараз меню (вступ, вхід, налаштування, завантаження): музику веде меню, а не музика зали RingSound.</summary>
        public static bool MenuMusicActive;

        /// <summary>Музика зали RingSound (true — грати, false — стихнути); ставить RingSound.</summary>
        public static Action<bool> GameMusic;

        public static void Play(MenuSfx cue) { if (Audio != null) Audio.Sfx(cue); }

        public static void Music(MenuMusic m) { if (Audio != null) Audio.PlayMusic(m); }
    }

    /// <summary>
    /// Міксер шин кодом (AudioMixer-асет кодом не створити — його групи живуть у внутрішньому API редактора; тому, як і в
    /// Alpha, гучність рахується множенням). Кроки 0…10 → підсилення з кроком 3 дБ: 10 = 0 дБ, 5 = −15 дБ, 1 = −27 дБ, 0 = тиша —
    /// рівномірно на слух, на відміну від лінійної шкали, де нижня половина повзунка майже не чутна.
    /// </summary>
    public sealed class RingAudioMix
    {
        public const float CrossfadeSeconds = 0.8f;
        public const float DbPerStep = 3f;

        /// <summary>Поточний міксер сцени (налаштування пишуть сюди, звук рингу й меню читають).</summary>
        public static RingAudioMix Current = new RingAudioMix();

        private readonly int[] _steps = { 8, 6, 8, 8 };

        /// <summary>Глушити, коли вікно без фокусу (налаштування «Звук у фоні»).</summary>
        public bool MuteInBackground = true;

        /// <summary>Вікно зараз у фокусі (ставить Unity-компонент з OnApplicationFocus).</summary>
        public bool Focused = true;

        public int Step(RingBus bus) { return _steps[(int)bus]; }

        public void SetStep(RingBus bus, int step)
        {
            _steps[(int)bus] = step < 0 ? 0 : step > RingSettingsValues.VolumeSteps ? RingSettingsValues.VolumeSteps : step;
        }

        public void SetFrom(RingSettingsValues v)
        {
            SetStep(RingBus.Master, v.Master);
            SetStep(RingBus.Music, v.Music);
            SetStep(RingBus.Sfx, v.Sfx);
            SetStep(RingBus.Voice, v.Voice);
            MuteInBackground = v.MuteInBackground;
        }

        /// <summary>Підсилення кроку 0…10 (лінійне, 0…1).</summary>
        public static float StepGain(int step)
        {
            if (step <= 0) return 0f;
            if (step >= RingSettingsValues.VolumeSteps) return 1f;
            return (float)Math.Pow(10.0, (step - RingSettingsValues.VolumeSteps) * DbPerStep / 20.0);
        }

        /// <summary>Підсилення шини з урахуванням Master і фокусу вікна.</summary>
        public float Gain(RingBus bus)
        {
            if (MuteInBackground && !Focused) return 0f;
            float master = StepGain(_steps[(int)RingBus.Master]);
            return bus == RingBus.Master ? master : master * StepGain(_steps[(int)bus]);
        }
    }
}
