using System.Collections.Generic;
using Game.Core.Checks;
using Game.Core.Quests;

namespace Game.Core.Scenes
{
    /// <summary>
    /// Портретні сцени з вибором навколо напарників і ради (Поправка №7.8):
    /// глава арки Мирослави (довіра), нічна розмова-конфронтація зради,
    /// запасна "довірча" сцена, коли зрада не насувається, епілоги другої
    /// глави, і рада Захара перед фіналом. Текста немає — лише ключі (R7):
    /// слова живуть у <c>UkrainianText</c>.
    ///
    /// Усі наслідки — <see cref="QuestConsequence"/>, той самий тип, що і
    /// квестовий рушій: один застосувач наслідку на квести/сцени/арки
    /// (<c>GameSession.ApplyConsequence</c>), як того вимагає власник.
    /// </summary>
    public static class CompanionScenes
    {
        // ---- ідентифікатори виборів (спільні з ботами, Core/Session/Bots) ----
        public const string MyroslavaTrustChoiceId = "myroslava_trust_choice";
        public const string MyroslavaEpilogueChoiceId = "myroslava_epilogue_choice";
        public const string MaksymEpilogueChoiceId = "maksym_epilogue_choice";
        public const string MyroslavaConfrontationChoiceId = "myroslava_confrontation_choice";
        public const string MyroslavaCheckupChoiceId = "myroslava_checkup_choice";
        public const string ZakharCouncilChoiceId = "zakhar_council_choice";

        // ---- прапори наслідку (StoryFlags) ----
        public const string MyroslavaTrustedFlag = "myroslava_trusted";
        public const string MyroslavaWatchedFlag = "myroslava_watched";
        public const string MyroslavaSentAwayFlag = "myroslava_sent_away";

        // M1.2: вибори епілогів глав 2 і перевірки стану — читає StoryEchoes (рядки підсумку).
        public const string MyroslavaCh2RememberFlag = "myroslava_ch2_remember";
        public const string MyroslavaCh2SilenceFlag = "myroslava_ch2_silence";
        public const string MyroslavaCheckupReassureFlag = "myroslava_checkup_reassure";
        public const string MyroslavaCheckupSpaceFlag = "myroslava_checkup_space";
        public const string MaksymCh2ForgiveFlag = "maksym_ch2_forgive";
        public const string MaksymCh2GuardFlag = "maksym_ch2_guard";

        public const string MyroslavaConfrontedTrustFlag = "myroslava_confronted_trust";
        public const string MyroslavaConfrontedFailedFlag = "myroslava_confronted_failed";
        public const string MyroslavaConfrontedProvokedFlag = "myroslava_confronted_provoked";
        public const string MyroslavaConfrontedReleaseFlag = "myroslava_confronted_release";
        public const string MyroslavaConfrontationResolvedFlag = "myroslava_confrontation_resolved";
        public const string MyroslavaDefectionExecutedFlag = "myroslava_defection_executed";

        public const string ZakharPreparedDamFlag = "zakhar_prepared_dam";
        public const string ZakharPreparedAssaultFlag = "zakhar_prepared_assault";
        public const string ZakharCouncilDoneFlag = "zakhar_council_done";

        /// <summary>Глава 1 арки Мирослави, доба 2 увечері — «Донька боярина» (гейт Steady, DefaultArcs).</summary>
        public static Scene MyroslavaTrustArc()
        {
            return new Scene("arc.myroslava.ch1", "scene.myroslava.ch1.title")
                .Step(SceneStep.Shot("myroslava", ShotFraming.Close))
                .Step(SceneStep.Line("myroslava", "scene.myroslava.ch1.open"))
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Beat(1.0))
                .Step(SceneStep.Choice(MyroslavaTrustChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.Simple("trust", "scene.myroslava.ch1.option.trust",
                        new QuestConsequence().Loyalty("myroslava", 10).Flag(MyroslavaTrustedFlag),
                        transitionKey: "to.arc.myroslava.ch1.done"),
                    SceneChoiceOption.Simple("watch", "scene.myroslava.ch1.option.watch",
                        new QuestConsequence().Flag(MyroslavaWatchedFlag),
                        transitionKey: "to.arc.myroslava.ch1.done"),
                    SceneChoiceOption.Simple("send_away", "scene.myroslava.ch1.option.send_away",
                        new QuestConsequence().Loyalty("myroslava", -10).Flag(MyroslavaSentAwayFlag),
                        transitionKey: "to.arc.myroslava.ch1.done")
                }));
        }

        /// <summary>Глава 2 арки Мирослави (гейт Devoted) — короткий епілог довіри, здебільшого доступний у вільній грі.</summary>
        public static Scene MyroslavaEpilogue()
        {
            return new Scene("arc.myroslava.ch2", "scene.myroslava.ch2.title")
                .Step(SceneStep.Shot("myroslava", ShotFraming.Two, "protagonist"))
                .Step(SceneStep.Line("myroslava", "scene.myroslava.ch2.open"))
                .Step(SceneStep.Choice(MyroslavaEpilogueChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.Simple("remember", "scene.myroslava.ch2.option.remember",
                        new QuestConsequence().WithXp(15).Flag(MyroslavaCh2RememberFlag),
                        transitionKey: "to.arc.myroslava.ch2.done"),
                    SceneChoiceOption.Simple("silence", "scene.myroslava.ch2.option.silence",
                        new QuestConsequence().Loyalty("myroslava", 5).Flag(MyroslavaCh2SilenceFlag),
                        transitionKey: "to.arc.myroslava.ch2.done")
                }));
        }

        /// <summary>Глава 2 арки Максима (гейт Devoted) — епілог вірності громаді.</summary>
        public static Scene MaksymEpilogue()
        {
            return new Scene("arc.maksym.ch2", "scene.maksym.ch2.title")
                .Step(SceneStep.Shot("maksym", ShotFraming.Two, "protagonist"))
                .Step(SceneStep.Line("maksym", "scene.maksym.ch2.open"))
                .Step(SceneStep.Choice(MaksymEpilogueChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.Simple("forgive", "scene.maksym.ch2.option.forgive",
                        new QuestConsequence().WithXp(15).Flag(MaksymCh2ForgiveFlag),
                        transitionKey: "to.arc.maksym.ch2.done"),
                    SceneChoiceOption.Simple("guard", "scene.maksym.ch2.option.guard",
                        new QuestConsequence().Loyalty("maksym", 5).Flag(MaksymCh2GuardFlag),
                        transitionKey: "to.arc.maksym.ch2.done")
                }));
        }

        /// <summary>
        /// Нічна розмова — конфронтація зради (доба 3, ніч/увечері): показ
        /// вузла, коли зрада насувається (сюжетний прапор посіяно + полоса
        /// довіри вже на дні). Персуейд рятує (довіра відновлена),
        /// звинувачення/погроза провокує негайну зраду, «відпустити» —
        /// теж зрада, але з м'якшим прапором для фіналу.
        ///
        /// M1.2: вибір у главі 1 арки (доба 2) лишає слід у порогах цієї розмови —
        /// прапори <c>myroslava_trusted/watched/sent_away</c> читає
        /// <see cref="ConfrontationThresholds"/>; пороги видно в прев'ю варіанта заздалегідь.
        /// </summary>
        public static Scene MyroslavaConfrontation(bool trustedInCh1 = false, bool watchedInCh1 = false, bool sentAwayInCh1 = false)
        {
            var thresholds = ConfrontationThresholds(trustedInCh1, watchedInCh1, sentAwayInCh1);
            var persuadeBands = new[]
            {
                new QuestConsequence().Loyalty("myroslava", -5).Flag(MyroslavaConfrontedFailedFlag),
                new QuestConsequence().Loyalty("myroslava", -5).Flag(MyroslavaConfrontedFailedFlag),
                new QuestConsequence().Loyalty("myroslava", 25).Flag(MyroslavaConfrontedTrustFlag),
                new QuestConsequence().Loyalty("myroslava", 35).Flag(MyroslavaConfrontedTrustFlag)
            };
            var accuseBands = new[]
            {
                new QuestConsequence().Tension(15).Flag(MyroslavaConfrontedProvokedFlag),
                new QuestConsequence().Tension(10).Flag(MyroslavaConfrontedProvokedFlag),
                new QuestConsequence().Tension(5).Flag(MyroslavaConfrontedProvokedFlag),
                new QuestConsequence().Tension(0).Flag(MyroslavaConfrontedProvokedFlag)
            };

            return new Scene("scene.myroslava.confrontation", "scene.myroslava.confrontation.title")
                .Step(SceneStep.Shot("myroslava", ShotFraming.Close))
                .Step(SceneStep.Line("myroslava", "scene.myroslava.confrontation.open"))
                .Step(SceneStep.Shot("protagonist", ShotFraming.Close))
                .Step(SceneStep.Beat(1.0))
                .Step(SceneStep.Choice(MyroslavaConfrontationChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.WithCheck("persuade", "scene.myroslava.confrontation.option.persuade",
                        SkillKeys.Persuade, thresholds.Persuade, ApproachForm.Persuade, persuadeBands,
                        transitionKey: "to.confrontation.resolved"),
                    SceneChoiceOption.WithCheck("accuse", "scene.myroslava.confrontation.option.accuse",
                        SkillKeys.Intimidate, thresholds.Accuse, ApproachForm.Intimidate, accuseBands,
                        transitionKey: "to.confrontation.resolved"),
                    SceneChoiceOption.Simple("release", "scene.myroslava.confrontation.option.release",
                        new QuestConsequence().Loyalty("myroslava", -10).Flag(MyroslavaConfrontedReleaseFlag),
                        transitionKey: "to.confrontation.resolved")
                }));
        }

        /// <summary>Базовий поріг «переконати» на нічній розмові (ПЛЕЙСХОЛДЕР — як і решта чисел збірки).</summary>
        public const int ConfrontationPersuadeBaseThreshold = 4;

        /// <summary>Базовий поріг «звинуватити» (Залякування) на нічній розмові (ПЛЕЙСХОЛДЕР).</summary>
        public const int ConfrontationAccuseBaseThreshold = 5;

        /// <summary>
        /// Пороги нічної розмови після вибору в главі 1 арки Мирослави (M1.2, «вибір → наслідок»):
        /// довіра (<c>myroslava_trusted</c>) — вона вислухає, «переконати» легше на 1;
        /// пильнування (<c>myroslava_watched</c>) — ти знаєш, що саме закинути, «звинуватити» легше на 1;
        /// відіслана (<c>myroslava_sent_away</c>) — пам'ятає образу, «переконати» важче на 1.
        /// Детерміновано, без нових шкал; поріг не падає нижче 1.
        /// </summary>
        public static (int Persuade, int Accuse) ConfrontationThresholds(bool trusted, bool watched, bool sentAway)
        {
            int persuade = ConfrontationPersuadeBaseThreshold - (trusted ? 1 : 0) + (sentAway ? 1 : 0);
            int accuse = ConfrontationAccuseBaseThreshold - (watched ? 1 : 0);
            return (System.Math.Max(1, persuade), System.Math.Max(1, accuse));
        }

        /// <summary>Запасна сцена того самого вузла (доба 3), коли зрада НЕ насувається — звичайна перевірка стосунків, без ставок.</summary>
        public static Scene MyroslavaTrustCheckup()
        {
            return new Scene("scene.myroslava.checkup", "scene.myroslava.checkup.title")
                .Step(SceneStep.Shot("myroslava", ShotFraming.Two, "protagonist"))
                .Step(SceneStep.Line("myroslava", "scene.myroslava.checkup.open"))
                .Step(SceneStep.Choice(MyroslavaCheckupChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.Simple("reassure", "scene.myroslava.checkup.option.reassure",
                        new QuestConsequence().Loyalty("myroslava", 5).Flag(MyroslavaCheckupReassureFlag),
                        transitionKey: "to.checkup.resolved"),
                    SceneChoiceOption.Simple("space", "scene.myroslava.checkup.option.space",
                        new QuestConsequence().Flag(MyroslavaCheckupSpaceFlag),
                        transitionKey: "to.checkup.resolved")
                }));
        }

        /// <summary>Рада Захара перед фіналом (доба 5, увечері) — готує тихий («загатити річку») або кровавий («тримати перевал») шлях фіналу.</summary>
        public static Scene ZakharCouncil()
        {
            return new Scene("scene.zakhar.council", "scene.zakhar.council.title")
                .Step(SceneStep.Shot("zakhar", ShotFraming.Close))
                .Step(SceneStep.Line("zakhar", "scene.zakhar.council.open"))
                .Step(SceneStep.Choice(ZakharCouncilChoiceId, new List<SceneChoiceOption>
                {
                    SceneChoiceOption.Simple("dam", "scene.zakhar.council.option.dam",
                        new QuestConsequence().Flag(ZakharPreparedDamFlag),
                        transitionKey: "to.council.resolved"),
                    SceneChoiceOption.Simple("assault", "scene.zakhar.council.option.assault",
                        new QuestConsequence().Flag(ZakharPreparedAssaultFlag),
                        transitionKey: "to.council.resolved")
                }));
        }
    }
}
