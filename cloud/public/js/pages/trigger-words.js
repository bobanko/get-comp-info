import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate } from '../logic.js';
import { HIGH, levelCounts, LOW, MEDIUM, normalizeTriggerWords, parseTriggerWords } from '../trigger-words-logic.js';

const LEVEL_BADGES = { [HIGH]: 'badge badge-danger', [MEDIUM]: 'badge badge-warn', [LOW]: 'badge badge-muted' };

const TEMPLATE = `[высокий]
ДСК

[средний]
ТФД

[низкий]
відомість
`;

// Admin page: the trigger words of SUSpector's ДСК document check. SUSpector downloads the list before every scan,
// so a change works without a new release. The list is plain text, as app\trigger-words.txt.
export async function renderTriggerWords(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка списка слов…'));
  let setting;
  let history;
  try {
    [{ setting }, { history }] = await Promise.all([api('/settings/trigger-words'), api('/settings/trigger-words/history')]);
  } catch (error) {
    replace(main, h('h1', {}, 'Слова SUSpector'), errorBox(error, () => renderTriggerWords(context)));
    return;
  }

  const saved = setting?.value.text ?? '';
  const editor = h('textarea', {
    class: 'mono',
    rows: '24',
    spellcheck: 'false',
    'aria-label': 'Слова-триггеры',
    value: saved || TEMPLATE,
    oninput: () => check(),
  });
  const summary = h('div', { class: 'actions' });
  const problems = h('div');
  const dirtyNote = h('span', { class: 'muted small' });
  const saveButton = h('button', { class: 'button primary', type: 'submit' }, 'Сохранить');

  function check() {
    const { words, problems: errors, repeats } = parseTriggerWords(editor.value);
    replace(
      summary,
      h('span', { class: 'muted small' }, `Слов: ${words.length}`),
      levelCounts(words).map(({ level, label, count }) => h('span', { class: LEVEL_BADGES[level] }, `${label}: ${count}`)),
    );
    replace(
      problems,
      errors.length > 0 && h('div', { class: 'notice notice-error', role: 'alert' }, errors.map((text) => h('p', {}, text))),
      repeats.length > 0 && h('div', { class: 'notice' }, repeats.map((text) => h('p', {}, text))),
    );
    const dirty = normalizeTriggerWords(editor.value) !== normalizeTriggerWords(saved);
    dirtyNote.textContent = dirty || !setting ? (setting ? 'Есть несохранённые изменения' : 'Список ещё не сохранён') : '';
    saveButton.disabled = errors.length > 0 || (!dirty && Boolean(setting));
  }

  const form = h(
    'form',
    {
      onsubmit: async (event) => {
        event.preventDefault();
        saveButton.disabled = true;
        try {
          await api('/settings/trigger-words', { method: 'PUT', body: { baseRevision: setting?.revision ?? null, value: { text: editor.value } } });
          toast('Слова сохранены. SUSpector возьмёт их при следующей проверке документов.');
          await renderTriggerWords(context);
        } catch (error) {
          replace(
            problems,
            h(
              'div',
              { class: 'notice notice-error', role: 'alert' },
              h('p', {}, error.code === 'conflict' ? 'Список уже изменил другой администратор. Обновите страницу: ваши правки не сохранены.' : error.message),
            ),
          );
          saveButton.disabled = false;
        }
      },
    },
    h(
      'section',
      { class: 'panel' },
      h('div', { class: 'panel-head' }, h('h2', {}, 'Список'), summary),
      editor,
      problems,
      h(
        'p',
        { class: 'muted small' },
        'Одно слово или фраза в строке, регистр не важен. [высокий], [средний] и [низкий] начинают уровень важности. ',
        'Строки с # — комментарии. Пробел во фразе совпадает с любыми пробелами или их отсутствием: «т. в. о.» найдёт и «т.в.о.». ',
        'Если одно слово входит в другое (ДСК и ДСК-ЕП, штат и штатка), засчитывается более длинное.',
      ),
    ),
    h('div', { class: 'actions' }, h('span', { class: 'spacer' }), dirtyNote, saveButton),
  );

  replace(
    main,
    h('h1', {}, 'Слова SUSpector'),
    h(
      'p',
      { class: 'lead' },
      'Слова-триггеры проверки «Поиск ДСК-файлов». SUSpector скачивает список перед каждой проверкой документов, новая версия программы не нужна. ',
      'Найденные файлы идут по важности: сначала со словами высокого уровня, внутри уровня — по числу совпадений с учётом веса (высокий 3, средний 2, низкий 1). ',
      'Без интернета программа берёт последний скачанный список, а если его нет — встроенный.',
    ),
    setting
      ? h('p', { class: 'muted small', title: setting.updatedBy }, `Ревизия ${setting.revision}, изменено ${formatDate(setting.updatedAt)}, ${setting.updatedByName ?? setting.updatedBy}`)
      : h('div', { class: 'notice' }, h('p', {}, 'Список в облаке ещё не сохранён: SUSpector использует встроенный. Впишите слова и сохраните.')),
    form,
    history.length > 0 &&
      h(
        'details',
        { class: 'panel muted small' },
        h('summary', {}, `История (${history.length})`),
        h(
          'ul',
          {},
          history.map((entry) =>
            h(
              'li',
              { title: entry.changedBy },
              `#${entry.revision} — ${formatDate(entry.changedAt)}, ${entry.changedByName ?? entry.changedBy}, слов: ${parseTriggerWords(entry.value.text).words.length} `,
              entry.revision !== setting?.revision &&
                h(
                  'button',
                  {
                    class: 'button small',
                    type: 'button',
                    onclick: () => {
                      editor.value = entry.value.text;
                      check();
                      editor.focus();
                      window.scrollTo(0, 0);
                    },
                  },
                  'В редактор',
                ),
            ),
          ),
        ),
      ),
  );
  check();
}
