import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const translationsPath = resolve(process.cwd(), 'src/renderer/src/locales/translations.json')

function flattenKeys(value: unknown, prefix = ''): string[] {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return [prefix]
  return Object.entries(value).flatMap(([key, child]) => flattenKeys(child, prefix ? `${prefix}.${key}` : key))
}

describe('renderer translations', () => {
  it('keeps RU, EN and ZH in one JSON file with identical keys', () => {
    const translations = JSON.parse(readFileSync(translationsPath, 'utf8')) as Record<string, unknown>
    expect(Object.keys(translations)).toEqual(['ru', 'en', 'zh'])

    const russianKeys = flattenKeys(translations.ru).sort()
    expect(russianKeys.length).toBeGreaterThan(70)
    expect(flattenKeys(translations.en).sort()).toEqual(russianKeys)
    expect(flattenKeys(translations.zh).sort()).toEqual(russianKeys)
  })
})
