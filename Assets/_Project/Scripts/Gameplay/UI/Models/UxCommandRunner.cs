using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Game.Core.Session;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Наслідок дії для екрана (UX-11): успіх, відмова людською мовою біля
    /// кнопки, або збій, текст якого на екран не йде — лише в лог.
    /// </summary>
    public sealed class UxOutcome
    {
        public readonly bool Ok;
        /// <summary>Відмова людською мовою (показати під кнопкою) або null.</summary>
        public readonly string Refusal;
        /// <summary>Сирий текст винятку — лише для логу, ніколи на екран.</summary>
        public readonly string RawError;
        /// <summary>Дія незворотна й чекає підтвердження (UX-12): виконається лише після <c>IUxInput.Confirm</c>.</summary>
        public readonly bool AwaitingConfirm;

        private UxOutcome(bool ok, string refusal, string rawError, bool awaitingConfirm = false)
        {
            Ok = ok;
            Refusal = refusal;
            RawError = rawError;
            AwaitingConfirm = awaitingConfirm;
        }

        public static UxOutcome Success() => new UxOutcome(true, null, null);
        public static UxOutcome Pending() => new UxOutcome(false, null, null, true);
        public static UxOutcome Refused(string refusal) => new UxOutcome(false, refusal ?? string.Empty, null);
        public static UxOutcome Failed(string humanText, string rawError) => new UxOutcome(false, humanText ?? string.Empty, rawError);
    }

    /// <summary>
    /// Виконання команд ядра для екранів і автотуру однаково (UI-14): ловить
    /// <see cref="InvalidOperationException"/> (так ядро відмовляє за станом
    /// гри) і перетворює на людський текст (<see cref="UxErrorText"/>), а код
    /// відмови (Assign/Order*/Depart/Craft) — на текст через переданий
    /// перекладач. Без рушія: тестується headless.
    /// </summary>
    public static class UxCommandRunner
    {
        public static UxOutcome Run(Action command, bool female)
        {
            if (command == null) return UxOutcome.Refused(null);
            try
            {
                command();
                return UxOutcome.Success();
            }
            catch (InvalidOperationException ex)
            {
                return UxOutcome.Failed(UxErrorText.Humanize(ex.Message, female), ex.Message);
            }
        }

        /// <summary>Команда з кодом результату: <paramref name="failureText"/> повертає текст відмови або null для успіху.</summary>
        public static UxOutcome Run<T>(Func<T> command, Func<T, string> failureText, bool female, out T result)
        {
            result = default(T);
            if (command == null) return UxOutcome.Refused(null);
            try
            {
                result = command();
                string failure = failureText != null ? failureText(result) : null;
                return string.IsNullOrEmpty(failure) ? UxOutcome.Success() : UxOutcome.Refused(failure);
            }
            catch (InvalidOperationException ex)
            {
                return UxOutcome.Failed(UxErrorText.Humanize(ex.Message, female), ex.Message);
            }
        }

        /// <summary>Дія з картки: спершу стан гри і власна причина дії, лише потім ядро (UI-04: мовчазних відмов немає).</summary>
        public static UxOutcome Invoke(UxAction action, SessionState state, bool female)
        {
            if (action == null) return UxOutcome.Refused(null);
            string reason = action.ReasonIn(state, female);
            if (reason != null) return UxOutcome.Refused(reason);
            if (action.Execute == null) return UxOutcome.Refused(UkrainianText.Get("ux.error.generic", female));
            try
            {
                return action.Execute() ?? UxOutcome.Success();
            }
            catch (InvalidOperationException ex)
            {
                return UxOutcome.Failed(UxErrorText.Humanize(ex.Message, female), ex.Message);
            }
        }
    }

    /// <summary>
    /// Людський текст замість сирого винятку (UX-11, docs/UX_DESIGN.md §5.15).
    ///
    /// Порядок, детермінований і перевірений тестами на справжніх винятках
    /// <c>GameSession</c> (<c>UxFoundationTests.ErrorText_*</c>; якщо ядро
    /// змінить формат, тест це спіймає):
    /// 1. відомі технічні відмови — власним ключем (фінал доби 5; збереження
    ///    іншої версії гри — назавжди, а не «зараз»);
    /// 2. відмова за станом гри («Команда недоступна у стані X (потрібен [один
    ///    з:] A, B)» або «лише в Morning/FreePlay») — «Це можна зробити лише
    ///    вранці або у вільній грі.»;
    /// 3. інакше — речення ядра без кодів правил «(R13)»; якщо в ньому лишилась
    ///    латиниця (імена методів, типів) — загальне «Зараз цього зробити не
    ///    можна.». Сирий текст — лише в лог.
    /// </summary>
    public static class UxErrorText
    {
        private static readonly Regex StateGate = new Regex(
            @"недоступна у стані\s+(\w+)\s*\(потрібен\s+(?:один з:\s*)?([^)]*)\)",
            RegexOptions.CultureInvariant);

        private static readonly Regex RuleCode = new Regex(@"\s*\(R\d+[^)]*\)", RegexOptions.CultureInvariant);

        private static readonly Regex Word = new Regex(@"[A-Za-z]+", RegexOptions.CultureInvariant);

        private static readonly Regex Latin = new Regex("[A-Za-z]", RegexOptions.CultureInvariant);

        public static string Humanize(string raw, bool female)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Generic(female);

            if (raw.Contains("ResolveFinale")) return UkrainianText.Get("ux.error.finale_first", female);
            if (raw.Contains("Збереження іншої версії")) return UkrainianText.Get("ux.error.save_incompatible", female);

            var gate = StateGate.Match(raw);
            if (gate.Success) return OnlyInOrGeneric(StatesIn(gate.Groups[2].Value), female);

            var named = StatesIn(raw);
            if (named.Count > 0) return OnlyIn(named, female);

            string cleaned = RuleCode.Replace(raw, string.Empty).Trim();
            if (cleaned.Length == 0 || Latin.IsMatch(cleaned)) return Generic(female);
            return cleaned;
        }

        /// <summary>«Це можна зробити лише вранці або у вільній грі.» — для переліку дозволених станів.</summary>
        public static string OnlyIn(IList<SessionState> allowed, bool female)
        {
            if (allowed == null || allowed.Count == 0) return Generic(female);
            var words = new StringBuilder();
            for (int i = 0; i < allowed.Count; i++)
            {
                if (i > 0) words.Append(i == allowed.Count - 1 ? " " + UkrainianText.Get("ux.word.or", female) + " " : ", ");
                words.Append(UkrainianText.Get(StateWordKey(allowed[i]), female));
            }
            return UkrainianText.Format("ux.error.only_in", female, "when", words.ToString());
        }

        /// <summary>Ключ обставини часу для стану гри: «вранці», «увечері», «у вільній грі»…</summary>
        public static string StateWordKey(SessionState state) => "ux.state." + state.ToString().ToLowerInvariant();

        private static string Generic(bool female) => UkrainianText.Get("ux.error.generic", female);

        private static string OnlyInOrGeneric(List<SessionState> states, bool female) =>
            states.Count > 0 ? OnlyIn(states, female) : Generic(female);

        /// <summary>Назви станів <see cref="SessionState"/>, що трапились у тексті як окремі слова, у порядку появи, без повторів.</summary>
        private static List<SessionState> StatesIn(string text)
        {
            var result = new List<SessionState>();
            foreach (Match m in Word.Matches(text))
            {
                SessionState s;
                if (Enum.TryParse(m.Value, false, out s) && Enum.IsDefined(typeof(SessionState), s) &&
                    string.Equals(s.ToString(), m.Value, StringComparison.Ordinal) && !result.Contains(s))
                    result.Add(s);
            }
            return result;
        }
    }
}
