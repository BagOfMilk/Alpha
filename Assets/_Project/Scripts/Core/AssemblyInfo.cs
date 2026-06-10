using System.Runtime.CompilerServices;

// Открываем internal-члены ядра тестовой сборке, чтобы тесты могли
// выставлять контролируемое состояние (напр. InjuryPoints) напрямую,
// не расширяя публичный API игры.
[assembly: InternalsVisibleTo("Game.Tests.EditMode")]
