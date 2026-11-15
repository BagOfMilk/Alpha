# Аудит кліпів «Рингу» без Blender і без Unity

Арт-директор, 14.11.2026. Власник: «Абослютно усе переглянь, не тіки фінішери, бо зараз це дуже погано».
Інструменти читають зібрані FBX кліпів (`Assets/Art/Animations/RING_Moves*.fbx`) просто з Python + numpy.
Blender-замок не потрібен. Звіт першого прогону — `docs/ring/design/ANIM_AUDIT_2026-11-14.md`.

| Файл | Що робить |
|---|---|
| `fbxanim.py` | Бінарний FBX 7.x → `npz`: світові положення й повороти 53 кісток, 30 к/с, усі кліпи. `bake <fbx> <out.npz>` |
| `audit_clips.py` | Мірки кожного кліпа: ривки, ковзання стоп, проникнення в себе й у партнера, мертві паузи, стик петлі, жвавість петель, відстань у мітках контакту → `<out>.md` і `.csv` |
| `touch.py` | Дотик тіл біля міток `contact/grab/sell`: найменший проміжок між будь-якими капсулами A і B у вікні ±0,12 с. `+` — не торкаються, `−` — заходять одне в одне |
| `render_clips.py` | Капсульні аркуші: пара A+B за зсувом маніфесту, види `side,q34,front,top,back34,side2`, кадри рівномірно або `--at` |
| `stack.py` | Склеює аркуші по N у стовпчик для перегляду |
| `fixoff.py` | Виправлений зсув пари для ударів: ударна частина A торкається цілі B на −2 см у мить `contact` (±0,04 с). Пише `bx`/`gap` генератора, кут «B лицем» і проникнення до удару. `--search` пробує ще й поворот B, `--write <файл>` пише копію маніфесту для `render_clips.py` (лише огляд, маніфест не чіпає) |
| `pairchk.py` | Де таз і голова A/B на кадрі 0 і в кінці (перевірка розміщення пари) |

## Як прогнати

```bash
python -I tools/ring_design/anim_audit/fbxanim.py bake Assets/Art/Animations/RING_Moves.fbx <тимчасова тека>/male.npz
python -I tools/ring_design/anim_audit/audit_clips.py <тимчасова тека>/male.npz Assets/Art/Animations/ring_anims_manifest.txt Assets/Art/Animations/ring_anims_events.txt <тимчасова тека>/audit_m
python -I -X utf8 tools/ring_design/anim_audit/touch.py <тимчасова тека>/male.npz Assets/Art/Animations/ring_anims_manifest.txt Assets/Art/Animations/ring_anims_events.txt
python -I tools/ring_design/anim_audit/render_clips.py <тимчасова тека>/male.npz Assets/Art/Animations/ring_anims_manifest.txt <тека аркушів> Center_Clothesline_A --at 0.5,0.6,0.68,0.76 --views side,q34,top
```

Для жіночого набору — `RING_Moves_F.fbx`, `ring_anims_f_manifest.txt`, `ring_anims_f_events.txt`.
`npz` (~33 МБ на набір) не комітити — це похідне.

## Межі

- Капсули грубі. Тулуб `spine_03` має радіус 11 см і покриває також плечі й шию. Тому «−10 см» у захватах шиї й зчепах ще не вада: дивитися аркуш зблизька.
- Хапання рука в руку (Whip_*) дає −6…−7 см — це норма, кисть у кисті.
- Розміщення пари перевірене: Whip_* хапають за −1 см, Armbar і LegLock — за ±2 см.
- Гра ставить пару тим самим зсувом маніфесту (`RingMatchDirector.RunPair`: `pA = pB − R(yawA)·Offset`). Отже, розрив у даних є розривом і в грі.
- Розміщення режисером, камера, світло — лише в кадрах білду.
