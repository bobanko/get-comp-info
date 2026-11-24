// Interface language of the web admin, as L.T in comp-info.exe: Russian texts in the code are the keys and
// strings-uk.js holds their Ukrainian versions. The Russian interface keeps the Ukrainian official abbreviations
// (МВО, О/С, ПІБ), and «Майно» is the section name in both. Data (records, names, notes) is never translated.
// Covered by test/i18n.test.ts: every t()/f() text of public/js has a Ukrainian version.
import { UK } from './strings-uk.js';

export const LANGUAGES = { uk: 'Українська', ru: 'Русский' };

const STORAGE_KEY = 'lang';

// The first Ukrainian or Russian language of the browser; Ukrainian for any other. Outside a browser (the Worker,
// tests) Russian, the language of the keys.
export function systemLanguage(languages = typeof document === 'undefined' ? null : globalThis.navigator?.languages) {
  if (!Array.isArray(languages)) return 'ru';
  for (const tag of languages) {
    const code = String(tag).slice(0, 2).toLowerCase();
    if (code === 'uk' || code === 'ru') return code;
  }
  return 'uk';
}

// 'system', 'uk' or 'ru': what the person chose in the menu.
export function languageChoice() {
  try {
    const value = globalThis.localStorage?.getItem(STORAGE_KEY);
    return value && value in LANGUAGES ? value : 'system';
  } catch {
    return 'system';
  }
}

export function setLanguageChoice(choice) {
  try {
    if (choice in LANGUAGES) localStorage.setItem(STORAGE_KEY, choice);
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Blocked storage: the choice lasts until the page is reloaded.
  }
}

// The language in use; a change of the choice reloads the page, so texts built at start-up follow it.
export let LANG = languageChoice() === 'system' ? systemLanguage() : languageChoice();

// For tests.
export function setLanguage(code) {
  LANG = code;
}

// The text in the interface language; the Russian text when there is no translation.
export function t(ru) {
  if (LANG === 'ru') return ru;
  return UK[ru] ?? ru;
}

// A translated text with {0}, {1}… replaced: f('Показано {0} из {1}', 5, 10).
export function f(ru, ...args) {
  return t(ru).replace(/\{(\d+)\}/g, (_, index) => String(args[Number(index)] ?? ''));
}
