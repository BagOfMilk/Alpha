using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Alpha.Shared;
using Game.Core.Characters;
using Game.Core.Scenes;
using Game.Core.Loop;
using Game.Core.World;

namespace Alpha.Play
{
    /// <summary>
    /// Текстовая сборка первого часа (docs/FIRST_HOUR.md §4 п. 6).
    ///
    /// Цикл суток: утро — расстановка — день — решение — ночь — выбор ночи.
    /// На пятые сутки печатается итог: что игрок увидел, чем платил и куда
    /// съехало Напряжение.
    ///
    /// Запуск:
    ///   dotnet run --project tools/Alpha.Play -- --auto
    ///   dotnet run --project tools/Alpha.Play
    ///   dotnet run --project tools/Alpha.Play -- --auto --path bloody
    ///
    /// Режим --auto нужен не для удобства: он делает срез ВОСПРОИЗВОДИМЫМ.
    /// Прогон руками и прогон политикой идут по одному коду, поэтому жалоба
    /// «у меня было не так» проверяется одной командой.
    /// </summary>
    internal static class Program
    {
        private const int SliceDays = 5;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            bool auto = Has(args, "--auto");
            var policyPath = Value(args, "--path", "quiet") == "bloody"
                ? IncidentPath.Bloody : IncidentPath.Quiet;
            bool policyPatrol = Value(args, "--night", "patrol") == "patrol";
            int days = int.TryParse(Value(args, "--days", SliceDays.ToString()), out var parsed)
                ? parsed : SliceDays;
            string savePath = Value(args, "--save", null);
            string loadPath = Value(args, "--load", null);

            // Мир первого часа — теперь общий с харнесом код ядра (FirstHourWorld,
            // Поправка №7): именной каст на постах, производство и голод в
            // конвейере. requirePlayerDecision=true — консоль спрашивает игрока.
            var world = SettlementWorld.Build(tier: 1, requirePlayerDecision: true);
            var processor = world.Processor;
            var cycle = world.Cycle;

            // Партия в поле — часть состояния суток и слепка (Поправка №5.6 п. 4).
            var home = world.BaseState;

            if (loadPath != null && File.Exists(loadPath))
            {
                processor.RestoreState(File.ReadAllText(loadPath));
                Console.WriteLine("— загружено: " + loadPath + ", сутки " + processor.CurrentDay);
            }

            var tally = new Tally();
            int until = processor.CurrentDay + days;

            while (processor.CurrentDay < until)
            {
                Morning(processor);

                // Сутки 1, утро: «Сосед с претензией» (FIRST_HOUR §2.2).
                // Телеграфия финала — игрок узнаёт антагониста ДО боя.
                if (processor.CurrentDay == 0)
                    SceneText.Play(OpeningScenes.NeighbourWithADemand(), Cast(), Console.WriteLine);

                Placement(processor);

                RunPhase(cycle, DayPhase.Day, auto, policyPath, tally);

                // Сутки 4 — короткая вылазка (FIRST_HOUR §2.2). Посты уходящих
                // СНИМАЮТСЯ: город двое суток живёт без трёх рук, и это видно
                // пометкой, а не попапом.
                if (processor.CurrentDay == 4 && !processor.Party.IsAway
                    && processor.Party.Depart(home, SettlementWorld.PartyIds, days: 2))
                {
                    Console.WriteLine("    партия ушла: " + string.Join(", ", processor.Party.Away));
                    foreach (var post in processor.Party.VacatedPositions)
                        Console.WriteLine("      пост опустел: " + post);
                    tally.Departures++;
                }

                processor.IsPatrolling = auto ? policyPatrol : AskPatrol(policyPatrol);
                if (processor.IsPatrolling) tally.PatrolNights++;

                // Ночь тоже умеет остановиться на решении — и останавливается:
                // ночной инцидент такой же ход игрока, как дневной. Пока это
                // обрабатывалось только для дня, на первом же ночном событии
                // следующее утро падало с «сутки не закончены».
                RunPhase(cycle, DayPhase.Night, auto, policyPath, tally);

                if (processor.Party.IsAway && processor.Party.TickDay())
                {
                    var back = processor.Party.Return(home);
                    Console.WriteLine("    партия вернулась: " + string.Join(", ", back) +
                                      " — посты за ними НЕ закреплены, расставь заново");
                }
            }

            Summary(processor, tally, days);

            if (savePath != null)
            {
                File.WriteAllText(savePath, processor.SaveState());
                Console.WriteLine("— сохранено: " + savePath);
            }
            return 0;
        }

        /// <summary>Карточки всех, кто может появиться в сценах среза.</summary>
        private static List<CharacterCard> Cast()
        {
            var cast = OpeningCast.All();
            cast.Add(OpeningScenes.Protagonist());
            return cast;
        }

        // ---- фазы суток ----

        /// <summary>
        /// Одна фаза целиком: продвинуть, показать и — если конвейер встал на
        /// решении — доиграть её до конца. Конвейер не позволит начать
        /// следующую фазу, пока ход игрока не сделан.
        ///
        /// Время идёт через SettlementCycle.AdvanceDay, а не голый
        /// DayProcessor.Advance (Поправка №7.1) — только мост переносит флаг
        /// голода из BaseState в конвейер.
        /// </summary>
        private static void RunPhase(Game.Core.Base.SettlementCycle cycle, DayPhase phase, bool auto,
            IncidentPath policyPath, Tally tally)
        {
            var p = cycle.Processor;
            var report = cycle.AdvanceDay(phase);
            Print(report, tally);

            while (p.AwaitsDecision)
            {
                var path = auto ? policyPath : AskPath(report.Pending, policyPath);
                tally.Decisions++;
                if (path == IncidentPath.Bloody) tally.BloodyChoices++;
                report = p.ResolvePending(path);
                Print(report, tally);
            }
        }


        private static void Morning(DayProcessor p)
        {
            Console.WriteLine();
            Console.WriteLine("=== Сутки " + (p.CurrentDay + 1) + ". Утро. Напряжение: " + p.Tension.Band + " ===");
        }

        /// <summary>
        /// Расстановка. Пока посты держит стартовая раскладка мира: экран
        /// перестановки — шаг 2 порядка сборки, до него срез играется тем, что
        /// есть. Строка печатается, чтобы отсутствие выбора было ВИДНО, а не
        /// выглядело так, будто расстановки в игре нет вовсе.
        /// </summary>
        private static void Placement(DayProcessor p)
        {
            var domains = p.PostDomains;
            if (domains == null || domains.Count == 0) return;

            var sb = new StringBuilder("    посты: ");
            for (int i = 0; i < domains.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(domains[i].DomainTag);
            }
            sb.Append("   [перестановка — шаг 2 сборки]");
            Console.WriteLine(sb.ToString());
        }

        private static IncidentPath AskPath(PendingDecision pending, IncidentPath fallback)
        {
            Console.WriteLine();
            Console.WriteLine("  СОБЫТИЕ: " + pending.TopicId + (pending.IsCrisis ? "   [кризис]" : ""));
            for (int i = 0; i < pending.Options.Count; i++)
            {
                var o = pending.Options[i];
                string who = o.HasCandidate ? o.BestActorId : "некому";
                Console.WriteLine("   " + (i + 1) + ") " + PathName(o.Path) + ": " + o.Skill +
                                  " против " + o.Threshold + ", возьмётся " + who +
                                  ", ожидание — " + o.ExpectedBand);
            }
            Console.Write("  выбор (1/2): ");

            var line = Console.ReadLine();
            if (int.TryParse(line, out var choice) && choice >= 1 && choice <= pending.Options.Count)
                return pending.Options[choice - 1].Path;
            return fallback;
        }

        private static bool AskPatrol(bool fallback)
        {
            Console.Write("  ночь: патрулировать или спать? (п/с): ");
            var line = Console.ReadLine();
            if (string.IsNullOrEmpty(line)) return fallback;
            return line.Trim().StartsWith("п", StringComparison.OrdinalIgnoreCase);
        }

        // ---- вывод ----

        private static void Print(DayReport report, Tally tally)
        {
            foreach (var outcome in report.Incidents)
            {
                tally.Incidents++;
                if (outcome.WasCrisis) tally.Crises++;
                if (outcome.WasUnmanned) tally.Unmanned++;

                string mark = outcome.WasCrisis ? "!!" : "  ";
                string post = outcome.WasUnmanned ? ", пост пуст" : "";
                Console.WriteLine("  " + mark + " " + outcome.TopicId + " (" + outcome.DomainTag + "): " +
                                  outcome.Band + post);

                if (outcome.PopulationLost > 0)
                    Console.WriteLine("     ушло людей: " + outcome.PopulationLost);
                if (!string.IsNullOrEmpty(outcome.AffectedActorId))
                    Console.WriteLine("     задет: " + outcome.AffectedActorId);
            }

            if (report.Signals == null) return;
            foreach (var r in report.Signals.Requests)
            {
                tally.Signals++;
                Console.WriteLine("    " + SignalText.Speaker(r.Channel) + ": " + SignalText.Line(r));
            }
        }

        private static void Summary(DayProcessor p, Tally t, int days)
        {
            Console.WriteLine();
            Console.WriteLine("=== Итог на сутки " + p.CurrentDay + " ===");
            Console.WriteLine("  Напряжение: " + p.Tension.Band + " (" + p.Tension.DaysInCurrentBand + " сут. в полосе)");
            Console.WriteLine("  происшествий: " + t.Incidents + ", из них кризисов: " + t.Crises);
            Console.WriteLine("  без человека на посту: " + t.Unmanned);
            Console.WriteLine("  решений игрока: " + t.Decisions + ", из них кровавых: " + t.BloodyChoices);
            Console.WriteLine("  ночей в патруле: " + t.PatrolNights + " из " + days);
            Console.WriteLine("  реплик города: " + t.Signals);
            Console.WriteLine("  вылазок: " + t.Departures);

            // Срез считается сыгранным, только если игроку было ЧТО решать.
            if (t.Decisions == 0)
                Console.WriteLine("  ВНИМАНИЕ: за срез не случилось ни одного решения — петля не проверена.");
        }

        // ---- мелочи ----

        private sealed class Tally
        {
            public int Incidents, Crises, Unmanned, Decisions, BloodyChoices, PatrolNights, Signals, Departures;
        }

        private static string PathName(IncidentPath path)
            => path == IncidentPath.Bloody ? "кровью" : "тихо";

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
}
