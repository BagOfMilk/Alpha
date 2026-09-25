using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>Что накопители выдали за фазу.</summary>
    public readonly struct PulseTick
    {
        public readonly IReadOnlyList<string> FiredSourceIds;
        public readonly IReadOnlyList<Forewarning> Forewarnings;

        public PulseTick(IReadOnlyList<string> fired, IReadOnlyList<Forewarning> forewarnings)
        {
            FiredSourceIds = fired;
            Forewarnings = forewarnings;
        }
    }

    /// <summary>
    /// Пульс мира: параллельные накопители давления вместо планировщика событий.
    ///
    /// Полностью детерминирован. Непредсказуемость даёт не случайность, а
    /// неполнота информации: накопителей несколько, ставки у них разные и
    /// меняются от действий игрока, пороги и заряды скрыты, а список активных
    /// источников игрок узнаёт только через предвестники.
    /// </summary>
    public sealed class WorldPulse
    {
        private readonly List<IPressureSource> _sources = new List<IPressureSource>();
        private readonly Dictionary<string, PressureTrack> _tracks = new Dictionary<string, PressureTrack>();
        private readonly PulseBalance _cfg;

        public WorldPulse(PulseBalance cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        }

        public void AddSource(IPressureSource source)
        {
            if (source == null || _tracks.ContainsKey(source.Id)) return;
            _sources.Add(source);
            _tracks.Add(source.Id, new PressureTrack(source, _cfg));
        }

        internal IReadOnlyDictionary<string, PressureTrack> Tracks => _tracks;

        /// <summary>Сколько накопителей реально работает сейчас — для инварианта «не меньше трёх».</summary>
        public int CountActive(PulseContext ctx)
        {
            int n = 0;
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].IsActive(ctx)) n++;
            return n;
        }

        public PulseTick Advance(PulseContext ctx) => Advance(ctx, null);

        /// <summary>
        /// Фаза пульса с вопросом «есть ли чем сработать». <paramref name="hasConsequence"/>
        /// отвечает по Id источника, найдётся ли для него инцидент прямо сейчас;
        /// спрашивает шаг дня, потому что таблицу знает он, а не накопитель.
        ///
        /// Готовый источник, которому нечем сработать, НЕ разряжается: заряд и
        /// услышанная ступень остаются, а бюджет срабатываний фазы достаётся
        /// тем, у кого последствие есть. Иначе Fire() обнулял заряд и лестницу,
        /// шаг инцидентов не находил, что разбирать, и шёл дальше — сброс без
        /// единого следа в отчёте (так жил «Тугар» в срезе первого часа: слух о
        /// боярине дошёл до третьей ступени и молча начинался заново).
        ///
        /// null — прежнее поведение: разряжается всякий готовый источник.
        /// </summary>
        public PulseTick Advance(PulseContext ctx, Func<string, bool> hasConsequence)
        {
            var forewarnings = new List<Forewarning>();

            // Накопление. Порядок обхода не влияет на результат: каждый источник
            // копит независимо, а ничьи при отборе разрешаются по Id.
            for (int i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                var track = _tracks[source.Id];

                if (!source.IsActive(ctx))
                    continue;

                track.Accumulate(source.InsistencePerDay(ctx), _cfg);

                // Кандидат в предвестники. Ступень НЕ засчитывается здесь:
                // засчитает её тот, кто доставит сигнал игроку (шаг Pulse).
                //
                // Выдаётся РОВНО ОДНА ступень за раз, а не текущая. Иначе
                // накопитель, проскочивший за ночь два порога, доносит сразу
                // третью, и вторая — единственная, обязанная назвать место —
                // теряется молча. Лестницу нужно проходить, а не перепрыгивать.
                int level = track.ForewarnLevel(_cfg);
                if (source.Announces && level > track.DeliveredLevel && !track.HoldsForewarnings(ctx.Day, _cfg))
                    forewarnings.Add(new Forewarning(track.SourceId, track.DeliveredLevel + 1, track.DomainTag));
            }

            // Отбор сработавших: самые заполненные первыми, ничьи — по Id.
            var ready = new List<PressureTrack>();
            foreach (var source in _sources)
            {
                var track = _tracks[source.Id];
                if (!source.IsActive(ctx) || !track.IsReady(ctx.Day, _cfg))
                    continue;

                // Готов, но разбирать нечего — держим заряд, а не жжём впустую.
                if (hasConsequence != null && !hasConsequence(source.Id))
                    continue;

                ready.Add(track);
            }

            ready.Sort((a, b) =>
            {
                int byFill = b.Fill.CompareTo(a.Fill);
                if (byFill != 0) return byFill;
                return string.CompareOrdinal(a.SourceId, b.SourceId);
            });

            int budget = ctx.IsNight ? _cfg.MaxFiresPerNight : _cfg.MaxFiresPerDay;
            var fired = new List<string>();
            for (int i = 0; i < ready.Count && fired.Count < budget; i++)
            {
                ready[i].Fire(ctx.Day);
                fired.Add(ready[i].SourceId);
            }

            // Ударивший источник в этом же тике не предупреждает: удар сам и
            // есть событие. Иначе ступень, набранная вместе с ударом, уходила
            // игроку рядом с инцидентом, а засчитывалась (MarkDelivered — после
            // Advance) уже на обнулённый трек: следующий круг начинался с
            // «третья услышана» и молчал до самого удара.
            if (fired.Count > 0)
                forewarnings.RemoveAll(f => fired.Contains(f.SourceId));

            forewarnings.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));
            return new PulseTick(fired, forewarnings);
        }

        /// <summary>
        /// Отметить, что предвестники дошли до игрока. Вызывает шаг дня: только он
        /// знает, спал игрок или патрулировал.
        /// </summary>
        public void MarkDelivered(IReadOnlyList<Forewarning> delivered, int day)
        {
            if (delivered == null) return;
            for (int i = 0; i < delivered.Count; i++)
                if (_tracks.TryGetValue(delivered[i].SourceId, out var track))
                    track.MarkDelivered(delivered[i].Level, day);
        }

        /// <summary>Ступень, которую игрок УСЛЫШАЛ — для проверок и тестов.</summary>
        internal int DeliveredLevelOf(string sourceId)
        {
            return _tracks.TryGetValue(sourceId, out var track) ? track.DeliveredLevel : 0;
        }

        /// <summary>
        /// D1b (seamsForD1 B3, іменний предмет «Ріг вивідника», ефект
        /// «forewarn_boost»): додатковий, ЧИСТО АДИТИВНИЙ сейв — не змінює
        /// жодного існуючого члена/поведінки. Одноразово підкидає заряд
        /// накопичувачу так, ніби він сам накопичив трохи більше за день
        /// (той самий шлях, що й <see cref="PressureTrack.Accumulate"/> уже
        /// зве кожен тік) — WorldPulse.Advance сам гарантує "рівно одна
        /// ступінь за тік" (коментар вище), тож великий разовий заряд
        /// природно розтягується на кілька НАСТУПНИХ попереджень, що
        /// приходять РАНІШЕ, ніж прийшли б без нього, а не миттєво всі одразу.
        /// Немає джерела випадковості (R1) — сума фіксована й детермінована.
        ///
        /// Фикс-ревью D1b: одноразовий стрибок НЕ сміє сам дістати чи
        /// перескочити Threshold. У Тугара (Kind=InternalThreat, не Crisis)
        /// IsReady() вимагає лише Charge&gt;=Threshold — без жодної
        /// прив'язки до того, скільки ступенів попереджень гравець УЖЕ
        /// почув, а зареєстрованого IncidentDefinition з SourceId=="tuhar"
        /// у першій годині ще нема (боярин лишається предвісником, не
        /// інцидентом: його розв'язка — сценарний Фінал). Тож перескочити
        /// Threshold ОДНИМ стрибком BoostCharge означало б (до воріт в
        /// Advance, див. нижче): WorldPulse.Fire() тут-таки, в тому самому
        /// виклику Advance(), мовчки скидає
        /// Charge/DeliveredLevel у нуль (IncidentTable.Pick поверне null —
        /// жодної події в лозі) — найгостріший, повністю усувний випадок,
        /// коли гравець знаходить ріг ПІЗНІШЕ, а не на добу 1, і Charge вже
        /// сам близько до порога від звичайного накопичення. Обмежуємо
        /// стрибок вільним місцем ДО порога (Threshold-1).
        ///
        /// Мовчазний розряд як такий закрито окремо (24.09.2026): Advance
        /// розряджає лише джерело, для якого в таблиці є інцидент
        /// (IncidentStep.HasIncidentFor), тож Тугар, дійшовши до порога сам
        /// (тіка кожну фазу — §1 рядок 8 табл. TEST_BUILD.md, поріг — на добі
        /// 3), тримає заряд і почуту третю ступінь до Фіналу. Клямп лишається:
        /// ріг пришвидшує драбину, а не стріляє сам — це запрацює, щойно в
        /// Тугара з'явиться власний інцидент. Наслідок для чисел (відкрито,
        /// вирішує власник): за сценарієм §3.4 ріг знаходять зранку доби 4,
        /// коли драбина Тугара вже пройдена, — room&lt;=0, і ріг у першій
        /// годині нічого не додає.
        ///
        /// Фикс-ревью D1b (мінор): повертає РЕАЛЬНО застосований заряд (0, якщо
        /// накопичувач невідомий, amount&lt;=0 або room&lt;=0 — впритул до
        /// порога). Раніше викликач (GameSession.GrantNamedItemById) логував
        /// "item.scout_horn.forewarn_boosted" БЕЗУМОВНО одразу після виклику —
        /// у вузькому вікні, де Тугар уже в 1 очці від Threshold, клямп мовчки
        /// зрізав ВЕСЬ буст (room&lt;=0 → 0 застосовано), а подія все одно
        /// йшла в лог, обіцяючи ефект, якого не було. Повертаючи прикладену
        /// суму, даємо викликачу самому вирішити, логувати подію чи ні —
        /// саме числа клямпа (Threshold-1 як стеля) це не чіпає.
        /// </summary>
        public int BoostCharge(string sourceId, int amount)
        {
            if (amount <= 0) return 0;
            if (!_tracks.TryGetValue(sourceId, out var track)) return 0;

            int room = track.Threshold - 1 - track.Charge;
            if (room <= 0) return 0;

            int applied = Math.Min(amount, room);
            track.Accumulate(applied, _cfg);
            return applied;
        }
    }
}
