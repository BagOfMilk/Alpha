using System;
using System.Collections.Generic;

namespace Game.Core.Ring.Career
{
    /// <summary>Вид закулісної сцени: розмова з букером (за замовчуванням) чи подія поза рингом (за розкладом, раз на сезон).</summary>
    public enum BackstageKind { Booker = 0, Event = 1 }

    /// <summary>
    /// Коли подія відкривається сама, без «Закулісся»: <see cref="FeudFirst"/> — на початку тижня першого шоу ворожнечі з Молотом
    /// (подія мішаного матчу, P2-6: героїня проти Молота). Дії тижня не витрачає.
    /// </summary>
    public enum BackstageAuto { None = 0, FeudFirst = 1 }

    /// <summary>
    /// Закулісна сцена кар'єри (TRPG_DESIGN §6.1, §7): граф у форматі промо + умови появи. Для події: з якого тижня сезону вона
    /// «дозріває» (<see cref="FromWeek"/> + хеш у межах <see cref="Spread"/>), з якого сезону (<see cref="MinSeason"/>) і за яких
    /// умов стану (<see cref="Requires"/>). Розмови з букером умов розкладу не мають: береться перша з умовами, що виконані
    /// (розмова свого букера федерації, <c>{"career":"booker","target":…}</c>), а запасна без умов — коли жодна з них не підходить.
    /// </summary>
    public sealed class BackstageScene
    {
        public PromoDef Promo;
        public BackstageKind Kind;
        public List<FeudCondition> Requires = new List<FeudCondition>();
        public int FromWeek = 1;
        /// <summary>Скільки тижнів розкиду: день появи = FromWeek + хеш(зерно, сезон, сцена) % Spread.</summary>
        public int Spread = 1;
        public int MinSeason = 1;
        /// <summary>Подія раз на кар'єру (не раз на сезон): після першого показу більше не дозріває (скаут, TRPG_DESIGN §6.4).</summary>
        public bool Once;
        /// <summary>Подія відкривається сама (<see cref="BackstageAuto"/>), а не через «Закулісся»; така подія — лише раз на кар'єру.</summary>
        public BackstageAuto Auto;

        public string Id { get { return Promo.Id; } }
    }

    /// <summary>Усі закулісні сцени кар'єри (career_backstage.json). Стан не тримає.</summary>
    public sealed class BackstageContent
    {
        public readonly List<BackstageScene> Scenes = new List<BackstageScene>();

        public BackstageScene Find(string id)
        {
            foreach (var s in Scenes) if (s.Id == id) return s;
            return null;
        }

        public IEnumerable<PromoDef> Promos()
        {
            foreach (var s in Scenes) yield return s.Promo;
        }

        /// <summary>
        /// Схема: {"scenes":[{"id","kind":"booker|event","fromWeek","spread","minSeason","once","auto":"feud_first","requires":[умови],"title","place","start","nodes":[…]}]}.
        /// Вузли, ефекти й умови — ті самі, що в промо ворожнечі (плюс умови й ефекти кар'єри, <see cref="CareerFx"/>).
        /// </summary>
        public static BackstageContent Load(string json)
        {
            var root = MiniJson.ParseObject(json);
            var c = new BackstageContent();
            foreach (var o in JsonRead.ObjList(root, "scenes", "backstage"))
            {
                var promo = FeudScriptLoader.ReadPromo(o);
                string where = "backstage " + promo.Id;
                var sc = new BackstageScene
                {
                    Promo = promo,
                    Kind = MoveCatalog.ParseEnum<BackstageKind>(JsonRead.Str(o, "kind", where), where + ".kind"),
                    FromWeek = JsonRead.Int(o, "fromWeek", where, 1),
                    Spread = Math.Max(1, JsonRead.Int(o, "spread", where, 1)),
                    MinSeason = JsonRead.Int(o, "minSeason", where, 1),
                    Once = JsonRead.Bool(o, "once", where, false)
                };
                string auto = JsonRead.Str(o, "auto", where, false);
                if (auto == "feud_first") sc.Auto = BackstageAuto.FeudFirst;
                else if (auto != null) throw new FormatException(where + ".auto: відоме значення лише feud_first, а не '" + auto + "'");
                if (sc.Auto != BackstageAuto.None && (sc.Kind != BackstageKind.Event || !sc.Once))
                    throw new FormatException(where + ": сама відкривається лише подія раз на кар'єру (kind event, once)");
                foreach (var co in JsonRead.ObjList(o, "requires", where)) sc.Requires.Add(FeudCondition.FromJson(co, where + ".requires"));
                if (c.Find(sc.Id) != null) throw new FormatException("повторна закулісна сцена " + sc.Id);
                c.Scenes.Add(sc);
            }
            bool fallback = false;
            foreach (var s in c.Scenes) if (s.Kind == BackstageKind.Booker && s.Requires.Count == 0) fallback = true;
            if (!fallback) throw new FormatException("закулісся: немає розмови з букером без умов (запасної)");
            return c;
        }
    }
}
