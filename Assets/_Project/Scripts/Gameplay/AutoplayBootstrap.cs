using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Гачок для майбутнього бот-прогону: коли фасад <c>GameSession</c>
    /// приїде з трунку, GameShell реалізує цей інтерфейс і підставляє його в
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
    }

    /// <summary>
    /// Читає прапорець командного рядка «-autoplay», знімає скріншоти під
    /// Screenshots/ каталогу білда, пише Logs/autoplay-summary.txt і завершує
    /// процес кодом виходу — все, що дим-тесту (R19) треба від процесу, ще
    /// до того, як з'явиться сам <c>GameSession</c>.
    ///
    /// ЧОГО ТУТ НЕМА НАВМИСНО. Жодної згадки Game.Core.Session: фасад
    /// пишеться паралельно в трунку й у цьому воркчасті не існує (§5 E1,
    /// TEST_BUILD.md). Компонент лише готує рейки, якими майбутній
    /// GameShell поведе бот-політики день за днем.
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
            bool hasMore = Driver != null && Driver.RunAutoplayStep(_step);
            string what = Driver != null ? Driver.DescribeLastStep() : "гачок не підключено — крок пропущено";
            _summary.Add("Крок " + _step + ": " + what);

            CaptureScreenshot(_step);
            _step++;

            if (!hasMore)
            {
                Finish(0);
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
