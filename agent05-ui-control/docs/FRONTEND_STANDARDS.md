# Требования к качеству фронтового кода (UI-проекты)

Для всех UI-проектов экосистемы (React + TypeScript, Vite и т.п.) должны быть подключены инструменты проверки качества и форматирования. Это обеспечивает единообразие и снижает количество ошибок.

## Обязательные инструменты

### 1. ESLint

- **Назначение:** статический анализ кода (синтаксис, типичные ошибки, стиль, лучшие практики React/TypeScript).
- **Требования:**
  - Конфигурация для TypeScript (`typescript-eslint`): парсинг и правила для TS.
  - Для React: плагины `eslint-plugin-react`, `eslint-plugin-react-hooks` (правила хуков).
  - Рекомендуется использовать рекомендованные пресеты (`recommended`) и при необходимости ужесточать правила.
  - В проекте должен быть скрипт `npm run lint` (или аналог), запускающий ESLint.
- **Файлы:** `eslint.config.js` (flat config, ESLint 9+) или `.eslintrc.cjs` / `.eslintrc.json` для старого формата.

### 2. Prettier

- **Назначение:** единообразное форматирование (отступы, кавычки, переносы строк, точка с запятой).
- **Требования:**
  - Отдельный конфиг (`.prettierrc` или `prettier.config.js`) с явными настройками по желанию (например, одинарные кавычки, 2 пробела, trailing comma).
  - Интеграция с ESLint: `eslint-config-prettier` отключает конфликтующие правила ESLint, чтобы Prettier был единственным источником правды по форматированию.
  - Скрипт `npm run format` (форматирование файлов) и при желании `format:check` (только проверка без записи).
- **Файлы:** `.prettierrc`, `.prettierignore` (например, `dist/`, `node_modules/`, сборки).

### 3. TypeScript

- **Назначение:** типизация и раннее обнаружение ошибок.
- **Требования:**
  - В `tsconfig.json` включён строгий режим: `"strict": true` (или как минимум `strictNullChecks`).
  - Сборка (`npm run build`) не должна проходить при ошибках типов (`tsc -b` или аналог в шаге build).

## Рекомендуемое

- **Проверка перед коммитом:** при желании — `lint-staged` + `husky` (pre-commit hook: запуск `lint` и `format` или `format --write` только для изменённых файлов). Не обязательно на старте, но желательно для будущих проектов.
- **CI:** в пайплайне запускать `npm run lint` и `npm run build` (и при наличии — `npm run format:check`), чтобы падать при нарушениях.

## Ссылки для новых проектов

- ESLint (flat config): https://eslint.org/docs/latest/use/configure/configuration-files-new
- typescript-eslint: https://typescript-eslint.io/
- Prettier: https://prettier.io/docs/en/configuration.html
- eslint-config-prettier: https://github.com/prettier/eslint-config-prettier

## Итоговый чек-лист для нового UI-проекта

1. Установить и настроить ESLint (TypeScript + React).
2. Установить Prettier и `eslint-config-prettier`.
3. Добавить в `package.json` скрипты: `"lint": "eslint ."`, `"format": "prettier --write ."`, при необходимости `"format:check": "prettier --check ."`.
4. Убедиться, что `npm run build` включает проверку типов (`tsc`).
5. Эталонная конфигурация при необходимости задаётся в плане или в каталоге UI первого сервиса в репозитории.
