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
    /// видиму заглушку "[ключ]" (лічильник — <see cref="UkrainianText.MissingKeyCounts"/>);
    /// 4 — лише <see cref="JournalFlag"/>: тур дійшов до кінця, але
    /// <c>GameSession.GetMechanicsJournal()</c> лишила хоч один із 44 записів
    /// непобаченим (<see cref="AutoplayGameDriver.JournalSeenCount"/> &lt;
    /// <see cref="AutoplayGameDriver.JournalTotalCount"/>).
    /// </summary>
    public sealed class AutoplayBootstrap : MonoBehaviour, IAutoplayHost
    {
        public const string CommandLineFlag = "-autoplay";
        public const string ThresholdFlag = "-autoplay-threshold";

        /// <summary>
        /// Поправка №7 (стиснутий темп): після звичайного туру (титул →
        /// створення → сценарні доби → підсумок доби 5 → FreePlay) НЕ
        /// зупиняється на першому дні вільної гри, а веде далі, руки геть
        /// (без облав/Храму/Укріплень — той самий "безрукий еталон", що й
        /// <c>PacifistPolicy</c>+<c>suppressCouncilRoutine</c> у боті), доки
        /// не побачить розв'язку великого бунту на площі або не впреться в
        /// стелю доби (<see cref="AutoplayGameDriver.LongTourDayCap"/>).
        /// Мовчки означає й <see cref="CommandLineFlag"/> — окремо вказувати
        /// його не треба.
        /// </summary>
        public const string LongTourFlag = "-autoplay-long";

        /// <summary>
        /// Бісекція краш-репорту про виліт після Application.Quit (root-cause
        /// evidence: 20.09.2026 репорт, стійка адреса всіх крашів у
        /// UnityPlayer.dll — детермінований порядок знищення, а не випадкове
        /// пошкодження купи): вихід без ЖОДНОГО ігрового кроку — лише титул
        /// на кілька кадрів і вихід. Ізолює природний вихід (гравець натиснув
        /// "Вихід" на титулі, не бачивши ні порталів, ні арени) від туру, що
        /// проходить крізь <see cref="PortraitRig"/>/<see cref="BattleArenaController"/>.
        /// </summary>
        public const string QuitAfterTitleFlag = "-quit-after-title";

        /// <summary>
        /// Ціль 1 (пряме доручення власника 25.09.2026): один прогін крізь
        /// РЕАЛЬНІ екрани, що доводить всі 44 записи <c>GetMechanicsJournal()</c>
        /// до Seen (сценарій, уже перевірений у Core —
        /// MechanicsJournalCompletionTests.JournalPlayer_OnePlaythrough_
        /// SeesAllFortyFourEntries). Мовчки означає й <see cref="CommandLineFlag"/>
        /// — той самий принцип, що вже й у <see cref="LongTourFlag"/> вище.
        /// Ігнорує <see cref="ThresholdFlag"/>/<see cref="LongTourFlag"/> (тур
        /// сам форсує детермінований поріг — інваріант 8) — див.
        /// <see cref="AutoplayGameDriver.RunJournal"/>.
        /// </summary>
        public const string JournalFlag = "-autoplay-journal";

        /// <summary>
        /// Виставляється <c>GameSceneBuilder.Build()</c> одразу після
        /// <c>AddComponent</c> — той самий GameObject "Boot", що й
        /// <see cref="GameShell"/> (Editor-only <c>GetComponent</c> там, не
        /// тут: цей файл лінтується заглушкою, у якій його немає).
        /// </summary>
        public GameShell Shell;

        private IEnumerator<int> _tour;
        private AutoplayGameDriver _driver;
        private bool _journalMode;
        private bool _hadException;
        private int _shotIndex = 1;
        private readonly List<string> _summary = new List<string>();

        /// <summary>Скільки кадрів лишилось до виходу в режимі <see cref="QuitAfterTitleFlag"/> (-1 = режим не активний).</summary>
        private int _quitAfterTitleFramesLeft = -1;

        /// <summary>
        /// Чи просив командний рядок автопрогон — перевіряється один раз при
        /// старті. <see cref="JournalFlag"/> тут само — той самий принцип, що
        /// вже й у <see cref="LongTourFlag"/>: сам вмикає <c>runInBackground</c>
        /// нижче (Start), окремо вказувати <see cref="CommandLineFlag"/> не треба.
        /// </summary>
        public static bool RequestedFromCommandLine() => HasArg(CommandLineFlag) || HasArg(LongTourFlag) || HasArg(JournalFlag);

        private static bool HasArg(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == flag) return true;
            return false;
        }

        private void Start()
        {
            // Автопрогон і плейн-вихід ідуть без людини за вікном: якщо вікно
            // стартувало без фокуса, плеєр з runInBackground=0 (ProjectSettings)
            // не крутить кадри, і прогін висить до тайм-ауту (замір 24.09.2026:
            // один з чотирьох запусків поспіль завис ще до титулу). Гравцю
            // це не заважає — прапорець змінюється тільки в цих режимах.
            if (HasArg(QuitAfterTitleFlag) || RequestedFromCommandLine())
                Application.runInBackground = true;

            if (HasArg(QuitAfterTitleFlag))
            {
                // Два кадри — титул точно встиг намалюватися бодай раз
                // (Repaint), перш ніж Update() (безпечна точка, не OnGUI)
                // покличе Finish/Application.Quit.
                _quitAfterTitleFramesLeft = 2;
                Log("Плейн-вихід (-quit-after-title): без туру, лише титул і вихід.");
                return;
            }

            if (!RequestedFromCommandLine()) return;

            if (Shell == null)
            {
                Log("АВТОПРОГОН: GameShell не підключено (GameSceneBuilder мав виставити AutoplayBootstrap.Shell) — прогін неможливий.");
                Finish(2);
                return;
            }

            UkrainianText.ResetMissingKeyTracking();
            bool threshold = HasArg(ThresholdFlag);
            bool longTour = HasArg(LongTourFlag);
            _journalMode = HasArg(JournalFlag);
            Log("Автопрогон почато: " + DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture) +
                (_journalMode
                    ? " (журнальний тур: усі 44 записи журналу механік, правило влучання — поріг)"
                    : " (правило влучання: " + (threshold ? "поріг" : "відсоток") +
                      (longTour ? ", довгий тур до великого бунту" : "") + ")"));

            _driver = new AutoplayGameDriver(this, Shell, threshold, longTour);
            _tour = _journalMode ? _driver.RunJournal() : _driver.Run();
        }

        private void Update()
        {
            if (_quitAfterTitleFramesLeft > 0)
            {
                _quitAfterTitleFramesLeft--;
                if (_quitAfterTitleFramesLeft == 0) Finish(0);
                return;
            }

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

                // Ціль 1, код виходу 4: журнальний тур дійшов до кінця (без
                // винятку), але GetMechanicsJournal() лишила хоч один запис
                // непобаченим — окремий код від "3 = пропущений ключ тексту",
                // щоб СI відрізняв "текст не переклали" від "механіку не
                // показали". Пріоритет: виняток (2) > непобачена механіка (4)
                // > пропущений ключ (3) > 0.
                bool journalIncomplete = _journalMode && _driver != null &&
                    _driver.JournalSeenCount < _driver.JournalTotalCount;
                if (_journalMode && _driver != null)
                    Log("Журнал: " + _driver.JournalSeenCount + "/" + _driver.JournalTotalCount +
                        (journalIncomplete ? " — не всі механіки побачено." : " — усі механіки побачено."));

                int exitCode = _hadException ? 2 : (journalIncomplete ? 4 : (missing > 0 ? 3 : 0));
                Finish(exitCode);
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

            // Root-cause фікс краху при виході (доказ — Windows Event Log,
            // Application/Id=1000, 24.09.2026): Application.Quit незалежно
            // від графічного API (перевірено і на форсованому D3D11, і на
            // штатному D3D12 — та сама адреса краху в UnityPlayer.dll) і
            // незалежно від того, що саме встигло намалюватися (той самий
            // крах на голому титулі, -quit-after-title, без жодного кадру
            // бою чи портрета) впав на ~22/22 запусках поспіль — нативний
            // teardown рушія сам по собі баговий, не код гри. Environment.Exit
            // не дає рушію взагалі дійти до цього шляху (0/6 крашів у тому
            // самому прогоні): див. той самий фікс у GameShell.HandleWantsToQuit,
            // яка ловить і решту тригерів виходу (кнопка, Alt+F4, закриття
            // вікна) тим самим способом.
            HardExit.Now(exitCode); // Environment.Exit зависав на виході — див. HardExit
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
