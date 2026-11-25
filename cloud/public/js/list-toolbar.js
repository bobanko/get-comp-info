// The search row of the list pages (devices, property, people). Wide screens: search and filters in one row.
// Phones: only the search and a «Фильтры» button that folds the rest; the row sits at the bottom of the screen
// within reach of the thumb (the default) or sticks to the top. app.css: .list-toolbar.
import { h, readSetting, replace, writeSetting } from './dom.js';
import { t } from './i18n.js';
import { splitMatches } from './search-logic.js';

// One setting for every list: where the search row of phones is.
const BOTTOM_SETTING = 'devices.searchBottom';

/**
 * @param {{ name: string, placeholder: string, query: string, onQuery: (query: string) => void,
 *   extra?: any[], activeFilters?: () => number }} options
 * name: the field name (a neutral one keeps browsers from offering logins or addresses);
 * extra: controls folded behind «Фильтры» on phones; activeFilters: how many of them filter now;
 * actions: buttons that stay next to the search on phones too (+ Добавить).
 */
export function listToolbar({ name, placeholder, query, onQuery, extra = [], activeFilters = () => 0, actions = [] }) {
  const clear = h(
    'button',
    {
      type: 'button',
      class: 'search-clear',
      'aria-label': t('Очистить поиск'),
      hidden: query === '',
      onclick: () => {
        search.value = '';
        clear.hidden = true;
        onQuery('');
        search.focus();
      },
    },
    '×',
  );
  const search = h('input', {
    type: 'search',
    class: 'search',
    placeholder,
    value: query,
    'aria-label': placeholder,
    // Phone keyboards: a "search" Enter key, no capitals or autocorrection in host names and serials.
    enterkeyhint: 'search',
    autocomplete: 'off',
    autocapitalize: 'off',
    autocorrect: 'off',
    spellcheck: 'false',
    // A plain search field, not a login, card or address: browsers (Chrome on iPhone) then offer no
    // passwords, cards or locations above the keyboard.
    name,
    inputmode: 'search',
    'aria-autocomplete': 'none',
    'data-form-type': 'other',
    oninput: (event) => {
      clear.hidden = event.target.value === '';
      onQuery(event.target.value);
    },
    // The list is already filtered while typing: Enter only hides the phone keyboard to show the results.
    onkeydown: (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        search.blur();
      }
    },
  });

  const filtersToggle = h('button', {
    type: 'button',
    class: 'button filters-toggle',
    'aria-expanded': 'false',
    onclick: () => filtersToggle.setAttribute('aria-expanded', String(toolbar.classList.toggle('open'))),
  });
  const updateFilters = () => {
    const active = activeFilters();
    replace(filtersToggle, t('Фильтры'), active > 0 && h('span', { class: 'n' }, String(active)));
  };

  const atBottom = readSetting(BOTTOM_SETTING, true);
  const bottomCheck = h(
    'label',
    { class: 'check mobile-only' },
    h('input', {
      type: 'checkbox',
      checked: atBottom,
      onchange: (event) => {
        writeSetting(BOTTOM_SETTING, event.target.checked);
        toolbar.classList.toggle('at-bottom', event.target.checked);
      },
    }),
    t('Поиск внизу экрана'),
  );

  const toolbar = h(
    'div',
    { class: atBottom ? 'toolbar list-toolbar at-bottom' : 'toolbar list-toolbar' },
    // A search form (never submitted): autofill treats its field as a search box.
    h('form', { class: 'search-box', role: 'search', autocomplete: 'off', onsubmit: (event) => event.preventDefault() }, search, clear),
    filtersToggle,
    h('div', { class: 'toolbar-extra' }, extra, bottomCheck),
    actions.filter(Boolean).length > 0 && h('div', { class: 'toolbar-actions' }, actions),
  );
  updateFilters();
  trackKeyboard();
  return { toolbar, search, updateFilters };
}

// Puts the cursor in the search when coming back with a query, but not on touch screens:
// the keyboard would cover the list.
export function focusSearch(search, query) {
  if (query && !matchMedia('(pointer: coarse)').matches) search.focus();
}

// The search row at the bottom must stay above the on-screen keyboard. Browsers that do not shrink the page
// for the keyboard (iOS Safari) only shrink the visual viewport: --keyboard is the part it covers (app.css).
let keyboardTracked = false;
function trackKeyboard() {
  const viewport = window.visualViewport;
  if (keyboardTracked || !viewport) return;
  keyboardTracked = true;
  const update = () => {
    const covered = Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop);
    document.documentElement.style.setProperty('--keyboard', `${Math.round(covered)}px`);
  };
  viewport.addEventListener('resize', update);
  viewport.addEventListener('scroll', update);
  update();
}

// Text with the parts found by the search wrapped in <mark>.

// One setting for every list: whether the explanation under the title is open.
const LEAD_SETTING = 'lists.leadOpen';

// The title of a list page with its explanation folded behind an «i» button: the text is read once,
// the rows are needed every time. Phones hide both (app.css: .list-title, .list-lead).
export function listTitle(title, text) {
  const lead = h('p', { class: 'lead list-lead', id: 'list-lead', hidden: !readSetting(LEAD_SETTING, false) }, text);
  const toggle = h(
    'button',
    {
      type: 'button',
      class: 'info-toggle',
      title: t('Что здесь'),
      'aria-label': t('Что здесь'),
      'aria-controls': 'list-lead',
      'aria-expanded': String(!lead.hidden),
      onclick: () => {
        lead.hidden = !lead.hidden;
        toggle.setAttribute('aria-expanded', String(!lead.hidden));
        writeSetting(LEAD_SETTING, !lead.hidden);
      },
    },
    'i',
  );
  return [h('h1', { class: 'list-title' }, title, toggle), lead];
}

export function marked(text, words) {
  const value = text === null || text === undefined ? '' : String(text);
  if (value === '' || words.length === 0) return value;
  return splitMatches(value, words).map((part) => (part.match ? h('mark', {}, part.text) : part.text));
}
