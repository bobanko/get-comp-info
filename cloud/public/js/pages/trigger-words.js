import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { formatDate } from '../logic.js';
import { checkWordRows, HIGH, LEVEL_LABELS, levelCounts, LOW, MEDIUM, normalizeTriggerWords, parseTriggerWords, wordsToText } from '../trigger-words-logic.js';

const LEVEL_BADGES = { [HIGH]: 'badge badge-danger', [MEDIUM]: 'badge badge-warn', [LOW]: 'badge badge-muted' };

// The first list of the table while the cloud has none: the list built into SUSpector (app\trigger-words.txt)
// as of 2026-11-24. Once saved, the cloud list is what the page shows.
const TEMPLATE = `[высокий]
таємно
особливої важливості
ДСК-ЕП
ДСК
для службового користування
гриф надав

[средний]
ТФД
БЧС
ЗВДТ
ООС
штатка
т. в. о.
МОСІ
телефонний довідник
телефонный справочник

[низкий]
штат
відомість
ведомость
вахтовка
доповідь
доклад
*.pfx
`;

const rowsOf = (text) => parseTriggerWords(text).words.map(({ text: word, level }) => ({ text: word, level }));

// Admin page: the trigger words of SUSpector's ДСК document check as a table «word — priority». SUSpector downloads
// the list before every scan, so a change works without a new release. The cloud keeps it as list text
// (the format of app\trigger-words.txt), which the program reads; the table is only its editor.
export async function renderTriggerWords(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка списка слов…')));
  let setting;
  let history;
  try {
    [{ setting }, { history }] = await Promise.all([api('/settings/trigger-words'), api('/settings/trigger-words/history')]);
  } catch (error) {
    replace(main, h('h1', {}, t('Суспектор: слова-триггеры')), errorBox(error, () => renderTriggerWords(context)));
    return;
  }

  const saved = setting ? wordsToText(rowsOf(setting.value.text)) : '';
  let rows = rowsOf(setting?.value.text ?? TEMPLATE);
  const body = h('tbody');
  const summary = h('div', { class: 'actions' });
  const problems = h('div');
  const dirtyNote = h('span', { class: 'muted small' });
  const saveButton = h('button', { class: 'button primary', type: 'submit' }, t('Сохранить'));

  function addRow(at, level) {
    rows.splice(at, 0, { text: '', level });
    drawRows();
    check();
    body.querySelectorAll('input')[at]?.focus();
  }

  function drawRows() {
    replace(
      body,
      rows.map((row, index) =>
        h(
          'tr',
          {},
          h('td', { class: 'muted number' }, String(index + 1)),
          h(
            'td',
            {},
            h('input', {
              class: 'word-input',
              value: row.text,
              maxlength: '200',
              spellcheck: 'false',
              placeholder: t('слово или фраза'),
              'aria-label': f('Слово {0}', index + 1),
              oninput: (event) => {
                row.text = event.target.value;
                check();
              },
              // Enter adds the next word of the same priority instead of sending the form.
              onkeydown: (event) => {
                if (event.key !== 'Enter') return;
                event.preventDefault();
                addRow(index + 1, row.level);
              },
            }),
          ),
          h(
            'td',
            {},
            h(
              'select',
              {
                class: `level-select level-${row.level}`,
                'aria-label': f('Приоритет слова {0}', index + 1),
                onchange: (event) => {
                  row.level = Number(event.target.value);
                  event.target.className = `level-select level-${row.level}`;
                  check();
                },
              },
              [HIGH, MEDIUM, LOW].map((level) => h('option', { value: String(level), selected: row.level === level }, `${level} — ${LEVEL_LABELS[level].toLowerCase()}`)),
            ),
          ),
          h(
            'td',
            { class: 'row-actions' },
            h(
              'button',
              {
                class: 'button small danger',
                type: 'button',
                title: t('Удалить слово'),
                'aria-label': f('Удалить слово {0}', index + 1),
                onclick: () => {
                  rows.splice(index, 1);
                  drawRows();
                  check();
                },
              },
              '✕',
            ),
          ),
        ),
      ),
    );
  }

  function check() {
    const { rows: marks, problems: errors } = checkWordRows(rows);
    [...body.rows].forEach((tr, index) => {
      const input = tr.querySelector('input');
      const mark = marks.get(index);
      tr.classList.toggle('invalid', Boolean(mark));
      input.title = mark ?? '';
      if (mark) input.setAttribute('aria-invalid', 'true');
      else input.removeAttribute('aria-invalid');
    });
    const words = rows.filter((row) => row.text.trim() !== '');
    replace(
      summary,
      h('span', { class: 'muted small' }, f('Слов: {0}', words.length)),
      levelCounts(words).map(({ level, label, count }) => h('span', { class: LEVEL_BADGES[level] }, `${label}: ${count}`)),
    );
    replace(problems, errors.length > 0 && h('div', { class: 'notice notice-error', role: 'alert' }, errors.map((text) => h('p', {}, text))));
    const dirty = wordsToText(rows) !== saved;
    dirtyNote.textContent = !setting ? t('Список ещё не сохранён') : dirty ? t('Есть несохранённые изменения') : '';
    saveButton.disabled = errors.length > 0 || (!dirty && Boolean(setting));
  }

  const form = h(
    'form',
    {
      onsubmit: async (event) => {
        event.preventDefault();
        saveButton.disabled = true;
        try {
          await api('/settings/trigger-words', { method: 'PUT', body: { baseRevision: setting?.revision ?? null, value: { text: normalizeTriggerWords(wordsToText(rows)) } } });
          toast(t('Слова сохранены. SUSpector возьмёт их при следующей проверке документов.'));
          await renderTriggerWords(context);
        } catch (error) {
          replace(
            problems,
            h(
              'div',
              { class: 'notice notice-error', role: 'alert' },
              h('p', {}, error.code === 'conflict' ? t('Список уже изменил другой администратор. Обновите страницу: ваши правки не сохранены.') : error.message),
            ),
          );
          saveButton.disabled = false;
        }
      },
    },
    h(
      'section',
      { class: 'panel' },
      h('div', { class: 'panel-head' }, h('h2', {}, t('Список')), summary),
      h(
        'div',
        { class: 'table-wrap' },
        h('table', { class: 'trigger-words' }, h('thead', {}, h('tr', {}, h('th', {}, '№'), h('th', {}, t('Слово или фраза')), h('th', {}, t('Приоритет')), h('th', {}))), body),
      ),
      h(
        'div',
        { class: 'actions' },
        h('button', { class: 'button', type: 'button', onclick: () => addRow(rows.length, MEDIUM) }, t('+ Добавить слово')),
        h('span', { class: 'muted small' }, t('Enter в строке добавляет следующее слово с тем же приоритетом.')),
      ),
      problems,
      h(
        'p',
        { class: 'muted small' },
        t('Регистр не важен. Пробел во фразе совпадает с любыми пробелами или их отсутствием: «т. в. о.» найдёт и «т.в.о.». '),
        t('Если одно слово входит в другое (ДСК и ДСК-ЕП, штат и штатка), засчитывается более длинное. '),
        t('Строка со * или ? без пробелов — маска имени файла: «*.pfx» (сертификаты VPN) находит такие файлы по имени, какого бы типа они ни были; внутри текста маска не ищется. '),
        t('Порядок строк внутри приоритета ни на что не влияет; после сохранения таблица упорядочивается по приоритету.'),
      ),
    ),
    h('div', { class: 'actions' }, h('span', { class: 'spacer' }), dirtyNote, saveButton),
  );

  replace(
    main,
    h('h1', {}, t('Суспектор: слова-триггеры')),
    h(
      'p',
      { class: 'lead' },
      t('Слова-триггеры проверки «Поиск ДСК-файлов». SUSpector скачивает список перед каждой проверкой документов, новая версия программы не нужна. '),
      t('Найденные файлы идут по важности: сначала со словами высокого приоритета, внутри него — по числу совпадений с учётом приоритета как веса (3, 2, 1). '),
      t('Без интернета программа берёт последний скачанный список, а если его нет — встроенный.'),
    ),
    setting
      ? h('p', { class: 'muted small', title: setting.updatedBy }, f('Ревизия {0}, изменено {1}, {2}', setting.revision, formatDate(setting.updatedAt), setting.updatedByName ?? setting.updatedBy))
      : h('div', { class: 'notice' }, h('p', {}, t('Список в облаке ещё не сохранён: SUSpector использует встроенный. Ниже он и есть — поправьте при необходимости и сохраните.'))),
    form,
    history.length > 0 &&
      h(
        'details',
        { class: 'panel muted small' },
        h('summary', {}, f('История ({0})', history.length)),
        h(
          'ul',
          {},
          history.map((entry) =>
            h(
              'li',
              { title: entry.changedBy },
              f('#{0} — {1}, {2}, слов: {3} ', entry.revision, formatDate(entry.changedAt), entry.changedByName ?? entry.changedBy, parseTriggerWords(entry.value.text).words.length),
              entry.revision !== setting?.revision &&
                h(
                  'button',
                  {
                    class: 'button small',
                    type: 'button',
                    onclick: () => {
                      rows = rowsOf(entry.value.text);
                      drawRows();
                      check();
                      window.scrollTo(0, 0);
                    },
                  },
                  t('В редактор'),
                ),
            ),
          ),
        ),
      ),
  );
  drawRows();
  check();
}
