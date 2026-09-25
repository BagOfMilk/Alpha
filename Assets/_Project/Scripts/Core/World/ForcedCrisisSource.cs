using System.Globalization;

namespace Game.Core.World
{
    /// <summary>Де в лінії кризи ми зараз.</summary>
    public enum CrisisPhase
    {
        Idle = 0,
        /// <summary>Попередження прозвучало (<c>crisis.test.warn</c>).</summary>
        Warned = 1,
        /// <summary>Вікно реакції відкрите (<c>crisis.test.window</c>).</summary>
        WindowOpen = 2,
        /// <summary>Криза вже вдарила — м'яко чи на повну.</summary>
        Resolved = 3
    }

    /// <summary>
    /// СКРИПТОВАНА ТЕСТ-КРИЗА доби 5 «Вогонь на в'їзді» (R10, §7.14
    /// TEST_BUILD.md) — АВТОРСЬКИЙ ПРЕВ'Ю тригер тестової сборки, не механіка
    /// постійної гри: природний Пульс (WorldPulse/PressureTrack) не встигає
    /// дійти до кризи за 5 діб — межа задокументована в §9.2 TEST_BUILD.md, не
    /// помилка.
    ///
    /// НАВМИСНО не реалізує <see cref="IPressureSource"/>. Той контракт
    /// призначений для накопичувача, що росте ПОСТУПОВО і видає рівно одну
    /// ступінь предвестника за тік (WorldPulse.Advance): природна криза
    /// доходить до "биття" лише після трьох ПОЧУТИХ ступенів і вікна
    /// милосердя (PressureTrack.IsReady). Скриптована криза стискає це до
    /// однієї доби (попередження вранці — вікно до ночі — биття вночі,
    /// §3.5), тому переливається в СВІЙ, простіший стейт-машин: Idle → Warned
    /// → WindowOpen → Resolved. Announces=true (лишень позначкою нижче, не
    /// властивістю інтерфейсу) — попередження ОБОВ'ЯЗКОВЕ: криза не має права
    /// вдарити без нього (US-11.2).
    ///
    /// Хук мітигації — <see cref="Mitigate"/>: D1 викликає його, коли гравець
    /// реагує (майбутній <c>GameSession.ReactToCrisis</c>) поки вікно відкрите.
    /// Сам клас жодного наслідку (рана/відтік) НЕ застосовує — це читання
    /// <see cref="HasBitten"/>/<see cref="WasMitigated"/> і виклик ІСНУЮЧИХ
    /// шляхів (<c>ICasualtySink.Wound</c>/<c>PopulationState.Remove</c>, як у
    /// <c>IncidentResolver.ResolveCrisis</c>) — робота D1, у якого ці порти є.
    /// </summary>
    public sealed class ForcedCrisisSource : Loop.IStateBlob
    {
        public const string SourceId = "test.fire_at_the_gate";
        public const string DomainTagValue = "в'їзд";

        /// <summary>Announces (US-11.2): скриптована криза зобов'язана попередити — завжди true.</summary>
        public const bool Announces = true;

        public int WarnDay { get; }
        public int WindowDays { get; }

        public CrisisPhase Phase { get; private set; } = CrisisPhase.Idle;
        public bool WasMitigated { get; private set; }
        public bool HasBitten { get; private set; }

        public ForcedCrisisSource(int warnDay = 5, int windowDays = 1)
        {
            WarnDay = warnDay;
            WindowDays = windowDays < 1 ? 1 : windowDays;
        }

        /// <summary>Попередження прозвучало (вранці доби <see cref="WarnDay"/>).</summary>
        public bool Warn(int day)
        {
            if (Phase != CrisisPhase.Idle || day != WarnDay) return false;
            Phase = CrisisPhase.Warned;
            return true;
        }

        /// <summary>Вікно реакції відкрите: «є час діяти — до ночі» (§7.14).</summary>
        public bool OpenReactionWindow()
        {
            if (Phase != CrisisPhase.Warned) return false;
            Phase = CrisisPhase.WindowOpen;
            return true;
        }

        /// <summary>
        /// Хук мітигації: гравець відреагував (D1.ReactToCrisis), поки вікно
        /// відкрите. Можна викликати лише РАЗ і лише до <see cref="Bite"/> —
        /// вікно чесно закривається биттям, а не мітигацією заднім числом.
        /// </summary>
        public bool Mitigate()
        {
            if (Phase != CrisisPhase.WindowOpen || HasBitten) return false;
            WasMitigated = true;
            return true;
        }

        /// <summary>
        /// Кінець вікна: криза вдаряє — <c>crisis.test.mitigated</c> (якщо
        /// встигли відреагувати) або <c>crisis.test.unmitigated</c>.
        /// </summary>
        public bool Bite()
        {
            if (Phase != CrisisPhase.WindowOpen) return false;
            HasBitten = true;
            Phase = CrisisPhase.Resolved;
            return true;
        }

        // ---- зліпок (D1a: GameSession.ComposeSave/ApplySave несе фрагмент "crisis=") ----

        public string CaptureState()
        {
            return ((int)Phase).ToString(CultureInfo.InvariantCulture) + "|" +
                   (WasMitigated ? 1 : 0) + "|" + (HasBitten ? 1 : 0);
        }

        public void RestoreState(string blob)
        {
            Phase = CrisisPhase.Idle;
            WasMitigated = false;
            HasBitten = false;
            if (string.IsNullOrEmpty(blob)) return;

            var parts = blob.Split('|');
            if (parts.Length < 3) return;

            int phase;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out phase))
                Phase = (CrisisPhase)phase;
            WasMitigated = parts[1] == "1";
            HasBitten = parts[2] == "1";
        }
    }
}
