using System;
using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Панель «Люди» (C, docs/UX_DESIGN.md §5.11): жетони людей зі статусом і
    /// лояльністю словом, а не таблиця чисел (UI-13). Числа лише ті, що
    /// дозволені (рівень, кількість шрамів — R17); лояльність — лише словом
    /// (UI-06). Порожній стан навчає (UX-13).
    /// </summary>
    public static class UxPeoplePanel
    {
        public static UxPanelModel Build(RosterView roster, Gender g)
        {
            var panel = new UxPanelModel
            {
                Id = UxPanelId.People,
                Title = UkrainianText.Get(UxPanelCatalog.Get(UxPanelId.People).TitleKey, g)
            };
            if (roster == null || roster.Companions == null || roster.Companions.Count == 0)
            {
                panel.EmptyText = UkrainianText.Get("ux.people.empty", g);
                return panel;
            }

            foreach (var c in roster.Companions)
            {
                var card = new UxCard
                {
                    Title = ScreenText.ResolveCompanionName(c.Id, g, roster),
                    Subtitle = ScreenText.CompanionStatusLabel(c.Id, c.Status, g)
                };
                if (!string.IsNullOrEmpty(c.AssignedSlotId))
                    card.Chips.Add(new UxChip(UkrainianText.Get("post." + c.AssignedSlotId, g), UxTone.Own));
                if (c.Loyalty.HasValue)
                    card.Chips.Add(new UxChip(ScreenText.LoyaltyLabel(c.Loyalty, g)));
                card.Chips.Add(new UxChip(UkrainianText.Format("ux.people.level", g, "n", c.Level.ToString())));
                if (c.ScarCount > 0)
                    card.Chips.Add(new UxChip(UkrainianText.Format("ux.people.scars", g, "n", c.ScarCount.ToString()), UxTone.Bad));
                panel.Cards.Add(card);
            }
            return panel;
        }
    }

    /// <summary>
    /// Панель «Журнал» (J, docs/UX_DESIGN.md §5.12): квести, світ, загроза,
    /// вилазки — чотири підвкладки. Панель лише показує і веде до місця
    /// (UX-04): дія квесту — у квестодавця, збори — на Заставі. Готовність —
    /// лише словом, без «віхи N з M» (GDD US-11.4; UX_DESIGN §2 п. 9).
    /// </summary>
    public static class UxJournalPanel
    {
        public const string ScoutingPostPlace = "post:scouting_post";

        public static UxPanelModel Build(QuestOfferView currentOffer, FactionsView factions, ReadinessView readiness, Gender g)
        {
            var panel = new UxPanelModel
            {
                Id = UxPanelId.Journal,
                Title = UkrainianText.Get(UxPanelCatalog.Get(UxPanelId.Journal).TitleKey, g)
            };
            string quests = UkrainianText.Get("ux.journal.section.quests", g);
            string world = UkrainianText.Get("ux.journal.section.world", g);
            string threat = UkrainianText.Get("ux.journal.section.threat", g);
            string trips = UkrainianText.Get("ux.journal.section.expeditions", g);

            // Квести: поточна пропозиція (read-only; OfferQuestStage тут не кличемо — він пише в журнал подій).
            if (currentOffer != null && !string.IsNullOrEmpty(currentOffer.QuestId))
            {
                var card = new UxCard { Section = quests, Title = ScreenText.QuestName(currentOffer.QuestId, g) };
                string check = ScreenText.QuestCheckLine(currentOffer, g);
                if (!string.IsNullOrEmpty(check)) card.Lines.Add(check);
                panel.Cards.Add(card);
            }
            else
            {
                var card = new UxCard { Section = quests, Title = UkrainianText.Get("ux.journal.quests.none", g) };
                card.Lines.Add(UkrainianText.Get("ux.journal.quests.where", g));
                panel.Cards.Add(card);
            }

            // Світ: щабель кожної сторони словом (№10, UI-06).
            if (factions != null && factions.Factions != null)
            {
                foreach (var f in factions.Factions)
                {
                    string nameKey = "faction." + f.Id;
                    var card = new UxCard
                    {
                        Section = world,
                        Title = UkrainianText.Has(nameKey, g) ? UkrainianText.Get(nameKey, g) : f.DisplayName
                    };
                    card.Chips.Add(new UxChip(ScreenText.FactionBandLabel(f.Band, g)));
                    panel.Cards.Add(card);
                }
            }

            // Загроза: лише слово готовності (без кількості віх).
            var threatCard = new UxCard { Section = threat, Title = UkrainianText.Get("ux.journal.threat.title", g) };
            if (readiness != null && !string.IsNullOrEmpty(readiness.Band))
                threatCard.Chips.Add(new UxChip(ScreenText.ReadinessLabel(readiness.Band, g)));
            panel.Cards.Add(threatCard);

            // Вилазки: картка веде на Заставу, власної дії не має.
            var tripCard = new UxCard
            {
                Section = trips,
                Title = UkrainianText.Get("ux.journal.expeditions.title", g),
                LinkPlaceId = ScoutingPostPlace
            };
            tripCard.Lines.Add(UkrainianText.Get("ux.journal.expeditions.where", g));
            panel.Cards.Add(tripCard);
            return panel;
        }
    }

    /// <summary>
    /// Панель «Збереження» (Esc → Зберегти / Завантажити, docs/UX_DESIGN.md
    /// §5.14). Картка слота — доба і підпис, дії з підтвердженням лише для
    /// незворотного (UX-12): перезапис зайнятого слота і завантаження поверх
    /// поточної гри. Запис на диск і свіжа сесія для завантаження — справа
    /// оболонки, тому виконання передається ззовні.
    /// </summary>
    public static class UxSavePanel
    {
        public static UxPanelModel Build(IReadOnlyList<SaveSlotView> slots, Gender g,
            Func<int, UxOutcome> save, Func<int, UxOutcome> load, bool allowLoad)
        {
            var panel = new UxPanelModel
            {
                Id = UxPanelId.Save,
                Title = UkrainianText.Get(UxPanelCatalog.Get(UxPanelId.Save).TitleKey, g)
            };
            if (slots == null || slots.Count == 0)
            {
                panel.EmptyText = UkrainianText.Get("ux.save.empty", g);
                return panel;
            }

            foreach (var slot in slots)
            {
                int index = slot.Slot;
                string label = ScreenText.SlotLabel(index, g);
                var card = new UxCard
                {
                    Title = ScreenText.SaveSlotLine(index, slot.Occupied, slot.Headline, slot.Day, g)
                };

                if (index >= 0)
                {
                    var saveAction = new UxAction
                    {
                        Id = "save:" + index,
                        Label = UkrainianText.Get("ux.save.action.save", g),
                        Intent = UxIntent.Primary,
                        Execute = save != null ? () => save(index) : (Func<UxOutcome>)null
                    };
                    saveAction.AllowedStates.Add(SessionState.Morning);
                    saveAction.AllowedStates.Add(SessionState.FreePlay);
                    if (slot.Occupied)
                        saveAction.Confirm = new UxConfirm(
                            UkrainianText.Format("ux.save.confirm.overwrite", g, "slot", label),
                            UkrainianText.Get("ux.save.confirm.overwrite.verb", g),
                            new[] { UkrainianText.Get("ux.save.confirm.overwrite.loss", g) });
                    card.Actions.Add(saveAction);
                }

                if (allowLoad && slot.Occupied)
                {
                    card.Actions.Add(new UxAction
                    {
                        Id = "load:" + index,
                        Label = UkrainianText.Get("ux.save.action.load", g),
                        Intent = UxIntent.Secondary,
                        Execute = load != null ? () => load(index) : (Func<UxOutcome>)null,
                        Confirm = new UxConfirm(
                            UkrainianText.Format("ux.save.confirm.load", g, "slot", label),
                            UkrainianText.Get("ux.save.confirm.load.verb", g),
                            new[] { UkrainianText.Get("ux.save.confirm.load.loss", g) })
                    });
                }
                panel.Cards.Add(card);
            }
            return panel;
        }
    }
}
