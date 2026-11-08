using Game.Gameplay.Ring.Models;
using UnityEngine;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Ворота звуку для всієї гри (і «Рингу», і архівної Alpha): кожен програвач перед відтворенням питає
    /// <see cref="Allows"/>, а на старті, поки нічого не схвалено, слухач ще й вимкнений (<c>AudioListener.volume = 0</c>) —
    /// тиша навіть для коду, що обійшов ворота. Правило власника 08.11.2026 — <see cref="RingSoundApproval"/>;
    /// охоронець — <c>RingSoundApprovalTests</c> (кожен файл, що грає звук, ходить через ці ворота).
    /// </summary>
    public static class RingSoundGate
    {
        /// <summary>Шлях у Resources (без розширення): <c>Assets/Resources/Ring/sound_approvals.json</c>.</summary>
        public const string ResourcePath = "Ring/sound_approvals";

        private static RingSoundApproval _approval;
        private static readonly System.Collections.Generic.HashSet<string> Muted = new System.Collections.Generic.HashSet<string>();

        public static RingSoundApproval Approval
        {
            get
            {
                if (_approval == null) _approval = Load();
                return _approval;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            _approval = Load();
            Muted.Clear();
            AudioListener.volume = _approval.ListenerVolume;
            Debug.Log("[Звук] схвалено кліпів: " + _approval.Count + (_approval.Count == 0
                ? " — уся гра мовчить (рішення власника 08.11.2026)" : "; решта мовчить"));
        }

        private static RingSoundApproval Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            return asset != null ? RingSoundApproval.Parse(asset.text) : RingSoundApproval.None;
        }

        /// <summary>Кліп можна грати: він є і власник його схвалив. Перший відмовлений кліп кожної назви — рядок у лог.</summary>
        public static bool Allows(AudioClip clip)
        {
            if (clip == null) return false;
            if (Approval.Allows(clip.name)) return true;
            if (Muted.Add(clip.name)) Debug.Log("[Звук] не схвалено — мовчить: " + clip.name);
            return false;
        }
    }
}
