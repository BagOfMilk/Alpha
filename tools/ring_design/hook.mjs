#!/usr/bin/env node
// Хук-нагадування Статуту вигляду «Рингу» (PreToolUse на Write|Edit|MultiEdit; НЕ блокує).
// Рішення власника 06.11.2026: хто створює видиме, спершу текстом до арт-директора (У0), потім low-poly (У1),
// потім кадри з білду (У2); кольори — лише з токенів. Хук раз на сесію додає це нагадування в контекст моделі
// (hookSpecificOutput.additionalContext), коли правиться видимий шлях. Сесії самого директора (гілка
// claude/game-design-system-df05bd) нагадування не отримують. Статут: docs/ring/DESIGN_SYSTEM.md.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const DIRECTOR_BRANCH = 'claude/game-design-system-df05bd';
const DIRECTOR_SESSION = 'local_d6c18264-ca16-4c92-bf0f-9335958f328c';
const VISIBLE = [
  /Assets\/_Project\/Scripts\/Gameplay\/Ring\/UI\//,
  /Assets\/_Project\/Scripts\/Gameplay\/Ring\/Stage\/Unity\/Ring(Fx|Tint|VenueDress|Stage3D|Show|Dress|Ropes)\.cs$/,
  /Assets\/Art\/Ring\//,
  /tools\/blender\/ring_[^/]+\.py$/,
];
const EXEMPT = [/RingTheme\.cs$/, /BrandTokens\.cs$/, /ring_brand\.py$/];

let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (c) => { raw += c; });
process.stdin.on('end', () => {
  try { main(JSON.parse(raw || '{}')); } catch { /* хук не має ламати роботу */ }
  process.exit(0);
});

function main(input) {
  const ti = input.tool_input || {};
  const file = String(ti.file_path || ti.path || '').replace(/\\/g, '/');
  if (!file || !VISIBLE.some((r) => r.test(file)) || EXEMPT.some((r) => r.test(file))) return;

  try {
    const branch = execFileSync('git', ['rev-parse', '--abbrev-ref', 'HEAD'],
      { cwd: input.cwd || process.cwd(), encoding: 'utf8', timeout: 3000, stdio: ['ignore', 'pipe', 'ignore'] }).trim();
    if (branch === DIRECTOR_BRANCH) return;
  } catch { /* не git — продовжуємо */ }

  const sid = String(input.session_id || 'nosession').replace(/[^A-Za-z0-9_-]/g, '');
  const flag = path.join(os.tmpdir(), 'ring_design_hook_' + sid + '.flag');
  if (fs.existsSync(flag)) return;
  try { fs.writeFileSync(flag, new Date().toISOString()); } catch { /* не критично */ }

  const msg =
    'Нагадування Статуту вигляду «Рингу» (docs/ring/DESIGN_SYSTEM.md). Ти правиш ВИДИМЕ: ' + file + '. ' +
    'Якщо це нова форма, колір, шрифт, елемент чи зміна розкладки — спершу текстом до арт-директора ' +
    '(SendMessage до сесії «Дизайн-система игры», ' + DIRECTOR_SESSION + ', крок У0), потім low-poly/чорновик (У1), ' +
    'потім кадри з білду (У2, чекліст Д1–Д6). Кольори — лише з RingTheme / docs/ring/design/brand_tokens.json: ' +
    'нових не вигадувати, а просити в директора. Дрібні правки без нових кольорів, форм і шрифтів У0 не потребують. ' +
    'Охоронець RingDesignGuardTests червоніє на нових літералах кольору, шрифту й дрібного кеглю (храповик). ' +
    'Це нагадування раз на сесію.';
  process.stdout.write(JSON.stringify({ hookSpecificOutput: { hookEventName: 'PreToolUse', additionalContext: msg } }));
}
