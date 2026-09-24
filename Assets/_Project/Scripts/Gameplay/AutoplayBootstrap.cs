using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Фаза F (docs/TEST_BUILD.md, "PHASE F LOOP"): дрібні послуги, які
    /// <see cref="AutoplayGameDriver"/> просить у хоста — знімок екрана з
    /// осмисленим ім'ям і рядок у підсумковий лог. Driver — чиста ігрова
    /// логіка (крокує <c>GameSession</c>/реальний <c>GameShell</c>), Bootstrap —
    /// рушійна обв'язка (коли саме кликати наступний крок, куди писати файли,
    /// яким кодом виходу завершити процес). Розділ той самий, що вже тримає
    /// решту файлу *Screen.cs/Widgets.cs: малювання/дані нарізно.
    /// </summary>
    public interface IAutoplayHost
    {
        /// <summary>Один рядок у Logs/autoplay-summary.txt.</summary>
        void Log(string line);

        /// <summary>
        /// Знімок екрана під Screenshots/, ім'я файлу — "NN-slug.png"
        /// (наростаючий лічильник + короткий опис екрана, напр. "hub-posts").
        /// </summary>
        void Capture(string slug);
    }

    /// <summary>
    /// Точка входу дим-тесту (R19/Фаза F): читає прапорці командного рядка
    /// "-autoplay"/"-autoplay-threshold", жене <see cref="AutoplayGameDriver"/>
    /// крізь РЕАЛЬНИЙ <see cref="GameShell"/> цієї ж сцени (не ізольовану копію
    /// сесії — попередня версія цього файлу саме так і губила візуальний тур:
    /// GameShell.OnGUI малював незмінний Title, бо жоден екран так і не бачив,
    /// що бот-прогін узагалі йде), пише Logs/autoplay-summary.txt і завершує
    /// процес кодом виходу.
    ///
    /// Крокування — БЕЗ UnityEngine-корутин (StartCoroutine/WaitForSeconds):
    /// <see cref="AutoplayGameDriver.Run"/> — звичайний C#-ітератор
    /// (<c>IEnumerator&lt;int&gt;</c>, "yield return 0" = почекати один
    /// намальований кадр), який ЦЕЙ файл прокручує по одному кроку за
    /// <see cref="Update"/>. Так весь тур лишається лінтованим
    /// (tools/Game.Gameplay.Lint) звичайним C#, а не рушійним API, якого
    /// заглушка не знає.
    ///
    /// Коди виходу (§1 "PHASE F LOOP" завдання): 0 — тур дійшов до кінця без
    /// винятків і без пропущених ключів тексту; 2 — будь-який виняток; 3 —
    /// тур дійшов до кінця, але <see cref="UkrainianText"/> хоч раз повернула
    /// видиму заглушку "[ключ]" (лічильник — <see cref="UkrainianText.MissingKeyCounts"/>).
    /// </summary>
    public sealed class AutoplayBootstrap : MonoBehaviour, IAutoplayHost
    {
        public const string CommandLineFlag = "-autoplay";
        public const string ThresholdFlag = "-autoplay-threshold";

        /// <summary>
        /// Виставляється <c>GameSceneBuilder.Build()</c> одразу після
        /// <c>AddComponent</c> — той самий GameObject "Boot", що й
        /// <see cref="GameShell"/> (Editor-only <c>GetComponent</c> там, не
        /// тут: цей файл лінтується заглушкою, у якій його немає).
        /// </summary>
        public GameShell Shell;

        private IEnumerator<int> _tour;
        private bool _hadException;
        private int _shotIndex = 1;
        private readonly List<string> _summary = new List<string>();

        /// <summary>Чи просив командний рядок автопрогон — перевіряється один раз при старті.</summary>
        public static bool RequestedFromCommandLine() => HasArg(CommandLineFlag);

        private static bool HasArg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == flag) return true;
            return false;
        }

        private void Start()
        {
            if (!RequestedFromCommandLine()) return;

            if (Shell == null)
            {
                Log("АВТОПРОГОН: GameShell не підключено (GameSceneBuilder мав виставити AutoplayBootstrap.Shell) — прогін неможливий.");
                Finish(2);
                return;
            }

            UkrainianText.ResetMissingKeyTracking();
            bool threshold = HasArg(ThresholdFlag);
            Log("Автопрогон почато: " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture) +
                " (правило влучання: " + (threshold ? "поріг" : "відсоток") + ")");

            var driver = new AutoplayGameDriver(this, Shell, threshold);
            _tour = driver.Run();
        }

        private void Update()
        {
            if (_tour == null) return;

            bool more;
            try
            {
                more = _tour.MoveNext();
            }
            catch (Exception ex)
            {
                _hadException = true;
                Log("ВИНЯТОК: " + ex.GetType().Name + " — " + ex.Message);
                Log(ex.StackTrace ?? "(без стектрейсу)");
                _tour = null;
                Capture("exception");
                Finish(2);
                return;
            }

            if (!more)
            {
                _tour = null;
                int missing = UkrainianText.MissingKeyCounts.Count;
                if (missing > 0)
                {
                    Log("Відсутні ключі тексту за прогін (" + missing + "):");
                    foreach (var kv in UkrainianText.MissingKeyCounts)
                        Log("  " + UkrainianText.MissingMarker(kv.Key) + " x" + kv.Value.ToString(CultureInfo.InvariantCulture));
                }
                Finish(_hadException ? 2 : (missing > 0 ? 3 : 0));
            }
        }

        public void Log(string line) => _summary.Add(line ?? string.Empty);

        public void Capture(string slug)
        {
            string dir = Path.Combine(BuildRoot(), "Screenshots");
            Directory.CreateDirectory(dir);
            string file = string.Format(CultureInfo.InvariantCulture, "{0:00}-{1}.png", _shotIndex++, slug);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, file));
            Log("Скріншот " + file + " (стан: " + (Shell != null ? Shell.Session?.State.ToString() ?? "?" : "?") + ")");
        }

        private void Finish(int exitCode)
        {
            Log("Автопрогон завершено, код виходу " + exitCode.ToString(CultureInfo.InvariantCulture) + ".");
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
