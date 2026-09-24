using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core.Characters.Creation;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Bots;
using Game.Core.Session.Views;
using Game.Gameplay.Combat;
using Game.Gameplay.Text;

namespace Alpha.Play
{
    /// <summary>
    /// Текстова збірка першого часу (docs/TEST_BUILD.md, пакет D2/E3b) — тепер
    /// цілком на <see cref="GameSession"/>, як і вимагає §5 (D2, "Alpha.Play/
    /// Program.cs"): жодного прямого звернення до DayProcessor/SettlementCycle,
    /// той самий фасад, що й Unity/тести. <see cref="BotRunner"/> (Core/Session/
    /// Bots, §1.1 "боти в ядрі") веде сесію однаково для --auto (готова
    /// політика) і для ручної гри (<see cref="ConsolePolicy"/> — теж
    /// IBotPolicy, лише питає стрічку замість власного "характеру").
    ///
    /// Текст (пакет E3b): усе, що бачить гравець, іде через
    /// <see cref="UkrainianText"/> — той самий <c>&lt;Compile Include&gt;</c>
    /// прийом, яким сюди вже підключений <c>SeededDiceRoller.cs</c>
    /// (<c>Alpha.Play.csproj</c>). Колишні <c>Alpha.Shared.SceneText</c>/
    /// <c>SignalText</c> (<c>tools/Shared/</c>) видалені цим пакетом — обидва
    /// були самодостатньою тимчасовою таблицею-дублем, яку E3b мав замінити.
    /// <c>--verify-text</c> перевіряє, що жоден рядок консолі не містить
    /// видиму заглушку <see cref="UkrainianText.MissingMarker"/> (деливрабл
    /// (4) пакета: "жоден сирий ключ у виводі").
    ///
    /// Бої консоль грає ЛИШЕ «Автобоєм» (§1.1: "Консоль грає бої лише
    /// Автобоєм — гравець обирає шлях, а не ходи") — тактичні ходи лишаються
    /// Unity й тренувальному бою.
    ///
    /// Запуск:
    ///   dotnet run --project tools/Alpha.Play -- --auto
    ///   dotnet run --project tools/Alpha.Play
    ///   dotnet run --project tools/Alpha.Play -- --auto --path bloody --days 15
    ///   dotnet run --project tools/Alpha.Play -- --auto --verify-text
    /// </summary>
    internal static class Program
    {
        private const int SliceDays = 5;

        /// <summary>
        /// Рід протагоніста для лукапів <see cref="UkrainianText"/>. Консольний
        /// прогін (як і <see cref="BotRunner"/> сьогодні — grep підтверджує:
        /// жоден виклик <c>SetProtagonistGender</c> у водії ботів) роду не
        /// обирає — <c>GameSession</c> лишається на дефолтному <c>Gender.Male</c>
        /// із <c>NewGame</c>, і консоль лукапить тим самим родом, щоб не
        /// розійтися з тим, що реально бачить рушій.
        /// </summary>
        private const Gender ProtagonistGender = Gender.Male;

        /// <summary>Деливрабл (4): true, коли консоль впіймала <see cref="UkrainianText.MissingMarker"/> у власному виводі за весь прогін.</summary>
        private static bool s_sawMissingTextMarker;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool auto = Has(args, "--auto");
            bool verifyText = Has(args, "--verify-text");
            string pathArg = Value(args, "--path", "quiet");
            int days = int.TryParse(Value(args, "--days", SliceDays.ToString()), out var parsedDays) ? parsedDays : SliceDays;
            string savePath = Value(args, "--save", null);
            string loadPath = Value(args, "--load", null);
            ulong seed = ulong.TryParse(Value(args, "--seed", "1"), out var parsedSeed) ? parsedSeed : 1UL;

            var roller = new SeededDiceRoller(seed);
            var session = new GameSession(roller);

            // Фікс-ревью D2 (minor, §1.1): --auto теж мав би грати бої лише
            // «Автобоєм» — ConsolePolicy.ChooseAutoResolve уже форсує true для
            // ручного шляху, AutoResolvePolicy дає ту саму гарантію для
            // готових політик (Pacifist.ChooseAutoResolve саме по собі false).
            IBotPolicy policy = !auto
                ? new ConsolePolicy()
                : new AutoResolvePolicy(pathArg == "bloody" ? (IBotPolicy)new BloodyPolicy() : new PacifistPolicy());

            if (loadPath != null && File.Exists(loadPath))
            {
                session.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold, Seed = seed, Roller = roller });
                session.RestoreFromBlob(File.ReadAllText(loadPath));
                Write("— завантажено: " + loadPath + ", доба " + session.CurrentView.Day);
            }
            else
            {
                session.NewGame(new NewGameOptions { SkipCreation = false, HitRule = HitRuleKind.Threshold, Seed = seed, Roller = roller });
            }

            var cast = Cast();
            BotRunner.Drive(session, policy, days, null, e => PrintEvent(e, session), null, null, null, null,
                step => PrintSceneStep(step, cast));

            Write("");
            Write("=== Стан: " + session.State + ", доба " + session.CurrentView.Day + " ===");
            if (session.State == SessionState.FreePlay)
                PrintSummary(session);

            if (savePath != null)
            {
                if (session.State == SessionState.Morning || session.State == SessionState.FreePlay)
                {
                    File.WriteAllText(savePath, session.SaveState(0));
                    Write("— збережено: " + savePath);
                }
                else
                {
                    Write("— збереження недоступне поза Morning/FreePlay (R13), поточний стан: " + session.State);
                }
            }

            if (verifyText)
            {
                if (s_sawMissingTextMarker)
                {
                    Console.Error.WriteLine("--verify-text: у виводі знайдено заглушку відсутнього тексту ('[' + ключ + ']').");
                    return 1;
                }
                Console.WriteLine("--verify-text: жодної заглушки в виводі — увесь текст пройшов через UkrainianText.");
            }

            return 0;
        }

        /// <summary>
        /// Єдина точка друку рядка (деливрабл (4)): ловить видиму заглушку
        /// <see cref="UkrainianText.MissingMarker"/> — консоль НЕ падає сама
        /// (гравцю/CI корисніше побачити решту прогону), але <c>--verify-text</c>
        /// перетворює її знахідку на ненульовий код виходу.
        /// </summary>
        private static void Write(string line)
        {
            if (line != null && MissingMarkerPattern.IsMatch(line)) s_sawMissingTextMarker = true;
            Console.WriteLine(line);
        }

        private static readonly System.Text.RegularExpressions.Regex MissingMarkerPattern =
            new System.Text.RegularExpressions.Regex(@"\[[a-z0-9_.]+\]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>Карточки всіх, хто може з'явитись у сценах зрізу (для друку імені за ActorId).</summary>
        private static List<Game.Core.Characters.CharacterCard> Cast()
        {
            var cast = Game.Core.Characters.OpeningCast.All();
            cast.Add(Game.Core.Scenes.OpeningScenes.Protagonist());
            return cast;
        }

        /// <summary>Ім'я за "char.&lt;id&gt;" (R7 — Core-контентний CharacterCard.DisplayName гравець не бачить), фолбек — сирий id.</summary>
        private static string Who(IEnumerable<Game.Core.Characters.CharacterCard> cast, string id)
        {
            if (string.IsNullOrEmpty(id)) return UkrainianText.Get("ui.console.empty_actor", ProtagonistGender);
            if (id == GameSession.ProtagonistId) return UkrainianText.Get("ui.console.you", ProtagonistGender);

            bool known = false;
            foreach (var card in cast)
                if (card != null && card.Id == id) { known = true; break; }
            if (!known) return id;

            string key = "char." + id;
            return UkrainianText.Has(key, ProtagonistGender) ? UkrainianText.Get(key, ProtagonistGender) : id;
        }

        /// <summary>Слово полоси Напруги для шапки доби — окремий короткий підпис, не плутати з "tension.band.&lt;Band&gt;" (повне речення-оголошення зміни).</summary>
        private static string TensionBandLabel(string rawBand)
        {
            if (string.IsNullOrEmpty(rawBand)) return rawBand;
            string key = "tension.band.label." + rawBand.ToLowerInvariant();
            return UkrainianText.Has(key, ProtagonistGender) ? UkrainianText.Get(key, ProtagonistGender) : rawBand;
        }

        // ---- друк портретної сцени (SceneStepView, окремий гачок BotRunner.Drive) ----

        private static void PrintSceneStep(SceneStepView step, List<Game.Core.Characters.CharacterCard> cast)
        {
            if (step == null) return;

            if (!string.IsNullOrEmpty(step.LineKey))
                Write("    " + Who(cast, step.SpeakerId ?? step.ActorId) + ": " + UkrainianText.Get(step.LineKey, ProtagonistGender));
            if (!string.IsNullOrEmpty(step.EffectKey))
                Write("    ( " + UkrainianText.Get(step.EffectKey, ProtagonistGender) + " )");
            if (step.IsFinished)
                Write("  ── кінець сцени ──");
        }

        // ---- друк стрічки подій (§4.3 GameSession.DayLog) через UkrainianText ----

        private static void PrintEvent(GameEvent e, GameSession session)
        {
            if (e == null || string.IsNullOrEmpty(e.Key)) return;

            switch (e.Key)
            {
                case "day.advanced":
                {
                    string phaseKey = e.Phase == DayPhase.Night ? "village.headline.phase.night" : "village.headline.phase.day";
                    string phase = UkrainianText.Get(phaseKey, ProtagonistGender);
                    Write("");
                    Write("=== Доба " + e.Args.GetValueOrDefault("day", "?") + ", " + phase +
                        ". Стан: " + TensionBandLabel(session.CurrentView.TensionBand) + " ===");
                    return;
                }

                case "scene.finished":
                    return; // саму сцену друкує PrintSceneStep (onSceneStep) покроково

                default:
                    break;
            }

            if (e.Key == "decision.resolved" || e.Key == "finale.resolved")
            {
                string mark = e.Args.GetValueOrDefault("noCandidate", "0") == "1" ? "!!" : "  ";
                string incidentId = e.Args.GetValueOrDefault("incidentId", e.Key);
                Write("  " + mark + " " + UkrainianText.Format(e.Key, ProtagonistGender,
                    "incidentId", TranslateArg("incidentId", incidentId), "band", TranslateArg("band", e.Args.GetValueOrDefault("band", null))));
                return;
            }

            // Решта ключів (assign.*, council.*, combat.*, dungeon.*, quest.*,
            // тощо) — E3b: через UkrainianText.Format із аргументами самої
            // події (§4.3 — Args-пари, чиї ІМЕНА тепер збігаються з
            // плейсхолдерами шаблону, GameSession.LogEvent grep-звірено),
            // кожен ЗНАЧУЩИЙ id (companionId/slotId/itemId/...) — ще й через
            // TranslateArg, інакше в стрічці лишався б сирий id
            // ("myroslava стає на settlement_farms." замість "Мирослава стає
            // на Ферми поселення.") — те, що R7 забороняє показувати напряму.
            // Ключа НЕМАЄ в таблиці — діагностичний дамп (той самий формат,
            // що ловить UkrainianText.MissingMarker/--verify-text) замість
            // мовчазного пропуску: реальна діра в таблиці має бути ВИДНА.
            if (UkrainianText.Has(e.Key, ProtagonistGender))
            {
                var pairs = new List<string>(e.Args.Count * 2);
                foreach (var kv in e.Args) { pairs.Add(kv.Key); pairs.Add(TranslateArg(kv.Key, kv.Value)); }
                Write("    " + UkrainianText.Format(e.Key, ProtagonistGender, pairs.ToArray()));
                return;
            }

            var argsText = new StringBuilder();
            foreach (var kv in e.Args)
                argsText.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            Write("    " + UkrainianText.MissingMarker(e.Key) + argsText);
        }

        /// <summary>
        /// Значення GameEvent.Args — сирі id/enum-назви ядра (companionId,
        /// slotId, itemId, ...), не текст (R7). За іменем аргументу пробуємо
        /// очевидний префікс таблиці ("char."/"post."/"item."/...); немає
        /// такого ключа — повертаємо значення як є (для аргументів без
        /// перекладу, напр. лічильники, це і є правильна поведінка).
        /// </summary>
        private static string TranslateArg(string argName, string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue)) return rawValue;

            string prefix = argName switch
            {
                "companionId" => "char.",
                "slotId" => "post.",
                "itemId" => "item.",
                "scarId" => "scar.",
                "factionId" or "favored" or "cost" => "faction.",
                "buildingId" => "building.",
                "siteId" => "site.",
                "incidentId" => "incident.",
                "band" => "band.",
                "resource" => "resource.",
                _ => null,
            };
            if (prefix == null) return rawValue;

            // "incident.<id>" не самодостатній ключ — короткий заголовок стоїть
            // під ".title" (§7.6/AddIncidentHeadlinesAndOutcomes); band-слова
            // ("Worst"/"Good"/...) і назви ресурсів ("Food"/"Gold") — enum-регістр
            // ядра, ключ таблиці нижнього регістру.
            string key = prefix == "incident."
                ? prefix + rawValue + ".title"
                : prefix + (prefix == "band." || prefix == "resource." ? rawValue.ToLowerInvariant() : rawValue);

            return UkrainianText.Has(key, ProtagonistGender) ? UkrainianText.Get(key, ProtagonistGender) : rawValue;
        }

        private static void PrintSummary(GameSession session)
        {
            var summary = session.GetSummaryView();
            Write("");
            Write("=== Підсумок ===");
            Write("  фінал: " + TranslateArg("band", summary.FinaleOutcomeKey ?? "—"));
            Write("  гаманець: золото " + summary.Wallet.Gold + ", матеріали " + summary.Wallet.Materials + ", їжа " + summary.Wallet.Food);
            Write("  збудовано: " + string.Join(", ", TranslateEach("buildingId", summary.BuiltBuildings)));
            foreach (var c in summary.FinalRoster)
                Write("  " + WhoById(c.Id) + " (" + c.Id + "): " + StatusWord(c.Status) +
                    (c.Loyalty.HasValue ? ", лояльність " + TranslateEnum("loyalty.band", c.Loyalty.Value.ToString()) : "") +
                    ", рівень " + c.Level);
            foreach (var f in summary.Factions)
                Write("  фракція " + WhoFactionById(f.Id) + ": " + TranslateEnum("faction.band", f.Band));
        }

        private static IEnumerable<string> TranslateEach(string argName, IEnumerable<string> rawValues)
        {
            foreach (var v in rawValues) yield return TranslateArg(argName, v);
        }

        /// <summary>Для enum'ів, чий ключ таблиці НЕ збігається з іменем аргументу Args ("faction.band.neutral", "loyalty.band.steady") — прямий префікс замість <see cref="TranslateArg"/>.</summary>
        private static string TranslateEnum(string keyPrefix, string rawValue)
        {
            if (string.IsNullOrEmpty(rawValue)) return rawValue;
            string key = keyPrefix + "." + rawValue.ToLowerInvariant();
            return UkrainianText.Has(key, ProtagonistGender) ? UkrainianText.Get(key, ProtagonistGender) : rawValue;
        }

        /// <summary>CompanionStatus — enum PascalCase (OnMission), ключ таблиці — snake_case ("status.on_mission"): явний перелік, не автоконверсія регістру.</summary>
        private static string StatusWord(Game.Core.Characters.CompanionStatus status)
        {
            string key = status switch
            {
                Game.Core.Characters.CompanionStatus.Idle => "status.idle",
                Game.Core.Characters.CompanionStatus.Assigned => "status.assigned",
                Game.Core.Characters.CompanionStatus.OnMission => "status.on_mission",
                Game.Core.Characters.CompanionStatus.Injured => "status.injured",
                Game.Core.Characters.CompanionStatus.Resting => "status.resting",
                Game.Core.Characters.CompanionStatus.Dead => "status.dead",
                Game.Core.Characters.CompanionStatus.Antagonist => "status.antagonist",
                _ => null,
            };
            return key != null && UkrainianText.Has(key, ProtagonistGender)
                ? UkrainianText.Get(key, ProtagonistGender) : status.ToString();
        }

        /// <summary>"char.&lt;id&gt;" напряму, без карти каста (підсумок після AcknowledgeSummary не тримає List&lt;CharacterCard&gt; під рукою) — той самий фолбек-принцип, що <see cref="Who"/>.</summary>
        private static string WhoById(string id)
        {
            string key = "char." + id;
            return UkrainianText.Has(key, ProtagonistGender) ? UkrainianText.Get(key, ProtagonistGender) : id;
        }

        private static string WhoFactionById(string id)
        {
            string key = "faction." + id;
            return UkrainianText.Has(key, ProtagonistGender) ? UkrainianText.Get(key, ProtagonistGender) : id;
        }

        private static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == flag) return true;
            return false;
        }

        private static string Value(string[] args, string key, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == key) return args[i + 1];
            return fallback;
        }
    }

    internal static class DictionaryExtensions
    {
        public static string GetValueOrDefault(this IReadOnlyDictionary<string, string> dict, string key, string fallback)
            => dict != null && dict.TryGetValue(key, out var v) ? v : fallback;
    }
}
