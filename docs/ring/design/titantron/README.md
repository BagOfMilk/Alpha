# Екрани виходу борців — генератор (арт-директор «Рингу»)

Рецепт (гімік, роль, щабель арени, ім'я, seed) → ролик (шари з доріжками в бітах) → гра. ТЗ для програвача — `../briefs/B07_entrance_screen.md`.

| Файл | Що |
|---|---|
| `kits.json` | дані: слоти кольорів (`slots`, `playerSlots`, `monoSlots`), кроки прозорості, щаблі (fps, удар, бюджет шарів), BPM гіміків, шрифти, зразкові рецепти |
| `preview/` | контактні аркуші 12 комбінацій (arena v0, hall v1, gym v0) і GIF (монстр/лиходій/Палац, літун/герой/Палац, технік/лиходій/клуб, шоумен/бунтар/спортзал) |
| `../../../../tools/ring_design/titantron/` | `motifs.py` (спрайти) · `compose.py` (композитор) · `render.py` (кадри Pillow) · `bake.py` (пули, manifest, golden) · `preview.py` · `selfcheck.py` |
| `../../../../Assets/Art/Ring/Titantron/` | `sprites/*.png` (21 біла маска), `pools/*.json` (36 пулів), `manifest.json`, `golden.json` |

## Команди (з `tools/`)
```bash
python -m ring_design.titantron.motifs          # спрайти → Assets/Art/Ring/Titantron/sprites
python -m ring_design.titantron.bake            # пули, manifest, golden
python -m ring_design.titantron.selfcheck       # токени, золото, прозорість, бюджет, детермінізм, імена
python -m ring_design.titantron.preview sheet arena 0 sheet.png            # аркуш 12 комбінацій
python -m ring_design.titantron.preview film monster villain arena 0 hammer film.png   # розкадрування вступу
python -m ring_design.titantron.preview gif flyer hero arena 0 dzhura out.gif          # анімація
```

## Правила
- Колір — лише слот `$…` або токен; прозорість — лише `ghost/dim/mid`; золото — лише `$accent`/`$flash` гравця (охоронці: `selfcheck.py`, `TitantronAssetsGuardTests`).
- Слів у спрайтах немає; текст — слотами `nick/full/tag`, ВЕЛИКІ Sofia, у «спортзалі» підписи — Amatic.
- Новий архетип: запис у `kits.json` (`gimmicks`) + функція в `compose.py` (`BUILDERS`); після — `bake` і `selfcheck`. Програвач не змінюється.
- Числа ритму й розкладки — ПЛЕЙСХОЛДЕР до перегляду в грі.
