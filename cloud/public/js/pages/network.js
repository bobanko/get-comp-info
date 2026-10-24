import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate } from '../logic.js';
import { describeLocation, emptyLocation, parseNumberList, suggestHostRange } from '../network-logic.js';

// Admin page: network profiles used by the console's LAN adapter setup and quick ping.
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

  // Editable copy; numbers lists are kept as text while typing.
  const value = setting?.value ?? { firstOctet: 10, segments: [], domainCheckHost: null, locations: [emptyLocation()] };
  const draft = {
    firstOctet: String(value.firstOctet),
    segments: value.segments.join(', '),
    domainCheckHost: value.domainCheckHost ?? '',
    locations: value.locations.map((location) => ({
      ...location,
      dnsServers: location.dnsServers.join(', '),
      segments: (location.segments ?? []).join(', '),
    })),
  };
  draw(context, setting, history, draft, null);
}

function draw(context, setting, history, draft, errorText) {
  const { main } = context;
  const redraw = (message = null) => draw(context, setting, history, draft, message);

  const field = (label, input, hint) => h('label', { class: 'field' }, h('span', {}, label), input, hint && h('small', { class: 'muted' }, hint));
  // Narrow field for a single octet (0-255) or the prefix select.
  const narrowField = (label, input) => h('label', { class: 'field narrow' }, h('span', {}, label), input);
  const octetInput = (object, key) => textInput(object, key, { inputmode: 'numeric', maxlength: '3', class: 'mono' });
  const textInput = (object, key, props = {}) =>
    h('input', {
      value: String(object[key] ?? ''),
      oninput: (event) => {
        object[key] = event.target.value;
        refreshPreviews();
      },
      ...props,
    });

  const previews = [];
  const refreshPreviews = () => previews.forEach((update) => update());

  const locationCards = draft.locations.map((location, index) => {
    const preview = h('p', { class: 'muted small mono' });
    const updatePreview = () => {
      preview.textContent = describeLocation(Number(draft.firstOctet), parseNumberList(location.segments || draft.segments)[0], location);
    };
    previews.push(updatePreview);
    updatePreview();

    const prefix = h(
      'select',
      {
        onchange: (event) => {
          location.prefixLength = Number(event.target.value);
          refreshPreviews();
        },
      },
      [24, 25, 26, 27, 28, 29, 30].map((length) => h('option', { value: String(length), selected: Number(location.prefixLength) === length }, `/${length}`)),
    );
    const move = (offset) => {
      const target = index + offset;
      if (target < 0 || target >= draft.locations.length) return;
      [draft.locations[index], draft.locations[target]] = [draft.locations[target], draft.locations[index]];
      redraw();
    };

    return h(
      'section',
      { class: 'panel location' },
      h(
        'div',
        { class: 'panel-head' },
        h('h2', {}, location.name || `Корпус ${index + 1}`),
        h(
          'div',
          { class: 'actions' },
          h('button', { class: 'button small', type: 'button', title: 'Выше в меню консоли', disabled: index === 0, onclick: () => move(-1) }, '↑'),
          h('button', { class: 'button small', type: 'button', title: 'Ниже в меню консоли', disabled: index === draft.locations.length - 1, onclick: () => move(1) }, '↓'),
          h(
            'button',
            {
              class: 'button small danger',
              type: 'button',
              onclick: () => {
                if (!confirm(`Удалить корпус «${location.name || index + 1}»?`)) return;
                draft.locations.splice(index, 1);
                redraw();
              },
            },
            'Удалить',
          ),
        ),
      ),
      h(
        'div',
        { class: 'grid-fields' },
        field('Название', textInput(location, 'name', { maxlength: '60' })),
        narrowField('Третий октет', octetInput(location, 'thirdOctet')),
        narrowField('Маска', prefix),
        narrowField('Шлюз, посл. октет', octetInput(location, 'gatewayOctet')),
        narrowField('Первый адрес', octetInput(location, 'firstHostOctet')),
        narrowField('Последний адрес', octetInput(location, 'lastHostOctet')),
        field('DNS-серверы', textInput(location, 'dnsServers', { class: 'mono' }), 'Через запятую, до 4.'),
        field('Свои сегменты', textInput(location, 'segments', { class: 'mono' }), 'Пусто — общие сегменты.'),
      ),
      h(
        'div',
        { class: 'actions' },
        h(
          'button',
          {
            class: 'button small',
            type: 'button',
            onclick: () => {
              const range = suggestHostRange(Number(location.gatewayOctet), Number(location.prefixLength));
              if (!range) {
                toast('Сначала укажите шлюз и маску.', 'error');
                return;
              }
              Object.assign(location, range);
              redraw();
            },
          },
          'Рассчитать диапазон по шлюзу и маске',
        ),
        preview,
      ),
    );
  });

  const saveButton = h('button', { class: 'button primary', type: 'submit' }, 'Сохранить');
  const form = h(
    'form',
    {
      onsubmit: async (event) => {
        event.preventDefault();
        saveButton.disabled = true;
        try {
          const result = await api('/settings/network', { method: 'PUT', body: { baseRevision: setting?.revision ?? null, value: toValue(draft) } });
          toast('Настройки сети сохранены. Консоль получит их при следующей синхронизации.');
          setting = result.setting;
          await renderNetwork(context);
        } catch (error) {
          saveButton.disabled = false;
          redraw(error.code === 'conflict' ? 'Настройки уже изменил другой администратор. Обновите страницу: ваши правки не сохранены.' : error.message);
        }
      },
    },
    errorText && h('div', { class: 'notice notice-error', role: 'alert' }, h('p', {}, errorText)),
    h(
      'section',
      { class: 'panel' },
      h('h2', {}, 'Общие параметры'),
      h(
        'div',
        { class: 'grid-fields' },
        narrowField('Первый октет', octetInput(draft, 'firstOctet')),
        field('Сегменты (второй октет)', textInput(draft, 'segments', { class: 'mono' }), 'Через запятую, первый — по умолчанию.'),
        field('Узел для проверки домена', textInput(draft, 'domainCheckHost', { class: 'mono', placeholder: 'не проверять' }), 'Быстрый ping проверяет его, если компьютер в одной из сетей.'),
      ),
    ),
    locationCards,
    h(
      'div',
      { class: 'actions sticky-actions' },
      h(
        'button',
        {
          class: 'button',
          type: 'button',
          onclick: () => {
            draft.locations.push({ ...emptyLocation(), dnsServers: '', segments: '' });
            redraw();
          },
        },
        '+ Корпус',
      ),
      h('span', { class: 'spacer' }),
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
        'section',
        { class: 'panel muted small' },
        h('h2', {}, 'История'),
        h('ul', {}, history.map((entry) => h('li', {}, `#${entry.revision} — ${formatDate(entry.changedAt)}, ${entry.changedBy}`))),
      ),
  );
}

// Converts the form back to the settings document; the server validates it.
function toValue(draft) {
  const number = (text) => (String(text).trim() === '' ? null : Number(String(text).trim()));
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
        dnsServers: String(location.dnsServers ?? '').split(',').map((item) => item.trim()).filter(Boolean),
      };
      const segments = parseNumberList(location.segments);
      if (segments.length > 0) result.segments = segments;
      return result;
    }),
  };
}
