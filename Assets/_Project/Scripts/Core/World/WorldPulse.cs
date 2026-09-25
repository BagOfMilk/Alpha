using System;
using System.Collections.Generic;
using Game.Core.Balance;

namespace Game.Core.World
{
    /// <summary>Що накопичувачі видали за фазу.</summary>
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
    /// Пульс світу: паралельні накопичувачі тиску замість планувальника подій.
    ///
    /// Повністю детермінований. Непередбачуваність дає не випадковість, а
    /// неповнота інформації: накопичувачів декілька, ставки в них різні і
    /// змінюються від дій гравця, пороги і заряди приховані, а список активних
    /// джерел гравець дізнається лише через передвісники.
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

        /// <summary>Скільки накопичувачів реально працює зараз — для інваріанту «не менше трьох».</summary>
        public int CountActive(PulseContext ctx)
        {
            int n = 0;
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i].IsActive(ctx)) n++;
            return n;
        }

        public PulseTick Advance(PulseContext ctx) => Advance(ctx, null);

        /// <summary>
        /// Фаза пульсу з питанням «чи є чим спрацювати». <paramref name="hasConsequence"/>
        /// відповідає за Id джерела, чи знайдеться для нього інцидент прямо зараз;
        /// питає крок дня, бо таблицю знає він, а не накопичувач.
        ///
        /// Готове джерело, якому нема чим спрацювати, НЕ розряджається: заряд і
        /// почута ступінь лишаються, а бюджет спрацювань фази дістається
        /// тим, у кого наслідок є. Інакше Fire() обнуляв би заряд і драбину,
        /// крок інцидентів не знаходив би, що розбирати, і йшов далі — скидання без
        /// жодного сліду у звіті (так жив «Тугар» у зрізі першого часу: чутка про
        /// боярина дійшла до третьої ступені і мовчки починалась заново).
        ///
        /// null — попередня поведінка: розряджається будь-яке готове джерело.
        /// </summary>
        public PulseTick Advance(PulseContext ctx, Func<string, bool> hasConsequence)
        {
            var forewarnings = new List<Forewarning>();

            // Накопичення. Порядок обходу не впливає на результат: кожне джерело
            // копить незалежно, а нічиї при відборі розв'язуються за Id.
            for (int i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                var track = _tracks[source.Id];

                if (!source.IsActive(ctx))
                    continue;

                track.Accumulate(source.InsistencePerDay(ctx), _cfg);

                // Кандидат у передвісники. Ступінь НЕ зараховується тут:
                // зарахує її той, хто доставить сигнал гравцю (крок Pulse).
                //
                // Видається РІВНО ОДНА ступінь за раз, а не поточна. Інакше
                // накопичувач, що проскочив за ніч два пороги, доносить одразу
                // третю, і друга — єдина, зобов'язана назвати місце —
                // губиться мовчки. Драбину потрібно проходити, а не перестрибувати.
                int level = track.ForewarnLevel(_cfg);
                if (source.Announces && level > track.DeliveredLevel && !track.HoldsForewarnings(ctx.Day, _cfg))
                    forewarnings.Add(new Forewarning(track.SourceId, track.DeliveredLevel + 1, track.DomainTag));
            }

            // Відбір тих, що спрацювали: найбільш заповнені першими, нічиї — за Id.
            var ready = new List<PressureTrack>();
            foreach (var source in _sources)
            {
                var track = _tracks[source.Id];
                if (!source.IsActive(ctx) || !track.IsReady(ctx.Day, _cfg))
                    continue;

                // Готове, але розбирати нічого — тримаємо заряд, а не палимо намарно.
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

            // Джерело, що вдарило, у цьому ж тіку не попереджає: удар сам і
            // є подією. Інакше ступінь, набрана разом з ударом, йшла б
            // гравцю поряд з інцидентом, а зараховувалась (MarkDelivered — після
            // Advance) уже на обнулений трек: наступне коло починалося б з
            // «третя почута» і мовчало б до самого удару.
            if (fired.Count > 0)
                forewarnings.RemoveAll(f => fired.Contains(f.SourceId));

            forewarnings.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));
            return new PulseTick(fired, forewarnings);
        }

        /// <summary>
        /// Позначити, що передвісники дійшли до гравця. Викликає крок дня: тільки він
        /// знає, спав гравець чи патрулював.
        /// </summary>
        public void MarkDelivered(IReadOnlyList<Forewarning> delivered, int day)
        {
            if (delivered == null) return;
            for (int i = 0; i < delivered.Count; i++)
                if (_tracks.TryGetValue(delivered[i].SourceId, out var track))
                    track.MarkDelivered(delivered[i].Level, day);
        }

        /// <summary>Ступінь, яку гравець ПОЧУВ — для перевірок і тестів.</summary>
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
        /// Фікс-рев’ю D1b: одноразовий стрибок НЕ сміє сам дістати чи
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
        /// Фікс-рев’ю D1b (мінор): повертає РЕАЛЬНО застосований заряд (0, якщо
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
