import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate } from '../logic.js';
import {
  autoHostRange,
  commonDns,
  emptyLocation,
  generalProblems,
  isAutoRange,
  locationProblems,
  parseAddressList,
  parseNumberList,
  subnetInfo,
  subnetMask,
} from '../network-logic.js';

const PREFIXES = [24, 25, 26, 27, 28, 29, 30];

// Admin page: network profiles used by the console's LAN adapter setup and quick ping.
// Locations are a compact table; one location at a time opens in an editor under its row.
export async function renderNetwork(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка настроек сети…'));
  let setting;
  let history;
  try {
    [{ setting }, { history }] = await Promise.all([api('/settings/network'), api('/settings/network/history')]);
  } catch (error) {
    replace(main, h('h1', {}, 'Сеть'), errorBox(error, () => renderNetwork(context)));
    return;
  }

  // Editable copy: lists are kept as text while typing; autoRange keeps the host range in step
  // with the gateway and mask until the admin chooses to set it by hand.
  // The saved format keeps DNS in every location (the console reads it there); the page shows
  // the DNS most locations share once, and only the different ones in their locations.
  const value = setting?.value ?? { firstOctet: 10, segments: [], domainCheckHost: null, locations: [emptyLocation()] };
  const sharedDns = commonDns(value.locations);
  const draft = {
    firstOctet: String(value.firstOctet),
    segments: value.segments.join(', '),
    dnsServers: sharedDns,
    domainCheckHost: value.domainCheckHost ?? '',
    locations: value.locations.map((location) => ({
      ...location,
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
    h('h2', {}, 'Общая часть адреса'),
    h(
      'div',
      { class: 'net-grid' },
      h('span', { class: 'net-label' }, 'Сеть'),
      h(
        'div',
        {},
        h(
          'span',
          { class: 'ip mono' },
          octetInput(draft, 'firstOctet', 'Первый октет'),
          '.',
          input(draft, 'segments', { class: 'mono segments', placeholder: '20, 21', 'aria-label': 'Второй октет: сегменты' }),
          '.x.x',
        ),
        h('small', { class: 'muted hint' }, 'Второй октет — сегменты через запятую, первый используется по умолчанию.'),
      ),
      h('span', { class: 'net-label' }, 'DNS'),
      h(
        'div',
        {},
        input(draft, 'dnsServers', { class: 'mono wide', placeholder: '10.0.0.1, 10.0.0.2', 'aria-label': 'DNS-серверы для всех корпусов' }),
        h('small', { class: 'muted hint' }, 'Для всех корпусов, через запятую, до 4. Корпус может задать свои.'),
      ),
      h('span', { class: 'net-label' }, 'Проверка домена'),
      h(
        'div',
        {},
        input(draft, 'domainCheckHost', { class: 'mono host', placeholder: 'не проверять', 'aria-label': 'Узел для проверки домена' }),
        h('small', { class: 'muted hint' }, 'Быстрый ping проверяет этот узел, если компьютер в одной из сетей.'),
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
      title: 'Есть ошибки: откройте корпус',
    });
    const summary = h(
      'tr',
      { class: editing ? 'location-row open' : 'location-row', onclick: toggle },
      h('td', { class: 'name' }, liveText(() => String(location.name ?? '').trim() || `Корпус ${index + 1}`), status),
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
        liveText(() => parseAddressList(location.dnsServers).join(', ') || 'общие'),
      ),
      h(
        'td',
        { class: 'row-actions' },
        h('button', { class: 'button small', type: 'button', title: 'Выше в меню консоли', disabled: index === 0, onclick: stop(() => move(index, -1)) }, '↑'),
        h(
          'button',
          { class: 'button small', type: 'button', title: 'Ниже в меню консоли', disabled: index === draft.locations.length - 1, onclick: stop(() => move(index, 1)) },
          '↓',
        ),
        h('button', { class: 'button small', type: 'button', onclick: stop(toggle) }, editing ? 'Свернуть' : 'Изменить'),
      ),
    );
    return editing ? [summary, h('tr', { class: 'editor-row' }, h('td', { colspan: '6' }, editor(location, index)))] : [summary];
  });

  function editor(location, index) {
    const prefix = h(
      'select',
      {
        'aria-label': 'Маска',
        onchange: (event) => {
          location.prefixLength = Number(event.target.value);
          refresh();
        },
      },
      PREFIXES.map((length) =>
        h('option', { value: String(length), selected: Number(location.prefixLength) === length }, `/${length} — ${subnetMask(length)}, ${2 ** (32 - length) - 2} адр.`),
      ),
    );

    const range = location.autoRange
      ? liveText(() => {
          const auto = autoHostRange(location);
          if (!auto) return 'Шлюз должен быть первым или последним адресом подсети — или задайте диапазон вручную.';
          return `${base(location)}.${auto.firstHostOctet} – ${base(location)}.${auto.lastHostOctet}  (${auto.lastHostOctet - auto.firstHostOctet + 1} адр.)`;
        }, { class: 'mono' })
      : h(
          'span',
          { class: 'ip mono' },
          liveText(() => base(location) + '.'),
          octetInput(location, 'firstHostOctet', 'Первый адрес'),
          h('span', { class: 'dash' }, '–'),
          liveText(() => base(location) + '.'),
          octetInput(location, 'lastHostOctet', 'Последний адрес'),
        );

    return h(
      'div',
      { class: 'location-editor' },
      h(
        'div',
        { class: 'net-grid' },
        h('span', { class: 'net-label' }, 'Название'),
        input(location, 'name', { maxlength: '60', class: 'name-input', 'aria-label': 'Название корпуса' }),

        h('span', { class: 'net-label' }, 'Подсеть'),
        h(
          'div',
          { class: 'net-inline' },
          h('span', { class: 'ip mono' }, liveText(() => `${show(draft.firstOctet)}.${firstSegment(location)}.`), octetInput(location, 'thirdOctet', 'Третий октет'), '.x'),
          prefix,
        ),

        h('span', { class: 'net-label' }, 'Шлюз'),
        h(
          'div',
          { class: 'net-inline' },
          h('span', { class: 'ip mono' }, liveText(() => base(location) + '.'), octetInput(location, 'gatewayOctet', 'Шлюз, последний октет')),
          liveText(
            () => {
              const info = subnetInfo(location.gatewayOctet, location.prefixLength);
              return info ? `подсеть ${base(location)}.${info.network}/${location.prefixLength}, адреса .${info.network + 1}–.${info.broadcast - 1}` : '';
            },
            { class: 'muted small' },
          ),
        ),

        h('span', { class: 'net-label' }, 'Адреса ПК'),
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
            'задать вручную',
          ),
        ),

        h('span', { class: 'net-label' }, 'DNS'),
        h(
          'div',
          {},
          input(location, 'dnsServers', { class: 'mono wide', placeholder: 'общие', 'aria-label': 'Свои DNS-серверы' }),
          h('small', { class: 'muted hint' }, 'Пусто — общие DNS. Свои — если в корпусе другие серверы.'),
        ),

        h('span', { class: 'net-label' }, 'Сегменты'),
        h(
          'div',
          {},
          input(location, 'segments', { class: 'mono segments', placeholder: 'общие', 'aria-label': 'Свои сегменты' }),
          h('small', { class: 'muted hint' }, 'Пусто — общие сегменты. Свои — если у корпуса другой второй октет.'),
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
          'Свернуть',
        ),
        h('span', { class: 'spacer' }),
        h(
          'button',
          {
            class: 'button danger',
            type: 'button',
            disabled: draft.locations.length === 1,
            title: draft.locations.length === 1 ? 'Нужен хотя бы один корпус' : undefined,
            onclick: () => {
              if (!confirm(`Удалить корпус «${location.name || index + 1}»?`)) return;
              draft.locations.splice(index, 1);
              view.editing = null;
              redraw();
            },
          },
          'Удалить корпус',
        ),
      ),
    );
  }

  const locations = h(
    'section',
    { class: 'panel' },
    h('h2', {}, `Корпуса (${draft.locations.length})`),
    h('p', { class: 'muted small' }, 'Порядок — как в меню консоли. Нажмите на строку, чтобы изменить корпус.'),
    h(
      'div',
      { class: 'table-wrap' },
      h(
        'table',
        { class: 'locations' },
        h('thead', {}, h('tr', {}, h('th', {}, 'Корпус'), h('th', {}, 'Подсеть'), h('th', {}, 'Шлюз'), h('th', {}, 'Адреса ПК'), h('th', {}, 'DNS'), h('th', {}))),
        h('tbody', {}, rows),
      ),
    ),
  );

  const saveButton = h('button', { class: 'button primary', type: 'submit' }, 'Сохранить');
  const form = h(
    'form',
    {
      onsubmit: async (event) => {
        event.preventDefault();
        view.checked = true;
        // Same checks as the server, so mistakes are shown next to the field.
        if (generalProblems(draft).length > 0) {
          view.editing = null;
          redraw('Исправьте общую часть адреса.');
          return;
        }
        const broken = draft.locations.findIndex((location) => locationProblems(location, names()).length > 0);
        if (broken >= 0) {
          view.editing = broken;
          redraw(`Исправьте корпус «${draft.locations[broken].name || broken + 1}».`);
          return;
        }
        saveButton.disabled = true;
        try {
          await api('/settings/network', { method: 'PUT', body: { baseRevision: setting?.revision ?? null, value: toValue(draft) } });
          toast('Настройки сети сохранены. Консоль получит их при следующей синхронизации.');
          await renderNetwork(context);
        } catch (error) {
          saveButton.disabled = false;
          redraw(error.code === 'conflict' ? 'Настройки уже изменил другой администратор. Обновите страницу: ваши правки не сохранены.' : error.message);
        }
      },
    },
    errorText && h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, errorText)),
    general,
    locations,
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
        '+ Корпус',
      ),
      h('span', { class: 'spacer' }),
      liveText(() => (dirty() ? 'Есть несохранённые изменения' : ''), { class: 'muted small' }),
      saveButton,
    ),
  );

  replace(
    main,
    h('h1', {}, 'Сеть'),
    h(
      'p',
      { class: 'lead' },
      'Корпуса и подсети для настройки LAN-адаптера и быстрого ping в консоли. Хранятся только в облаке; консоль скачивает их при синхронизации и дальше работает без интернета.',
    ),
    setting
      ? h('p', { class: 'muted small' }, `Ревизия ${setting.revision}, изменено ${formatDate(setting.updatedAt)}, ${setting.updatedBy}`)
      : h('div', { class: 'notice' }, h('p', {}, 'Настройки сети ещё не заданы: заполните и сохраните.')),
    form,
    history.length > 0 &&
      h(
        'details',
        { class: 'panel muted small' },
        h('summary', {}, `История (${history.length})`),
        h('ul', {}, history.map((entry) => h('li', {}, `#${entry.revision} — ${formatDate(entry.changedAt)}, ${entry.changedBy}`))),
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
      return result;
    }),
  };
}
