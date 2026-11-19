// Search that ignores the alphabet: "боб" finds "bob" and back, "мишко" finds "myshko" and "mishko".
// Pure helpers, covered by test/web-logic.test.ts.
//
// Text and query are both turned into a loose Latin key and compared there:
// 1. Cyrillic is transliterated by the official Ukrainian table (Cabinet of Ministers resolution No. 55 of
//    27.01.2010, as on dmsu.gov.ua), with the Russian letters ы, э, ё, ъ added;
// 2. spellings people mix up are folded into one: y/j/i → i, g/h → h, kh → h, ts → c, w → v,
//    and doubled letters → one (digits stay as they are, so IP addresses do not change).
// Every key character remembers the part of the original text it came from, to highlight the match there.

const CYRILLIC = {
  а: 'a', б: 'b', в: 'v', г: 'h', ґ: 'g', д: 'd', е: 'e', є: 'ie', ж: 'zh', з: 'z', и: 'y', і: 'i', ї: 'i', й: 'i',
  к: 'k', л: 'l', м: 'm', н: 'n', о: 'o', п: 'p', р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'kh', ц: 'ts',
  ч: 'ch', ш: 'sh', щ: 'shch', ь: '', ю: 'iu', я: 'ia',
  // Russian letters that Ukrainian has not.
  ы: 'y', э: 'e', ё: 'e', ъ: '',
  // Apostrophes are dropped, as in the official table.
  "'": '', '’': '', 'ʼ': '', '`': '',
};

// Applied in this order: longer spellings first.
const FOLDS = [
  ['shch', 'sch'],
  ['kh', 'h'],
  ['ts', 'c'],
  ['g', 'h'],
  ['j', 'i'],
  ['y', 'i'],
  ['w', 'v'],
];

const LETTER = /[a-z]/;

// Loose key of a text: { key, spans } where spans[i] = [start, end) of key[i] in the original text.
export function looseKey(text) {
  const source = String(text ?? '').toLowerCase();
  let items = [];
  for (let i = 0; i < source.length; i++) {
    const ch = source[i];
    const latin = Object.prototype.hasOwnProperty.call(CYRILLIC, ch) ? CYRILLIC[ch] : ch;
    for (const out of latin) items.push({ ch: out, start: i, end: i + 1 });
  }
  for (const [from, to] of FOLDS) items = fold(items, from, to);
  // Doubled letters: "kompp" = "komp", "дмитрий" (dmytrii) = "dmitri".
  const single = [];
  for (const item of items) {
    const last = single[single.length - 1];
    if (last && last.ch === item.ch && LETTER.test(item.ch)) last.end = item.end;
    else single.push({ ...item });
  }
  return { key: single.map((item) => item.ch).join(''), spans: single.map((item) => [item.start, item.end]) };
}

function fold(items, from, to) {
  const result = [];
  for (let i = 0; i < items.length; ) {
    let same = i + from.length <= items.length;
    for (let k = 0; same && k < from.length; k++) same = items[i + k].ch === from[k];
    if (!same) {
      result.push(items[i]);
      i++;
      continue;
    }
    const start = items[i].start;
    const end = items[i + from.length - 1].end;
    // The replacement covers the whole original part: its first character carries the span.
    [...to].forEach((ch, k) => result.push({ ch, start: k === 0 ? start : end, end }));
    i += from.length;
  }
  return result;
}

// Words of a search query, in lower case.
export function queryWords(query) {
  return String(query ?? '').toLowerCase().split(/\s+/).filter(Boolean);
}

// The word occurs in the text as typed or in another alphabet / spelling.
export function textMatches(text, word, key = looseKey(text).key) {
  if (String(text ?? '').toLowerCase().includes(word)) return true;
  const loose = looseKey(word).key;
  return loose !== '' && key.includes(loose);
}

// Parts of the text to highlight for these words: sorted, merged [start, end) pairs.
export function matchRanges(text, words) {
  const value = String(text ?? '');
  if (!value || words.length === 0) return [];
  const lower = value.toLowerCase();
  const { key, spans } = looseKey(value);
  const ranges = [];
  for (const word of words) {
    for (let at = lower.indexOf(word); at >= 0 && word; at = lower.indexOf(word, at + 1)) ranges.push([at, at + word.length]);
    const loose = looseKey(word).key;
    if (!loose) continue;
    for (let at = key.indexOf(loose); at >= 0; at = key.indexOf(loose, at + 1)) {
      ranges.push([spans[at][0], spans[at + loose.length - 1][1]]);
    }
  }
  ranges.sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  const merged = [];
  for (const range of ranges) {
    const last = merged[merged.length - 1];
    if (last && range[0] <= last[1]) last[1] = Math.max(last[1], range[1]);
    else merged.push([...range]);
  }
  return merged;
}

// The text split into plain and matched parts: [{ text, match }].
export function splitMatches(text, words) {
  const value = String(text ?? '');
  const parts = [];
  let at = 0;
  for (const [start, end] of matchRanges(value, words)) {
    if (start > at) parts.push({ text: value.slice(at, start), match: false });
    parts.push({ text: value.slice(start, end), match: true });
    at = end;
  }
  if (at < value.length) parts.push({ text: value.slice(at), match: false });
  return parts;
}
