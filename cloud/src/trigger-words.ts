// The trigger words of SUSpector's ДСК document check, kept in the settings table and edited by admins on the page
// «Слова SUSpector». SUSpector downloads them before every document scan (GET /api/suspector/trigger-words, public
// as the rest of /api/suspector), so a change needs no new release. Without a saved list the program uses its own.
import { normalizeTriggerWords, parseTriggerWords } from '../public/js/trigger-words-logic.js';
import { fail, isPlainObject } from './http';

export const TRIGGER_WORDS_KEY = 'suspector-trigger-words';

export interface TriggerWordsValue {
  text: string;
}

export function validateTriggerWords(value: unknown): TriggerWordsValue {
  if (!isPlainObject(value) || typeof value.text !== 'string') {
    fail(400, 'invalid_trigger_words', 'A JSON object with the text of the list is required.');
  }
  const text = normalizeTriggerWords(value.text);
  const { problems } = parseTriggerWords(text);
  if (problems.length > 0) fail(400, 'invalid_trigger_words', `The list has mistakes (${problems.length}): fix them on the page.`);
  return { text };
}
