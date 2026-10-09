using System.Collections.Generic;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Characters;
using Game.Gameplay.UI;
using Game.Gameplay.Walk;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Діалог у світі (власник, 08.10.2026: «зробити діалоги між персонажами як в baldursgayte 3»; «Камера в світі,
    /// як у BG3»): камера села переходить на крупні перспективні плани того, хто говорить, співрозмовники
    /// повертаються одне до одного, мовець жестикулює. ЩО знімати і ЗВІДКИ — <see cref="DialogueDirector"/>
    /// (чистий C#, тести); тут лише постаті сцени й камера.
    ///
    /// Камерою села керує <see cref="HeroWalker"/> в Update; цей режисер перезаписує позу в LateUpdate (з
    /// <see cref="GameShell"/>), тож правок у HeroWalker не потрібно, а після сцени камера повертається рівно туди,
    /// де була. Між планами — склейки, як у BG3; у межах плану — повільний наїзд.
    /// Хто не стоїть поруч у селі (Тугар, ті, хто ще на посту далеко) — тимчасова постать набору навпроти героя.
    ///
    /// З розбору BG3 (08.10.2026, власник обрав усе): два варіанти крупного на людину (повернення до того самого — інший
    /// кадр), співрозмовники дивляться одне на одного головою (кістка голови доповертається після аніматора, до 50°),
    /// м'яке світло на обличчя в крупних планах з боку камери, «Як у селі» в меню паузи — без крупних планів.
    /// Статут PERF-01: без DoF; світло діалогу на Низькій графіці вимкнене.
    /// </summary>
    public sealed class DialogueStage : MonoBehaviour
    {
        /// <summary>Далі за цю відстань (одиниці світу) жителя не знімаємо там, де стоїть, — ставимо постать поруч.</summary>
        private const float NearEnough = 3f;
        private const float ShotPushSeconds = 7f;
        /// <summary>Найбільший поворот голови до співрозмовника, градуси; швидкість наростання ваги погляду.</summary>
        private const float LookMaxDegrees = 50f, LookBlendPerSecond = 3f;

        private GameShell _shell;
        private bool _active;
        private Camera _camera;

        // збережена камера
        private bool _savedOrtho;
        private float _savedSize, _savedFov, _savedNear;
        private Vector3 _savedPos;
        private Quaternion _savedRot;

        private HeroWalker _hero;
        private Quaternion _heroSavedRot;
        private string _anchorOtherId;

        private sealed class Actor
        {
            public Transform Root;
            public Transform Head;
            public KitFigure Figure;
            public bool Temporary;
            public Quaternion SavedRotation;
            public AnimationClip SavedIdle;
            /// <summary>
            /// Кадр, з якого поза постаті нова: створення тимчасової (бінд-поза, кістки розкидані) або зміна кліпу
            /// «стоїть» (сидячий устає). Голову з неї беремо, коли поза встановилась.
            /// </summary>
            public int SpawnFrame = -100;
            /// <summary>Зброя й щит, сховані на час розмови (повертаються в <see cref="End"/>).</summary>
            public List<Renderer> Sheathed;
        }

        /// <summary>
        /// Зброю на час розмови прибрано (власник, 08.10.2026: «Хоочу щоб Эквіп ніколи не був так, а нормально Не
        /// скрізь руку чи тіло»; як у BG3 — у розмові зброя в піхвах): жест «говорить» — порожніми руками, і спис чи
        /// палиця не прорізають пальці й тіло. Частини набору <c>wpn_*</c>, <c>shield_*</c> і тятива лука.
        /// </summary>
        private static void Sheathe(Actor actor)
        {
            if (actor?.Root == null || actor.Sheathed != null) return;
            actor.Sheathed = new List<Renderer>();
            foreach (var r in actor.Root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                string n = r.gameObject.name;
                if (n.EndsWith("_stowed", System.StringComparison.OrdinalIgnoreCase)) continue; // за спиною — лишається (трек V)
                bool gear = n.IndexOf("wpn_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("shield_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("buckler", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                            r is LineRenderer;
                if (!gear) continue;
                r.enabled = false;
                actor.Sheathed.Add(r);
            }
        }

        private static void Unsheathe(Actor actor)
        {
            if (actor?.Sheathed == null) return;
            foreach (var r in actor.Sheathed) if (r != null) r.enabled = true;
            actor.Sheathed = null;
        }

        /// <summary>Свіжа тимчасова постать ще не отримала анімовану позу — голову з неї брати рано.</summary>
        private static bool NotSettled(Actor a) => a != null && Time.frameCount - a.SpawnFrame < 4;

        /// <summary>Перший план уже знято — до того камеру села не чіпаємо (немає валідної пози).</summary>
        private bool _hasPose;

        private readonly Dictionary<string, Actor> _actors = new Dictionary<string, Actor>();
        /// <summary>Скільки разів крупний план уже був на цій людині — варіант крупного чергується.</summary>
        private readonly Dictionary<string, int> _closeCount = new Dictionary<string, int>();
        private DialogueShot _shot;
        private Light _keyLight;
        private float _lookWeight;
        private string _shotSignature;
        private DialogueCameraPose _pose;
        private float _shotAge;
        private string _talkingId;

        public bool IsActive => _active;

        public void Init(GameShell shell) => _shell = shell;

        /// <summary>Раз на кадр (з LateUpdate оболонки): сцена йде — тримати кадр; ні — віддати камеру.</summary>
        public void Tick(SceneStepView current, string displaySpeakerId)
        {
            if (current == null || _shell == null || _shell.Session == null)
            {
                End();
                return;
            }
            if (!DialogueCameraSetting.CloseUps)
            {
                End(); // «Як у селі»: камера лишається на загальному плані, вікно діалогу те саме
                return;
            }
            if (!_active && !Begin()) return;

            // Спершу жест мовцю (сидячий на віче встає), і лише коли поза встановилась — план: інакше камера
            // рахує голову сидячого, а знімає вже стоячого (тур 08.10.2026, крупний на Захара — груди).
            if (!string.IsNullOrEmpty(displaySpeakerId)) ActorFor(displaySpeakerId);
            SetTalking(displaySpeakerId);

            var shot = DialogueDirector.Plan(current.Framing, current.ActorId, current.SecondActorId, current.SpeakerId,
                GameSession.ProtagonistId);
            if (shot != null && shot.Signature != _shotSignature) Cut(shot);
            LookAtEachOther(displaySpeakerId);

            _shotAge += Time.unscaledDeltaTime;
            if (_camera == null || !_hasPose) return;
            var pos = DialogueDirector.PushIn(_pose, _shotAge / ShotPushSeconds);
            _camera.orthographic = false;
            _camera.fieldOfView = _pose.Fov;
            _camera.nearClipPlane = 0.02f;
            _camera.transform.position = ToV(pos);
            _camera.transform.rotation = Quaternion.LookRotation(ToV(_pose.LookAt) - ToV(pos), Vector3.up);
        }

        private bool Begin()
        {
            _hero = FindAnyObjectByType<HeroWalker>();
            _camera = _hero != null && _hero.hubCamera != null ? _hero.hubCamera : Camera.main;
            if (_camera == null || !_camera.isActiveAndEnabled || _hero == null) return false;
            _savedOrtho = _camera.orthographic;
            _savedSize = _camera.orthographicSize;
            _savedFov = _camera.fieldOfView;
            _savedNear = _camera.nearClipPlane;
            _savedPos = _camera.transform.position;
            _savedRot = _camera.transform.rotation;
            _heroSavedRot = _hero.transform.rotation;
            _shotSignature = null;
            _anchorOtherId = null;
            _active = true;
            return true;
        }

        /// <summary>Сцена скінчилась: камера, постаті, пози — як були.</summary>
        public void End()
        {
            if (!_active) return;
            _active = false;
            SetTalking(null);
            foreach (var pair in _actors)
            {
                var a = pair.Value;
                if (a.Root == null) continue;
                Unsheathe(a);
                RestoreIdle(a); // хто сидів на віче — сідає назад
                if (a.Temporary) Destroy(a.Root.gameObject);
                else if (a.Root != (_hero != null ? _hero.transform : null)) a.Root.rotation = a.SavedRotation;
            }
            _actors.Clear();
            _closeCount.Clear();
            _shot = null;
            _hasPose = false;
            _lookWeight = 0f;
            if (_keyLight != null) _keyLight.enabled = false;
            if (_hero != null) _hero.transform.rotation = _heroSavedRot;
            if (_camera != null)
            {
                _camera.orthographic = _savedOrtho;
                _camera.orthographicSize = _savedSize;
                _camera.fieldOfView = _savedFov;
                _camera.nearClipPlane = _savedNear;
                _camera.transform.position = _savedPos;
                _camera.transform.rotation = _savedRot;
            }
            _shotSignature = null;
        }

        private void OnDestroy() => End();

        // ============================ кадр ============================

        private void Cut(DialogueShot shot)
        {
            var subject = ActorFor(shot.SubjectId);
            if (subject == null) return; // нікого показати — лишаємо попередній план
            string otherId = shot.OtherId;
            if (string.IsNullOrEmpty(otherId) && shot.SubjectId == GameSession.ProtagonistId) otherId = _anchorOtherId;
            var other = !string.IsNullOrEmpty(otherId) ? ActorFor(otherId) : null;
            if (shot.SubjectId != GameSession.ProtagonistId && _anchorOtherId == null) _anchorOtherId = shot.SubjectId;
            if (!string.IsNullOrEmpty(shot.OtherId) && shot.OtherId != GameSession.ProtagonistId && _anchorOtherId == null) _anchorOtherId = shot.OtherId;

            var heroActor = ActorFor(GameSession.ProtagonistId);
            var anchorOther = _anchorOtherId != null ? ActorFor(_anchorOtherId) : null;

            Vector3 subjectHead = HeadOf(subject);
            Vector3 otherHead = other != null ? HeadOf(other) : subjectHead + subject.Root.forward * 0.6f;
            Vector3 axisFrom = heroActor != null ? HeadOf(heroActor) : subjectHead;
            Vector3 axisTo = anchorOther != null ? HeadOf(anchorOther) : axisFrom + (_hero != null ? _hero.transform.forward : Vector3.forward);

            // Співрозмовники дивляться одне на одного.
            if (other != null)
            {
                Face(subject, otherHead);
                Face(other, subjectHead);
            }

            float height = FigureHeight(subject);
            int variant = 0;
            if (shot.Kind == DialogueShotKind.Close)
            {
                _closeCount.TryGetValue(shot.SubjectId, out variant);
                _closeCount[shot.SubjectId] = variant + 1;
            }
            _pose = DialogueDirector.Pose(shot.Kind, ToD(subjectHead), ToD(otherHead), ToD(axisFrom), ToD(axisTo), height, variant);
            _shot = shot;
            _hasPose = true;
            _shotSignature = shot.Signature;
            _shotAge = 0f;
            PlaceKeyLight(shot.Kind, subjectHead, height);
        }

        /// <summary>
        /// М'яке світло на обличчя в крупних планах (розбір BG3: у розмови своє світло, що йде з боку камери). Одне
        /// точкове світло на всю сцену; на Низькій графіці вимкнене (Статут PERF-01).
        /// </summary>
        private void PlaceKeyLight(DialogueShotKind kind, Vector3 subjectHead, float height)
        {
            bool want = !GraphicsTier.IsLow &&
                        (kind == DialogueShotKind.Close || kind == DialogueShotKind.OverShoulder);
            if (!want)
            {
                if (_keyLight != null) _keyLight.enabled = false;
                return;
            }
            if (_keyLight == null)
            {
                var go = new GameObject("DialogueKeyLight");
                go.transform.SetParent(transform, false);
                _keyLight = go.AddComponent<Light>();
                _keyLight.type = LightType.Spot;
                _keyLight.color = new Color(1f, 0.92f, 0.82f);
                _keyLight.shadows = LightShadows.None;
            }
            var cam = ToV(_pose.Position);
            var toCam = cam - subjectHead;
            toCam.y = 0f;
            var side = Vector3.Cross(Vector3.up, toCam.normalized);
            var pos = subjectHead + toCam.normalized * (0.9f * height) + side * (0.35f * height) + Vector3.up * (0.25f * height);
            _keyLight.transform.position = pos;
            _keyLight.transform.rotation = Quaternion.LookRotation(subjectHead - pos, Vector3.up);
            _keyLight.range = 2.5f * height;
            _keyLight.spotAngle = 50f;
            _keyLight.intensity = 1.4f;
            _keyLight.enabled = true;
        }

        /// <summary>
        /// Погляд головою (розбір BG3: співрозмовники автоматично дивляться одне на одного): після аніматора доповернути
        /// кістку голови (і трохи шию) до голови співрозмовника, не більше <see cref="LookMaxDegrees"/>. Аніматор щокадру
        /// пише позу заново, тож поворот не накопичується; без анімації (статична постать) не чіпаємо.
        /// </summary>
        private void LookAtEachOther(string speakerId)
        {
            _lookWeight = Mathf.MoveTowards(_lookWeight, 1f, Time.unscaledDeltaTime * LookBlendPerSecond);
            if (string.IsNullOrEmpty(speakerId) || !_actors.TryGetValue(speakerId, out var speaker) || speaker.Root == null) return;
            string listenerId = _shot != null && _shot.SubjectId == speakerId ? _shot.OtherId : _shot?.SubjectId;
            if (string.IsNullOrEmpty(listenerId) || listenerId == speakerId)
                listenerId = speakerId == GameSession.ProtagonistId ? _anchorOtherId : GameSession.ProtagonistId;
            Vector3 speakerHead = HeadOf(speaker);
            foreach (var pair in _actors)
            {
                var a = pair.Value;
                if (a.Root == null) continue;
                Vector3 target;
                if (pair.Key == speakerId)
                {
                    if (listenerId == null || !_actors.TryGetValue(listenerId, out var listener) || listener.Root == null) continue;
                    target = HeadOf(listener);
                }
                else target = speakerHead;
                TurnHead(a, target, _lookWeight);
            }
        }

        private static void TurnHead(Actor actor, Vector3 target, float weight)
        {
            if (actor.Head == null || actor.Figure == null || actor.Figure.Model == null || actor.Figure.Animation == null) return;
            var facing = CharacterAssembler.Facing(actor.Figure.Model);
            facing.y = 0f;
            var toTarget = target - actor.Head.position;
            if (facing.sqrMagnitude < 1e-6f || toTarget.sqrMagnitude < 1e-6f) return;
            var delta = Quaternion.FromToRotation(facing.normalized, toTarget.normalized);
            delta = Quaternion.RotateTowards(Quaternion.identity, delta, LookMaxDegrees);
            var neck = actor.Head.parent;
            if (neck != null) neck.rotation = Quaternion.Slerp(Quaternion.identity, delta, 0.3f * weight) * neck.rotation;
            actor.Head.rotation = Quaternion.Slerp(Quaternion.identity, delta, 0.7f * weight) * actor.Head.rotation;
        }

        private void Face(Actor actor, Vector3 point)
        {
            if (actor?.Root == null) return;
            var dir = point - actor.Root.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            // Корінь героя повернутий на modelYawOffset під старі фігурки (див. HeroWalker.Face) — та сама поправка.
            if (_hero != null && actor.Root == _hero.transform) look *= Quaternion.Euler(0f, _hero.modelYawOffset, 0f);
            actor.Root.rotation = look;
        }

        /// <summary>Жест мовцю (Talk), спокій решті; після сцени — те, що було.</summary>
        private void SetTalking(string speakerId)
        {
            if (speakerId == _talkingId) return;
            if (_talkingId != null && _actors.TryGetValue(_talkingId, out var was)) StandStill(was);
            _talkingId = speakerId;
            if (speakerId == null || !_actors.TryGetValue(speakerId, out var now)) return;
            var anim = now.Figure != null ? now.Figure.Animation : null;
            if (anim == null) return;
            KitFigure.TryFindLibraries(out _, out var anims);
            var talk = anims != null && anims.IsComplete ? anims.For(CharacterAnimState.Talk, WeaponStyle.Unarmed) : null;
            if (talk == null) return;
            if (now.SavedIdle == null) now.SavedIdle = anim.idle;
            if (anim.idle != talk) now.SpawnFrame = Time.frameCount; // нова поза — кістки голови довіряємо за кілька кадрів
            anim.SetIdleClip(talk);
        }

        /// <summary>
        /// У розмові всі стоять (як у BG3): той, хто сидів на віче чи працював на посту, встає ще до першого плану —
        /// інакше камера рахувала голову сидячого, а знімала стоячого (тур 08.10.2026, Захар — груди).
        /// </summary>
        private static void StandStill(Actor actor)
        {
            var anim = actor?.Figure != null ? actor.Figure.Animation : null;
            if (anim == null) return;
            KitFigure.TryFindLibraries(out _, out var anims);
            var stand = anims != null && anims.IsComplete ? anims.For(CharacterAnimState.Idle, WeaponStyle.Unarmed) : null;
            if (stand == null || anim.idle == stand) return;
            if (actor.SavedIdle == null) actor.SavedIdle = anim.idle;
            anim.SetIdleClip(stand);
            actor.SpawnFrame = Time.frameCount;
        }

        private static void RestoreIdle(Actor actor)
        {
            var anim = actor?.Figure != null ? actor.Figure.Animation : null;
            if (anim != null && actor.SavedIdle != null) anim.SetIdleClip(actor.SavedIdle);
        }

        // ============================ хто де стоїть ============================

        private Actor ActorFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_actors.TryGetValue(id, out var known) && known.Root != null) return known;

            Actor actor = null;
            if (id == GameSession.ProtagonistId)
            {
                if (_hero == null) return null;
                actor = new Actor { Root = _hero.transform, Figure = _hero.GetComponentInChildren<KitFigure>(), SavedRotation = _hero.transform.rotation };
            }
            else
            {
                var holder = VillageHolderOf(id);
                if (holder != null && _hero != null && Vector3.Distance(holder.position, _hero.transform.position) <= NearEnough)
                    actor = new Actor { Root = holder, Figure = holder.GetComponent<KitFigure>(), SavedRotation = holder.rotation };
                else
                    actor = SpawnBeside(id);
            }
            if (actor == null) return null;
            actor.Head = FindHead(actor.Root);
            Sheathe(actor);
            if (!actor.Temporary && id != GameSession.ProtagonistId) StandStill(actor);
            if (actor.SavedIdle == null && actor.Figure != null && actor.Figure.Animation != null) actor.SavedIdle = actor.Figure.Animation.idle;
            _actors[id] = actor;
            return actor;
        }

        /// <summary>
        /// Постать жителя в селі — та сама розстановка, що будує сцену (<see cref="VillagePeople.Arrange"/>): на посту —
        /// <c>villager:&lt;пост&gt;</c>, без поста — <c>idle:&lt;місце&gt;</c>. null — у селі його не видно.
        /// </summary>
        private Transform VillageHolderOf(string id)
        {
            var roster = _shell.Session.GetRosterView();
            if (roster?.Companions == null) return null;
            var posts = new Dictionary<string, WalkPoint>();
            foreach (var c in roster.Companions)
                if (!string.IsNullOrEmpty(c.AssignedSlotId) && Active("villager:" + c.AssignedSlotId) != null)
                    posts[c.AssignedSlotId] = default(WalkPoint);
            var idle = new List<WalkPoint>();
            for (int i = 0; i < VillagePeople.IdleSpotCount; i++) idle.Add(default(WalkPoint));
            foreach (var spot in VillagePeople.Arrange(roster, posts, idle))
            {
                if (spot.CompanionId != id) continue;
                if (spot.AtPost)
                {
                    foreach (var c in roster.Companions)
                        if (c.Id == id) return Active("villager:" + c.AssignedSlotId);
                    return null;
                }
                return Active("idle:" + spot.IdleIndex);
            }
            return null;
        }

        private static Transform Active(string name)
        {
            var go = GameObject.Find(name);
            return go != null && go.activeInHierarchy ? go.transform : null;
        }

        /// <summary>Тимчасова постать набору навпроти героя (на розмовній відстані), обличчям до нього.</summary>
        private Actor SpawnBeside(string id)
        {
            if (_hero == null) return null;
            var look = _shell.Session.GetAppearance(id);
            if (look == null) return null;
            int index = 0;
            foreach (var a in _actors.Values) if (a.Temporary) index++;
            var heroPos = _hero.transform.position;
            var forward = _hero.transform.rotation * Quaternion.Euler(0f, -_hero.modelYawOffset, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            float spread = index == 0 ? 0f : (index % 2 == 1 ? 35f : -35f) * ((index + 1) / 2);
            var dir = Quaternion.Euler(0f, spread, 0f) * forward;
            var go = new GameObject("dialogue:" + id);
            go.layer = _hero.gameObject.layer;
            go.transform.position = heroPos + dir * 0.6f;
            go.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            var figure = go.AddComponent<KitFigure>();
            if (!figure.Show(look, new List<string>(), CharacterAnimState.Idle, 1f, 0.37f * (index + 1), go.layer))
            {
                Destroy(go);
                return null;
            }
            return new Actor { Root = go.transform, Figure = figure, Temporary = true, SavedRotation = go.transform.rotation, SpawnFrame = Time.frameCount };
        }

        private static Transform FindHead(Transform root)
        {
            if (root == null) return null;
            // Гуманоїдна кістка голови з аватара — надійно (за ім'ям «head» трапляються чужі вузли: шапка, древко).
            var animator = root.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var bone = animator.GetBoneTransform(HumanBodyBones.Head);
                if (bone != null) return bone;
            }
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.Equals("head", System.StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        /// <summary>
        /// Голова в світі. Поки поза не встановилась (свіжа постать у бінд-позі, щойно встав), кістка бреше — тоді
        /// голова від кореня за стандартним зростом: план ріжеться одразу, без очікування й без стрибка.
        /// </summary>
        private static Vector3 HeadOf(Actor actor)
        {
            if (actor.Head != null && !NotSettled(actor)) return actor.Head.position;
            return actor.Root.position + Vector3.up * (0.93f * StandardHeight);
        }

        private static float StandardHeight => 1.75f * Game.Gameplay.Visual.ArtScale.World;

        /// <summary>Зріст постаті в одиницях світу (масштаб сцени × людський зріст ≈ 1,75 м).</summary>
        private static float FigureHeight(Actor actor)
        {
            if (actor?.Head != null && actor.Root != null && !NotSettled(actor))
            {
                float h = (actor.Head.position.y - actor.Root.position.y) / 0.93f;
                if (h > 0.05f) return h;
            }
            return StandardHeight;
        }

        private static DialogueVec ToD(Vector3 v) => new DialogueVec(v.x, v.y, v.z);
        private static Vector3 ToV(DialogueVec v) => new Vector3(v.X, v.Y, v.Z);
    }
}
