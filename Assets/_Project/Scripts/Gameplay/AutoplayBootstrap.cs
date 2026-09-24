using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Гачок бот-прогону: <see cref="AutoplayGameDriver"/> (пакет E1b)
    /// реалізує цей інтерфейс поверх <c>GameSession</c>/<c>BotRunner</c>, а
    /// <c>GameShell.Awake</c> підставляє його в
    /// <see cref="AutoplayBootstrap.Driver"/>. Сам гачок навмисно НЕ знає, що
    /// таке «доба» чи «бот-політика» — лише «зроби один крок і скажи, чи є
    /// ще куди йти».
    /// </summary>
    public interface IAutoplayDriver
    {
        /// <summary>Один крок автопрогону. false — прогону більше нема куди йти (кампанія скінчилась/провалилась).</summary>
        bool RunAutoplayStep(int stepIndex);

        /// <summary>Короткий людський підсумок щойно зробленого кроку — рядок у autoplay-summary.txt.</summary>
        string DescribeLastStep();

        /// <summary>
        /// Пакет E1b: true, коли прогін зупинився ЧЕРЕЗ провал (виняток або
        /// кампанія застрягла), а не тому, що чесно дійшов до Summary/FreePlay.
        /// <see cref="AutoplayBootstrap"/> читає це лише ПІСЛЯ
        /// <see cref="RunAutoplayStep"/> повернув false — код виходу мусить
        /// бути 0 лише коли прогін дійшов до кінця без винятків (R19/§5 E1b).
        /// </summary>
        bool Failed { get; }
    }

    /// <summary>
    /// Читає прапорець командного рядка «-autoplay», знімає скріншоти під
    /// Screenshots/ каталогу білда, пише Logs/autoplay-summary.txt і завершує
    /// процес кодом виходу — усе, що дим-тесту (R19) треба від процесу.
    ///
    /// ЧОГО ТУТ НЕМА НАВМИСНО. Жодної згадки Game.Core.Session напряму —
    /// компонент лише крутить <see cref="IAutoplayDriver"/> та обробляє
    /// виняток/код виходу; сам прогін ГРИ (GameSession/BotRunner) — робота
    /// <see cref="AutoplayGameDriver"/> (Gameplay/AutoplayGameDriver.cs).
    /// </summary>
    public sealed class AutoplayBootstrap : MonoBehaviour
    {
        public const string CommandLineFlag = "-autoplay";

        [Tooltip("Пауза між кроками — дає рендеру встигнути замалювати кадр перед скріншотом.")]
        [Min(0f)] public float secondsPerStep = 0.5f;

        [Tooltip("Запобіжник: без підключеного гачка чи зі зламаною кампанією прогон не крутиться вічно.")]
        [Min(1)] public int maxSteps = 40;

        /// <summary>Підставляється майбутнім GameShell, коли фасад GameSession приїде з трунку.</summary>
        public static IAutoplayDriver Driver;

        private bool _active;
        private int _step;
        private float _timer;
        private readonly List<string> _summary = new List<string>();

        /// <summary>Чи просив командний рядок автопрогон — перевіряється один раз при старті.</summary>
        public static bool RequestedFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == CommandLineFlag) return true;
            return false;
        }

        private void Start()
        {
            if (!RequestedFromCommandLine()) return;

            _active = true;
            _summary.Add("Автопрогон запущено: " + DateTime.UtcNow.ToString("u"));
        }

        private void Update()
        {
            if (!_active) return;

            _timer += Time.deltaTime;
            if (_timer < secondsPerStep) return;

            _timer = 0f;
            Step();
        }

        private void Step()
        {
            bool hasMore;
            string what;

            try
            {
                hasMore = Driver != null && Driver.RunAutoplayStep(_step);
                what = Driver != null ? Driver.DescribeLastStep() : "гачок не підключено — крок пропущено";
            }
            catch (Exception ex)
            {
                // Виняток у водії не має піти вгору й зупинити Update() без
                // підсумку — код виходу все одно мусить бути ненульовим
                // (R19: "exit code 0 лише якщо прогін дійшов до Summary/
                // FreePlay без винятків").
                what = "Виняток на кроці " + _step + ": " + ex.GetType().Name + " — " + ex.Message;
                _summary.Add("Крок " + _step + ": " + what);
                CaptureScreenshot(_step);
                Finish(1);
                return;
            }

            _summary.Add("Крок " + _step + ": " + what);
            CaptureScreenshot(_step);
            _step++;

            if (!hasMore)
            {
                bool failed = Driver != null && Driver.Failed;
                Finish(failed ? 1 : 0);
                return;
            }

            if (_step >= maxSteps)
            {
                _summary.Add("Зупинено запобіжником maxSteps=" + maxSteps + " — гачок ще сигналив «є ще»");
                Finish(1);
            }
        }

        private void CaptureScreenshot(int step)
        {
            string dir = Path.Combine(BuildRoot(), "Screenshots");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, string.Format("autoplay-{0:00}.png", step)));
        }

        private void Finish(int exitCode)
        {
            _active = false;
            WriteSummary();
            Application.Quit(exitCode);
        }

        private void WriteSummary()
        {
            string dir = Path.Combine(BuildRoot(), "Logs");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "autoplay-summary.txt"), _summary.ToArray());
        }

        /// <summary>
        /// Каталог білда: у зібраному .exe <see cref="Application.dataPath"/> —
        /// це «…\Alpha_Data», а Screenshots/ і Logs/ лежать одним рівнем
        /// вище, поруч із самим .exe (той самий каталог, який готує
        /// build-unity.ps1).
        /// </summary>
        private static string BuildRoot()
        {
            var parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.dataPath;
        }
    }
}
