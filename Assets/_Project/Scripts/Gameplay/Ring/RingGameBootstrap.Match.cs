using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;
using Game.Core.Ring.Match;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.Stage;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.Ring.UI.Menus;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Бій у хості гри (зона «ПМ + Бій», docs/ring/ZONES.md): хід бота за героя в огляді (<c>-ring-shots</c>) і кадри тактів
    /// режисера. Каркас хоста — RingGameBootstrap.cs; окремий файл, щоб правки бою не перемішувались зі сценою й кар'єрою.
    /// </summary>
    public sealed partial class RingGameBootstrap
    {
        /// <summary>
        /// Огляд: хід бота за героя в матчі. Бот грає тим самим ШІ, що й за суперника (раніше випадкові цифри — фінішера огляд
        /// так і не побачив; власник 04.11.2026: «це погано»). true — хід зроблено, крок огляду на цьому скінчено.
        /// </summary>
        private bool BotMatchStep()
        {
            var eng = !_onTitle ? _career.Engine : null;
            if (eng == null || eng.IsOver || !BotTurn(eng)) return false;
            if (!ReviewRopes(eng)) MatchAi.Step(eng);
            _view.Refresh();
            return true;
        }

        private int _beatShot = -1;
        private float _beatShotAt = -1f;

        /// <summary>
        /// Кадр кожного такту режисера (кидок, утримання, фінішер) — наступним кадром після такту: виноска вже на екрані, а
        /// тіло ще на контакті (огляд іде на ×3, тож давніші 0,15 с реального часу були 0,45 с гри — після контакту).
        /// Утримання — кадр на кожну долоню рефері (<c>pin&lt;ступінь&gt;_c1/_c2/_c3</c>).
        /// </summary>
        private void BeatShots()
        {
            var stage = RingStageHub.Current;
            if (stage == null || stage.BeatEvent == null) return;
            if (stage.BeatSeq != _beatShot) { _beatShot = stage.BeatSeq; _beatShotAt = Time.unscaledTime; return; }
            if (_beatShotAt < 0f || Time.unscaledTime < _beatShotAt || _shotIndex >= ShotLimit) return;
            _beatShotAt = -1f;
            var ev = stage.BeatEvent;
            if (ev.Kind == MatchEventKind.TurnEnd || ev.Kind == MatchEventKind.Sell) return;
            string kind = ev.Kind == MatchEventKind.Kickout ? "pin" + (int)ev.Tier + "_c" + stage.BeatPhase : ev.Kind + "_" + ev.Action + (ev.HasRoll ? "_" + ev.Tier : "");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(_shotsDir, string.Format("{0:000}_beat_{1}.png", _shotIndex++, kind)));
        }

        private int _reviewRopes;

        /// <summary>
        /// Канати й кут в огляді (08.11.2026, пакет «Відео огляд», п. 8: «на кадрах сезону видно біг у канати й відскок
        /// спиною»). ШІ ці дії не обирає ніколи — кидок стоїть вище в його переліку, — тож за сезон огляду біг у канати не
        /// траплявся жодного разу. Лише бот огляду за героя: кожен другий «кидок чи удар» міняє на «У канати» / «У кут» по
        /// черзі, якщо дія доступна. Правила матчу й ШІ суперника не змінюються.
        /// </summary>
        private bool ReviewRopes(MatchEngine eng)
        {
            if (eng.Phase != MatchPhase.AwaitAction) return false;
            var id = MatchAi.ChooseAction(eng);
            if (id != MatchActionId.Throw && id != MatchActionId.Strike) return false;
            int k = _reviewRopes++ % 4;
            var alt = k == 1 ? MatchActionId.IntoRopes : k == 3 ? MatchActionId.IntoCorner : id;
            if (alt == id || !eng.Describe(alt).Available) return false;
            eng.Perform(alt);
            return true;
        }

        private static bool BotTurn(MatchEngine e)
        {
            switch (e.Phase)
            {
                case MatchPhase.AwaitReaction: return e.PendingReaction.Defender == MatchSide.Player;
                case MatchPhase.AwaitBoost: return e.PendingRoll.Actor == MatchSide.Player;
                default: return e.CurrentActor == MatchSide.Player;
            }
        }
    }
}
