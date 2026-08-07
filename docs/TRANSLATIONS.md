# Community translations

Codex Account Switcher accepts community translations. All desktop UI text lives in one JSON file:

`desktop/src/renderer/src/locales/translations.json`

## Fork and pull request workflow

1. Sign in to GitHub and open `https://github.com/korsun009/codex-account-switcher`.
2. Press **Fork**, clone your fork, and create a branch such as `translation/uk`.
3. Open `desktop/src/renderer/src/locales/translations.json` in an editor that preserves UTF-8.
4. To improve an existing language, edit only its values. To add a language, copy the complete Russian object, give it a short locale key, and translate every string.
5. Add the new locale mapping in `desktop/src/renderer/src/locales/i18n.ts` and expose it in the language selector.
6. Run the checks below, commit, push to your fork, and open a pull request against `korsun009/codex-account-switcher:main`.

## Translation rules

- Keep every JSON key and nesting level aligned with the Russian `ru` object.
- Translate values, not keys.
- Preserve variables exactly: `{{name}}`, `{{version}}`, and other `{{placeholders}}` must remain present and spelled identically.
- Keep product names, paths, CLI flags, API routes, and file names such as `Codex`, `auth.json`, `--remote-api`, and `/switch-account` unchanged.
- Do not add tokens, personal accounts, private URLs, or machine-specific examples.
- Keep controls concise enough for the existing layout.
- Use natural language rather than literal machine translation.

## Validate

```powershell
Push-Location .\desktop
pnpm install --frozen-lockfile
pnpm typecheck
pnpm test
Pop-Location
```

The i18n tests compare language key trees and detect missing entries. In the pull request, name the language and locale, state whether a native speaker reviewed it, and include a screenshot if text length changes the layout.
