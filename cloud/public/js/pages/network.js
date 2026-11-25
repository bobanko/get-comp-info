import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { formatDate } from '../logic.js';
import {
  autoHostRange,
  checkReserved,
  commonDns,
  emptyLocation,
  generalProblems,
  isAutoRange,
  locationProblems,
  parseAddressList,
  parseNumberList,
  parseReservedLines,
  rangeEnd,
  rangeSize,
  reservedRows,
  reservedValue,
  subnetInfo,
  subnetMask,
} from '../network-logic.js';

const PREFIXES = [24, 25, 26, 27, 28, 29, 30];

// Admin page: network profiles used by the console's LAN adapter setup and quick ping.
// Locations are a compact table; one location at a time opens in an editor under its row.
export async function renderNetwork(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка настроек сети…')));
  let setting;
  let history;
  try {
    [{ setting }, { history }] = await Promise.all([api('/settings/network'), api('/settings/network/history')]);
  } catch (error) {
    replace(main, h('h1', {}, t('Настройки сети')), errorBox(error, () => renderNetwork(context)));
    return;
  }

  // Editable copy: lists are kept as text while typing; autoRange keeps the host range in step
  // with the gateway and mask until the admin chooses to set it by hand.
  // The saved format keeps DNS in every location (the console reads it there); the page shows
  // the DNS most locations share once, and only the different ones in their locations.
  const value = setting?.value ?? { firstOctet: 10, segments: [], domainCheckHost: null, domainName: null, locations: [emptyLocation()] };
  const sharedDns = commonDns(value.locations);
  const draft = {
    firstOctet: String(value.firstOctet),
    segments: value.segments.join(', '),
    dnsServers: sharedDns,
    domainCheckHost: value.domainCheckHost ?? '',
    domainName: value.domainName ?? '',
    reserved: reservedRows(value.reservedAddresses),
    locations: value.locations.map((location) => ({
      ...location,
      tk: location.tk ?? '',
      dnsServers: location.dnsServers.join(', ') === sharedDns ? '' : location.dnsServers.join(', '),
      segments: (location.segments ?? []).join(', '),
      autoRange: isAutoRange(location),
    })),
  };
  const view = { editing: setting ? null : 0, saved: JSON.stringify(toValue(draft)) };
  draw(context, setting, history, draft, view, null);
}

function draw(context, setting, history, draft, view, errorText) {
  const { main } = context;
  const redraw = (message = null) => draw(context, setting, history, draft, view, message);

  // Parts of the page that follow the form while typing, without rebuilding it (keeps focus).
  const live = [];
  const refresh = () => {
    for (const location of draft.locations) {
      if (!location.autoRange) continue;
      const range = autoHostRange(location);
      if (range) Object.assign(location, range);
    }
    live.forEach((update) => update());
  };
  const liveText = (compute, props = {}) => {
    const element = h('span', props);
    live.push(() => {
      element.textContent = compute();
    });
    return element;
  };
  const input = (object, key, props = {}) =>
    h('input', {
      value: String(object[key] ?? ''),
      oninput: (event) => {
        object[key] = event.target.value;
        refresh();
      },
      ...props,
    });
  const octetInput = (object, key, label) => input(object, key, { class: 'mono octet', inputmode: 'numeric', maxlength: '3', 'aria-label': label });

  const show = (value) => String(value ?? '').trim() || '?';
  const firstSegment = (location) => {
    const own = parseNumberList(location.segments);
    const list = own.length > 0 ? own : parseNumberList(draft.segments);
    return Number.isInteger(list[0]) ? list[0] : '?';
  };
  // "10.20.128" — the part of every address of the location that does not change.
  const base = (location) => `${show(draft.firstOctet)}.${firstSegment(location)}.${show(location.thirdOctet)}`;
  const names = () => draft.locations.map((location) => location.name);
  // A just added location shows its mistakes only after the first attempt to save.
  const problemsOf = (location) => (location.isNew && !view.checked ? [] : locationProblems(location, names()));
  const dirty = () => JSON.stringify(toValue(draft)) !== view.saved;

  const general = h(
    'section',
    { class: 'panel' },
    h('h2', {}, t('Общая часть адреса')),
    h(
      'div',
      { class: 'net-grid' },
      h('span', { class: 'net-label' }, t('Сеть')),
      h(
        'div',
        {},
        h(
          'span',
          { class: 'ip mono' },
          octetInput(draft, 'firstOctet', t('Первый октет')),
          '.',
          input(draft, 'segments', { class: 'mono segments', placeholder: '20, 21', 'aria-label': t('Второй октет: сегменты') }),
          '.x.x',
        ),
        h('small', { class: 'muted hint' }, t('Второй октет — сегменты через запятую, первый используется по умолчанию.')),
      ),
      h('span', { class: 'net-label' }, 'DNS'),
      h(
        'div',
        {},
        input(draft, 'dnsServers', { class: 'mono wide', placeholder: '10.0.0.1, 10.0.0.2', 'aria-label': t('DNS-серверы для всех корпусов') }),
        h('small', { class: 'muted hint' }, t('Для всех корпусов, через запятую, до 4. Корпус может задать свои.')),
      ),
      h('span', { class: 'net-label' }, t('Проверка домена')),
      h(
        'div',
        {},
        input(draft, 'domainCheckHost', { class: 'mono host', placeholder: t('не проверять'), 'aria-label': t('Узел для проверки домена') }),
        h('small', { class: 'muted hint' }, t('Быстрый ping проверяет этот узел, если компьютер в одной из сетей.')),
      ),
      h('span', { class: 'net-label' }, t('Домен')),
      h(
        'div',
        {},
        input(draft, 'domainName', { class: 'mono host', placeholder: t('как узел проверки домена'), 'aria-label': t('Домен для ввода компьютеров') }),
        h('small', { class: 'muted hint' }, t('В него программа вводит компьютеры (раздел «Домен»). Пусто — берётся узел проверки домена.')),
      ),
    ),
    problemList(() => generalProblems(draft)),
  );

  function problemList(compute) {
    const list = h('ul', { class: 'problems' });
    live.push(() => {
      const problems = compute();
      replace(list, problems.map((problem) => h('li', {}, problem)));
      list.hidden = problems.length === 0;
    });
    return list;
  }

  const move = (index, offset) => {
    const target = index + offset;
    if (target < 0 || target >= draft.locations.length) return;
    [draft.locations[index], draft.locations[target]] = [draft.locations[target], draft.locations[index]];
    if (view.editing === index) view.editing = target;
    else if (view.editing === target) view.editing = index;
    redraw();
  };

  const rows = draft.locations.flatMap((location, index) => {
    const editing = view.editing === index;
    const toggle = () => {
      view.editing = editing ? null : index;
      redraw();
    };
    const stop = (action) => (event) => {
      event.stopPropagation();
      action();
    };
    const status = liveText(() => (problemsOf(location).length > 0 ? '!' : ''), {
      class: 'row-problem',
      title: t('Есть ошибки: откройте корпус'),
    });
    const summary = h(
      'tr',
      { class: editing ? 'location-row open' : 'location-row', onclick: toggle },
      h('td', { class: 'name' }, liveText(() => String(location.name ?? '').trim() || f('Корпус {0}', index + 1)), status),
      h('td', {}, liveText(() => String(location.tk ?? '').trim() || '—')),
      h(
        'td',
        { class: 'mono' },
        liveText(() => {
          const info = subnetInfo(location.gatewayOctet, location.prefixLength);
          return `${base(location)}.${info ? info.network : '?'}/${location.prefixLength}`;
        }),
      ),
      h('td', { class: 'mono' }, liveText(() => '.' + show(location.gatewayOctet))),
      h('td', { class: 'mono' }, liveText(() => `.${show(location.firstHostOctet)}–.${show(location.lastHostOctet)}`)),
      h(
        'td',
        { class: 'mono dns' },
        liveText(() => parseAddressList(location.dnsServers).join(', ') || t('общие')),
      ),
      h(
        'td',
        { class: 'row-actions' },
        h('button', { class: 'button small', type: 'button', title: t('Выше в меню консоли'), disabled: index === 0, onclick: stop(() => move(index, -1)) }, '↑'),
        h(
          'button',
          { class: 'button small', type: 'button', title: t('Ниже в меню консоли'), disabled: index === draft.locations.length - 1, onclick: stop(() => move(index, 1)) },
          '↓',
        ),
        h('button', { class: 'button small', type: 'button', onclick: stop(toggle) }, editing ? t('Свернуть') : t('Изменить')),
      ),
    );
    return editing ? [summary, h('tr', { class: 'editor-row' }, h('td', { colspan: '7' }, editor(location, index)))] : [summary];
  });

  function editor(location, index) {
    const prefix = h(
      'select',
      {
        'aria-label': t('Маска'),
        onchange: (event) => {
          location.prefixLength = Number(event.target.value);
          refresh();
        },
      },
      PREFIXES.map((length) =>
        h('option', { value: String(length), selected: Number(location.prefixLength) === length }, f('/{0} — {1}, {2} адр.', length, subnetMask(length), 2 ** (32 - length) - 2)),
      ),
    );

    const range = location.autoRange
      ? liveText(() => {
          const auto = autoHostRange(location);
          if (!auto) return t('Шлюз должен быть первым или последним адресом подсети — или задайте диапазон вручную.');
          return f('{0}.{1} – {2}.{3}  ({4} адр.)', base(location), auto.firstHostOctet, base(location), auto.lastHostOctet, auto.lastHostOctet - auto.firstHostOctet + 1);
        }, { class: 'mono' })
      : h(
          'span',
          { class: 'ip mono' },
          liveText(() => base(location) + '.'),
          octetInput(location, 'firstHostOctet', t('Первый адрес')),
          h('span', { class: 'dash' }, '–'),
          liveText(() => base(location) + '.'),
          octetInput(location, 'lastHostOctet', t('Последний адрес')),
        );

    return h(
      'div',
      { class: 'location-editor' },
      h(
        'div',
        { class: 'net-grid' },
        h('span', { class: 'net-label' }, t('Название')),
        input(location, 'name', { maxlength: '60', class: 'name-input', 'aria-label': t('Название корпуса') }),

        h('span', { class: 'net-label' }, t('ТК')),
        input(location, 'tk', { maxlength: '20', class: 'mono segments', placeholder: t('ТК1000'), 'aria-label': t('Номер ТК корпуса') }),

        h('span', { class: 'net-label' }, t('Подсеть')),
        h(
          'div',
          { class: 'net-inline' },
          h('span', { class: 'ip mono' }, liveText(() => `${show(draft.firstOctet)}.${firstSegment(location)}.`), octetInput(location, 'thirdOctet', t('Третий октет')), '.x'),
          prefix,
        ),

        h('span', { class: 'net-label' }, t('Шлюз')),
        h(
          'div',
          { class: 'net-inline' },
          h('span', { class: 'ip mono' }, liveText(() => base(location) + '.'), octetInput(location, 'gatewayOctet', t('Шлюз, последний октет'))),
          liveText(
            () => {
              const info = subnetInfo(location.gatewayOctet, location.prefixLength);
              return info ? f('подсеть {0}.{1}/{2}, адреса .{3}–.{4}', base(location), info.network, location.prefixLength, info.network + 1, info.broadcast - 1) : '';
            },
            { class: 'muted small' },
          ),
        ),

        h('span', { class: 'net-label' }, t('Адреса ПК')),
        h(
          'div',
          { class: 'net-inline' },
          range,
          h(
            'label',
            { class: 'check small' },
            h('input', {
              type: 'checkbox',
              checked: !location.autoRange,
              onchange: (event) => {
                location.autoRange = !event.target.checked;
                refresh();
                redraw();
              },
            }),
            t('задать вручную'),
          ),
        ),

        h('span', { class: 'net-label' }, 'DNS'),
        h(
          'div',
          {},
          input(location, 'dnsServers', { class: 'mono wide', placeholder: t('общие'), 'aria-label': t('Свои DNS-серверы') }),
          h('small', { class: 'muted hint' }, t('Пусто — общие DNS. Свои — если в корпусе другие серверы.')),
        ),

        h('span', { class: 'net-label' }, t('Сегменты')),
        h(
          'div',
          {},
          input(location, 'segments', { class: 'mono segments', placeholder: t('общие'), 'aria-label': t('Свои сегменты') }),
          h('small', { class: 'muted hint' }, t('Пусто — общие сегменты. Свои — если у корпуса другой второй октет.')),
        ),
      ),
      problemList(() => problemsOf(location)),
      h(
        'div',
        { class: 'actions' },
        h(
          'button',
          {
            class: 'button',
            type: 'button',
            onclick: () => {
              view.editing = null;
              redraw();
            },
          },
          t('Свернуть'),
        ),
        h('span', { class: 'spacer' }),
        h(
          'button',
          {
            class: 'button danger',
            type: 'button',
            disabled: draft.locations.length === 1,
            title: draft.locations.length === 1 ? t('Нужен хотя бы один корпус') : undefined,
            onclick: () => {
              if (!confirm(f('Удалить корпус «{0}»?', location.name || index + 1))) return;
              draft.locations.splice(index, 1);
              view.editing = null;
              redraw();
            },
          },
          t('Удалить корпус'),
        ),
      ),
    );
  }

  const locations = h(
    'section',
    { class: 'panel' },
    h('h2', {}, f('Корпуса ({0})', draft.locations.length)),
    h('p', { class: 'muted small' }, t('Порядок — как в меню консоли. Нажмите на строку, чтобы изменить корпус.')),
    h(
      'div',
      { class: 'table-wrap' },
      h(
        'table',
        { class: 'locations' },
        h('thead', {}, h('tr', {}, h('th', {}, t('Корпус')), h('th', {}, t('ТК')), h('th', {}, t('Подсеть')), h('th', {}, t('Шлюз')), h('th', {}, t('Адреса ПК')), h('th', {}, 'DNS'), h('th', {}))),
        h('tbody', {}, rows),
      ),
    ),
  );

  // Reserved addresses: one row per address or range. Enter adds the next row; lines pasted into an address
  // field become rows ("10.0.0.5 — сервер", "10.0.0.20-29 камеры").
  const reservedBody = h('tbody');
  // Empty rows are left out of the check and of the saved list.
  const reservedRowsChecked = () => checkReserved(draft.reserved);
  const addReserved = (at, rows = [{ address: '', lastAddress: '', note: '' }]) => {
    draft.reserved.splice(at, 0, ...rows.map((row) => ({ ...row })));
    drawReserved();
    refresh();
    reservedBody.rows[at]?.querySelector('input')?.focus();
  };
  const reservedInput = (row, index, key, props) =>
    h('input', {
      value: row[key],
      spellcheck: 'false',
      oninput: (event) => {
        row[key] = event.target.value;
        refresh();
      },
      onkeydown: (event) => {
        if (event.key !== 'Enter') return;
        event.preventDefault();
        addReserved(index + 1);
      },
      ...props,
    });
  function drawReserved() {
    if (draft.reserved.length === 0) {
      replace(reservedBody, h('tr', { class: 'empty-row' }, h('td', { colspan: '6', class: 'empty' }, t('Занятых адресов нет.'))));
      return;
    }
    replace(
      reservedBody,
      draft.reserved.map((row, index) =>
        h(
          'tr',
          {},
          h('td', { class: 'muted number', 'data-col': 'number' }, String(index + 1)),
          h(
            'td',
            { 'data-col': 'address' },
            reservedInput(row, index, 'address', {
              class: 'mono address',
              inputmode: 'decimal',
              placeholder: '10.0.0.5',
              'aria-label': f('Адрес, строка {0}', index + 1),
              // Several lines pasted: each becomes a row, this one first.
              onpaste: (event) => {
                const text = event.clipboardData?.getData('text') ?? '';
                if (!/\n/.test(text.trim())) return;
                event.preventDefault();
                const pasted = parseReservedLines(text);
                draft.reserved.splice(index, row.address.trim() || row.note.trim() ? 0 : 1);
                addReserved(index, pasted);
              },
            }),
          ),
          h(
            'td',
            { 'data-col': 'last' },
            h('span', { class: 'dash', 'aria-hidden': 'true' }, '–'),
            reservedInput(row, index, 'lastAddress', {
              class: 'mono address',
              inputmode: 'decimal',
              placeholder: t('один адрес'),
              title: t('Конец диапазона: полный адрес или только последний октет'),
              'aria-label': f('Конец диапазона, строка {0}', index + 1),
            }),
          ),
          h(
            'td',
            { class: 'muted small nowrap size', 'data-col': 'size' },
            liveText(() => {
              const size = rangeSize(row);
              return size > 1 ? f('{0} адр. (до {1})', size, rangeEnd(row.address, row.lastAddress)) : '';
            }),
          ),
          h('td', { 'data-col': 'note' }, reservedInput(row, index, 'note', { class: 'note', maxlength: '200', placeholder: t('примечание'), 'aria-label': f('Примечание, строка {0}', index + 1) })),
          h(
            'td',
            { class: 'row-actions' },
            h(
              'button',
              {
                class: 'button small danger',
                type: 'button',
                title: t('Удалить строку'),
                'aria-label': f('Удалить строку {0}', index + 1),
                onclick: () => {
                  draft.reserved.splice(index, 1);
                  drawReserved();
                  refresh();
                },
              },
              '✕',
            ),
          ),
        ),
      ),
    );
  }
  // Marks the rows with mistakes while typing.
  live.push(() => {
    const { marks } = reservedRowsChecked();
    [...reservedBody.rows].forEach((tr, index) => {
      const mark = marks.get(index);
      tr.classList.toggle('invalid', Boolean(mark));
      tr.title = mark ?? '';
    });
  });
  drawReserved();

  const reserved = h(
    'section',
    { class: 'panel' },
    h(
      'h2',
      {},
      liveText(() => {
        const value = reservedValue(draft.reserved);
        const addresses = draft.reserved.reduce((sum, row) => sum + (rangeSize(row) ?? 0), 0);
        return f('Занятые адреса: строк {0}, адресов {1}', value.length, addresses);
      }),
    ),
    h(
      'p',
      { class: 'muted small' },
      t('Адреса без записи в базе: серверы, камеры, принтеры, компьютеры без серийного номера. Программа не предлагает их как свободные при вводе в домен. Диапазон — начало и конец (конец можно указать одним последним октетом). Enter добавляет строку; несколько строк из буфера обмена вставляются в адрес как отдельные строки.'),
    ),
    h(
      'div',
      { class: 'table-wrap' },
      h(
        'table',
        { class: 'reserved' },
        h('thead', {}, h('tr', {}, h('th', {}, '№'), h('th', {}, t('Адрес')), h('th', {}, t('Конец диапазона')), h('th', {}), h('th', {}, t('Примечание')), h('th', {}))),
        reservedBody,
      ),
    ),
    h('div', { class: 'actions' }, h('button', { class: 'button', type: 'button', onclick: () => addReserved(draft.reserved.length) }, t('+ Адрес'))),
    problemList(() => reservedRowsChecked().problems),
  );

  const saveButton = h('button', { class: 'button primary', type: 'submit' }, t('Сохранить'));
  const form = h(
    'form',
    {
      onsubmit: async (event) => {
        event.preventDefault();
        view.checked = true;
        // Same checks as the server, so mistakes are shown next to the field.
        if (generalProblems(draft).length > 0) {
          view.editing = null;
          redraw(t('Исправьте общую часть адреса.'));
          return;
        }
        if (checkReserved(draft.reserved).problems.length > 0) {
          view.editing = null;
          redraw(t('Исправьте список занятых адресов.'));
          return;
        }
        const broken = draft.locations.findIndex((location) => locationProblems(location, names()).length > 0);
        if (broken >= 0) {
          view.editing = broken;
          redraw(f('Исправьте корпус «{0}».', draft.locations[broken].name || broken + 1));
          return;
        }
        saveButton.disabled = true;
        try {
          await api('/settings/network', { method: 'PUT', body: { baseRevision: setting?.revision ?? null, value: toValue(draft) } });
          toast(t('Настройки сети сохранены. Консоль получит их при следующей синхронизации.'));
          await renderNetwork(context);
        } catch (error) {
          saveButton.disabled = false;
          redraw(error.code === 'conflict' ? t('Настройки уже изменил другой администратор. Обновите страницу: ваши правки не сохранены.') : error.message);
        }
      },
    },
    errorText && h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, errorText)),
    general,
    locations,
    reserved,
    h(
      'div',
      { class: 'actions sticky-actions' },
      h(
        'button',
        {
          class: 'button',
          type: 'button',
          onclick: () => {
            draft.locations.push({ ...emptyLocation(), dnsServers: '', segments: '', autoRange: true, isNew: true });
            view.editing = draft.locations.length - 1;
            redraw();
          },
        },
        t('+ Корпус'),
      ),
      h('span', { class: 'spacer' }),
      liveText(() => (dirty() ? t('Есть несохранённые изменения') : ''), { class: 'muted small' }),
      saveButton,
    ),
  );

  replace(
    main,
    h('h1', {}, t('Настройки сети')),
    h(
      'p',
      { class: 'lead' },
      t('Корпуса и подсети для настройки LAN-адаптера и быстрого ping в программе. Хранятся только в облаке; программа скачивает их при синхронизации и дальше работает без интернета.'),
    ),
    setting
      ? h('p', { class: 'muted small', title: setting.updatedBy }, f('Ревизия {0}, изменено {1}, {2}', setting.revision, formatDate(setting.updatedAt), setting.updatedByName ?? setting.updatedBy))
      : h('div', { class: 'notice' }, h('p', {}, t('Настройки сети ещё не заданы: заполните и сохраните.'))),
    form,
    history.length > 0 &&
      h(
        'details',
        { class: 'panel muted small' },
        h('summary', {}, f('История ({0})', history.length)),
        h('ul', {}, history.map((entry) => h('li', { title: entry.changedBy }, `#${entry.revision} — ${formatDate(entry.changedAt)}, ${entry.changedByName ?? entry.changedBy}`))),
      ),
  );
  refresh();
}

// Converts the form back to the settings document; the server validates it again.
function toValue(draft) {
  const number = (text) => (String(text ?? '').trim() === '' ? null : Number(String(text).trim()));
  return {
    firstOctet: number(draft.firstOctet),
    segments: parseNumberList(draft.segments),
    domainCheckHost: draft.domainCheckHost.trim() || null,
    domainName: draft.domainName.trim() || null,
    locations: draft.locations.map((location) => {
      const result = {
        name: String(location.name ?? '').trim(),
        thirdOctet: number(location.thirdOctet),
        prefixLength: Number(location.prefixLength),
        gatewayOctet: number(location.gatewayOctet),
        firstHostOctet: number(location.firstHostOctet),
        lastHostOctet: number(location.lastHostOctet),
        // Own DNS if set, otherwise the shared one: the console reads DNS from each location.
        dnsServers: parseAddressList(String(location.dnsServers ?? '').trim() ? location.dnsServers : draft.dnsServers),
      };
      const segments = parseNumberList(location.segments);
      if (segments.length > 0) result.segments = segments;
      const tk = String(location.tk ?? '').trim();
      if (tk) result.tk = tk;
      return result;
    }),
    reservedAddresses: reservedValue(draft.reserved),
  };
}
