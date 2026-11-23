import { exports } from 'cloudflare:workers';
import { describe, expect, it } from 'vitest';
import { checkWordRows, levelCounts, normalizeTriggerWords, parseTriggerWords, wordsToText } from '../public/js/trigger-words-logic.js';
import { addUser, webRequest } from './helpers';

const LIST = '[высокий]\nДСК\nгриф надав\n\n[средний]\nБЧС\nт. в. о.\n\n[низкий]\nштат\n';

async function publicList() {
  const response = await exports.default.fetch(new Request('https://comp-info.test/api/suspector/trigger-words'));
  return { status: response.status, headers: response.headers, body: (await response.json()) as any };
}

async function currentRevision(admin: string): Promise<number | null> {
  return (await webRequest(admin, 'GET', '/settings/trigger-words')).body.setting?.revision ?? null;
}

describe('SUSpector trigger words', () => {
  it('are edited by admins and given to SUSpector without a key', async () => {
    const admin = await addUser('admin');
    const baseRevision = await currentRevision(admin);
    if (baseRevision === null) expect((await publicList()).status).toBe(404);

    const saved = await webRequest(admin, 'PUT', '/settings/trigger-words', { baseRevision, value: { text: '  [высокий]  \r\nДСК\r\n\r\n\r\n\r\nштат' } });
    expect(saved.status).toBe(200);
    expect(saved.body.setting).toMatchObject({ revision: (baseRevision ?? 0) + 1, updatedBy: admin, value: { text: '[высокий]\nДСК\n\nштат\n' } });

    const second = await webRequest(admin, 'PUT', '/settings/trigger-words', { baseRevision: saved.body.setting.revision, value: { text: LIST } });
    expect(second.status).toBe(200);

    const list = await publicList();
    expect(list.status).toBe(200);
    expect(list.headers.get('Cache-Control')).toBe('no-store');
    expect(list.body).toEqual({ revision: second.body.setting.revision, updatedAt: second.body.setting.updatedAt, text: LIST });

    const history = await webRequest(admin, 'GET', '/settings/trigger-words/history');
    expect(history.body.history[0]).toMatchObject({ revision: second.body.setting.revision, changedBy: admin, value: { text: LIST } });

    const journal = await webRequest(admin, 'GET', '/audit?category=programs');
    expect(journal.body.entries.some((e: any) => e.kind === 'settings' && e.target === 'suspector-trigger-words')).toBe(true);
  });

  it('are edited by admins only', async () => {
    for (const role of ['viewer', 'harvester', 'editor'] as const) {
      const user = await addUser(role);
      expect((await webRequest(user, 'GET', '/settings/trigger-words')).status).toBe(403);
      expect((await webRequest(user, 'PUT', '/settings/trigger-words', { baseRevision: null, value: { text: LIST } })).status).toBe(403);
    }
  });

  it('reject lists with mistakes and stale edits', async () => {
    const admin = await addUser('admin');
    const baseRevision = await currentRevision(admin);
    for (const text of ['# nothing', '[важный]\nДСК', 'x'.repeat(101)]) {
      const bad = await webRequest(admin, 'PUT', '/settings/trigger-words', { baseRevision, value: { text } });
      expect(bad.status).toBe(400);
      expect(bad.body.error.code).toBe('invalid_trigger_words');
    }
    expect((await webRequest(admin, 'PUT', '/settings/trigger-words', { baseRevision, value: 'ДСК' })).status).toBe(400);

    await webRequest(admin, 'PUT', '/settings/trigger-words', { baseRevision, value: { text: LIST } });
    const stale = await webRequest(admin, 'PUT', '/settings/trigger-words', { baseRevision, value: { text: 'ДСК' } });
    expect(stale.status).toBe(409);
    expect(stale.body.current.revision).toBe((baseRevision ?? 0) + 1);
  });
});

describe('trigger word list format', () => {
  it('reads levels, comments and repeats as SUSpector does', () => {
    const { words, problems, repeats } = parseTriggerWords('# comment\nДСК\n[Високий]\n таємно \nдск\n[low]\nт.  в. о.');
    expect(problems).toEqual([]);
    expect(words.map((w: any) => [w.text, w.level])).toEqual([
      ['ДСК', 2],
      ['таємно', 3],
      ['т. в. о.', 1],
    ]);
    expect(repeats).toEqual(['Строка 5: «дск» уже есть в строке 2, второй раз не учитывается.']);
    expect(levelCounts(words).map((l: any) => l.count)).toEqual([1, 1, 1]);
  });

  it('finds mistakes', () => {
    expect(parseTriggerWords('').problems).toEqual(['В списке нет ни одного слова.']);
    expect(parseTriggerWords('[срочно]\nДСК').problems[0]).toContain('Строка 1: неизвестный уровень [срочно]');
    expect(parseTriggerWords(Array.from({ length: 301 }, (_, i) => `слово${i}`).join('\n')).problems).toEqual(['Слов больше 300.']);
  });

  it('turns the table back into list text', () => {
    const rows = [
      { text: ' штат ', level: 1 },
      { text: 'ДСК', level: 3 },
      { text: '', level: 2 },
      { text: 'т.  в. о.', level: 2 },
      { text: 'гриф надав', level: 3 },
    ];
    const text = wordsToText(rows);
    expect(text).toBe('[высокий]\nДСК\nгриф надав\n\n[средний]\nт. в. о.\n\n[низкий]\nштат\n');
    expect(normalizeTriggerWords(text)).toBe(text);
    expect(parseTriggerWords(text).words.map((w: any) => [w.text, w.level])).toEqual([
      ['ДСК', 3],
      ['гриф надав', 3],
      ['т. в. о.', 2],
      ['штат', 1],
    ]);
    expect(wordsToText([{ text: 'БЧС', level: 2 }])).toBe('[средний]\nБЧС\n');
  });

  it('marks the table rows with mistakes', () => {
    const { rows, problems } = checkWordRows([
      { text: 'ДСК', level: 3 },
      { text: 'дск', level: 1 },
      { text: '# ДСК', level: 2 },
      { text: '[высокий]', level: 2 },
      { text: 'x'.repeat(101), level: 2 },
      { text: '  ', level: 2 },
    ]);
    expect([...rows.keys()]).toEqual([1, 2, 3, 4]);
    expect(rows.get(1)).toBe('Повтор: «дск» уже есть в строке 1.');
    expect(problems).toEqual(['Ошибок в строках: 4. Они отмечены в таблице.']);
    expect(checkWordRows([{ text: ' ', level: 2 }]).problems).toEqual(['В списке нет ни одного слова.']);
    expect(checkWordRows([{ text: 'ДСК', level: 3 }]).problems).toEqual([]);
  });

  it('saves the text tidy', () => {
    expect(normalizeTriggerWords('\r\n  [высокий] \r\nДСК  \r\n\r\n\r\n\r\nштат\r\n\r\n')).toBe('[высокий]\nДСК\n\nштат\n');
  });
});
