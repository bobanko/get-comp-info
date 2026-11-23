// The SUSpector trigger word list, as the admin writes it on the page «Слова SUSpector». The page and the Worker
// (src/trigger-words.ts) check it with this one module. The rules are those of app\src\TriggerWords.cs, which reads
// the list in the program: one word or phrase per line; [высокий], [средний], [низкий] (also Ukrainian and English
// names) start a level; # starts a comment; words before the first level are medium; a repeat (any case) counts once.

export const HIGH = 3;
export const MEDIUM = 2;
export const LOW = 1;

export const LEVEL_LABELS = { [HIGH]: 'Высокий', [MEDIUM]: 'Средний', [LOW]: 'Низкий' };

const LEVEL_NAMES = {
  высокий: HIGH,
  високий: HIGH,
  high: HIGH,
  средний: MEDIUM,
  середній: MEDIUM,
  medium: MEDIUM,
  низкий: LOW,
  низький: LOW,
  low: LOW,
};

export const MAX_WORDS = 300;
export const MAX_WORD_LENGTH = 100;
export const MAX_TEXT_LENGTH = 20000;

// { words: [{ text, level, line }], problems: [text], repeats: [text] }: problems stop saving, repeats do not
// (the program keeps the first one).
export function parseTriggerWords(text) {
  const source = String(text ?? '');
  const words = [];
  const problems = [];
  const repeats = [];
  const seen = new Map();
  let level = MEDIUM;
  if (source.length > MAX_TEXT_LENGTH) problems.push(`Список длиннее ${MAX_TEXT_LENGTH} символов.`);
  source.split(/\r?\n/).forEach((raw, index) => {
    const line = raw.trim();
    const where = `Строка ${index + 1}`;
    if (line === '' || line.startsWith('#')) return;
    if (line.startsWith('[') && line.endsWith(']')) {
      const named = LEVEL_NAMES[line.slice(1, -1).trim().toLowerCase()];
      if (named) level = named;
      else problems.push(`${where}: неизвестный уровень ${line}. Нужен [высокий], [средний] или [низкий].`);
      return;
    }
    const word = line.replace(/\s+/g, ' ');
    if (word.length > MAX_WORD_LENGTH) {
      problems.push(`${where}: слово или фраза длиннее ${MAX_WORD_LENGTH} символов.`);
      return;
    }
    const key = word.toLowerCase();
    if (seen.has(key)) {
      repeats.push(`${where}: «${word}» уже есть в строке ${seen.get(key)}, второй раз не учитывается.`);
      return;
    }
    seen.set(key, index + 1);
    words.push({ text: word, level, line: index + 1 });
  });
  if (words.length === 0) problems.push('В списке нет ни одного слова.');
  if (words.length > MAX_WORDS) problems.push(`Слов больше ${MAX_WORDS}.`);
  return { words, problems, repeats };
}

// "высокий 6 · средний 9 · низкий 6"
export function levelCounts(words) {
  return [HIGH, MEDIUM, LOW].map((level) => ({ level, label: LEVEL_LABELS[level], count: words.filter((w) => w.level === level).length }));
}

// The saved form: lines without spaces at the edges, \n line ends, one line end at the end.
export function normalizeTriggerWords(text) {
  return (
    String(text ?? '')
      .split(/\r?\n/)
      .map((line) => line.trim())
      .join('\n')
      .replace(/\n{3,}/g, '\n\n')
      .trim() + '\n'
  );
}
