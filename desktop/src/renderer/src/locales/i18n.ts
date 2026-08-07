import translations from './translations.json'

export type Language = keyof typeof translations

type Join<Prefix extends string, Key extends string> = Prefix extends '' ? Key : `${Prefix}.${Key}`
type LeafKeys<Value, Prefix extends string = ''> = Value extends string
  ? Prefix
  : { [Key in keyof Value & string]: LeafKeys<Value[Key], Join<Prefix, Key>> }[keyof Value & string]

export type TranslationKey = LeafKeys<(typeof translations)['ru']>
export type TranslationVariables = Record<string, string | number>
export type Translator = (key: TranslationKey, variables?: TranslationVariables) => string

export const localeByLanguage: Record<Language, string> = {
  ru: 'ru-RU',
  en: 'en-US',
  zh: 'zh-CN'
}

function readTranslation(language: Language, key: TranslationKey): string {
  const parts = key.split('.')
  let value: unknown = translations[language]
  for (const part of parts) {
    if (!value || typeof value !== 'object' || !(part in value)) {
      value = undefined
      break
    }
    value = (value as Record<string, unknown>)[part]
  }

  if (typeof value === 'string') return value
  if (language !== 'ru') return readTranslation('ru', key)
  return key
}

export function createTranslator(language: Language): Translator {
  return (key, variables = {}) => {
    const template = readTranslation(language, key)
    return template.replace(/\{\{(\w+)\}\}/g, (match, variable: string) => (
      Object.hasOwn(variables, variable) ? String(variables[variable]) : match
    ))
  }
}

export function formatDateTime(value: string, language: Language): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat(localeByLanguage[language], {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(date)
}
