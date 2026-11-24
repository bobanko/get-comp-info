/// <reference types="vite/client" />
import { describe, expect, it } from 'vitest';
import { f, setLanguage, systemLanguage, t } from '../public/js/i18n.js';
import { UK } from '../public/js/strings-uk.js';

// The source of every web page module, as text.
const sources = import.meta.glob('../public/js/**/*.js', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

const KEY = /\b[tf]\(\s*'((?:[^'\\]|\\.)*)'/g;
const LITERAL = /'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)"|`((?:[^`\\]|\\.)*)`/g;
const CYRILLIC = /[А-Яа-яЁёІіЇїЄєҐґ]/;
// The JS source of a key back to the text t() gets.
const unescape = (text: string) => text.replace(/\\(.)/g, '$1');

// Cyrillic that is data, not interface text: Ukrainian abbreviations and names kept in every language,
// the level names of the trigger word file format, language names.
const NOT_TRANSLATED = new Set(['МВО', 'О/С', 'ПІБ', 'Комп’ютер', 'Прізвище Ім’я По батькові', 'высокий', 'средний', 'низкий', 'Українська', 'Русский']);
const DATA_MODULES = ['strings-uk.js', 'search-logic.js'];

function codeFiles() {
  return Object.entries(sources).filter(([path]) => !DATA_MODULES.some((name) => path.endsWith('/' + name)));
}

function usedKeys() {
  const keys = new Set<string>();
  for (const [, text] of codeFiles()) for (const match of text.matchAll(KEY)) keys.add(unescape(match[1]));
  return keys;
}

const placeholders = (text: string) => [...text.matchAll(/\{\d+\}/g)].map((m) => m[0]).sort().join(',');

describe('web interface texts', () => {
  it('finds the page modules', () => {
    expect(codeFiles().length).toBeGreaterThan(15);
  });

  it('have a Ukrainian version with the same placeholders', () => {
    const missing = [...usedKeys()].filter((key) => !(key in UK));
    expect(missing).toEqual([]);
    const broken = Object.entries(UK).filter(([ru, uk]) => placeholders(ru) !== placeholders(uk as string));
    expect(broken).toEqual([]);
  });

  it('keep no unused Ukrainian versions', () => {
    const used = usedKeys();
    expect(Object.keys(UK).filter((key) => !used.has(key))).toEqual([]);
  });

  it('are all passed through t() or f()', () => {
    const loose: string[] = [];
    for (const [path, text] of codeFiles()) {
      text.split(/\r?\n/).forEach((line, index) => {
        const trimmed = line.trim();
        if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) return;
        for (const match of line.replace(KEY, '').matchAll(LITERAL)) {
          const body = match[1] ?? match[2] ?? match[3];
          if (CYRILLIC.test(body) && !NOT_TRANSLATED.has(body)) loose.push(`${path}:${index + 1}: ${body}`);
        }
      });
    }
    expect(loose).toEqual([]);
  });
});

describe('interface language', () => {
  it('follows the browser: Ukrainian or Russian, Ukrainian for any other', () => {
    expect(systemLanguage(['ru-RU', 'uk'])).toBe('ru');
    expect(systemLanguage(['en-US', 'uk-UA'])).toBe('uk');
    expect(systemLanguage(['en-US', 'de'])).toBe('uk');
    expect(systemLanguage(null)).toBe('ru');
  });

  it('translates and fills placeholders', () => {
    try {
      setLanguage('uk');
      expect(t('Устройства')).toBe('Пристрої');
      expect(f('Показано {0} из {1}', 3, 10)).toBe('Показано 3 з 10');
      expect(t('текст без перевода')).toBe('текст без перевода');
      setLanguage('ru');
      expect(f('Показано {0} из {1}', 3, 10)).toBe('Показано 3 из 10');
    } finally {
      setLanguage('ru');
    }
  });
});
