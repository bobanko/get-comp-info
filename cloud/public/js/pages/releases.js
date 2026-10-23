import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate } from '../logic.js';

export async function renderReleases(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка версий…'));
  let releases;
  try {
    releases = (await api('/releases')).releases;
  } catch (error) {
    replace(main, h('h1', {}, 'Версии программы'), errorBox(error, () => renderReleases(context)));
    return;
  }

  replace(
    main,
    h('h1', {}, 'Версии программы'),
    h(
      'p',
      { class: 'lead' },
      'Версии публикует GitHub Actions по тегу. Консоль предлагает установить текущую версию в пункте «Обновить программу». Если новая версия оказалась неудачной, сделайте текущей предыдущую — консоль предложит откат.',
    ),
    releases.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, 'Версий пока нет. Выпустите первую: tools/update-version.BAT, коммит, тег vВЕРСИЯ и push.'))
      : h('section', { class: 'panel' }, releasesTable(context, releases)),
  );
}

function releasesTable(context, releases) {
  return h(
    'div',
    { class: 'table-wrap' },
    h(
      'table',
      {},
      h('thead', {}, h('tr', {}, h('th', {}, 'Версия'), h('th', {}, 'Опубликована'), h('th', {}, 'Что нового'), h('th', {}, 'Размер'), h('th', {}))),
      h(
        'tbody',
        {},
        releases.map((release) =>
          h(
            'tr',
            {},
            h(
              'td',
              { class: 'nowrap' },
              h('span', { class: 'mono' }, release.version),
              release.isCurrent && h('span', { class: 'badge badge-ok' }, 'текущая'),
              h('div', { class: 'muted small mono', title: release.commit }, release.commit.slice(0, 7)),
            ),
            h('td', { class: 'nowrap' }, formatDate(release.publishedAt)),
            h(
              'td',
              {},
              release.notes.length === 0
                ? h('span', { class: 'muted' }, '—')
                : h(
                    'details',
                    {},
                    h('summary', {}, release.notes[0], release.notes.length > 1 ? ` и ещё ${release.notes.length - 1}` : ''),
                    h('ul', { class: 'notes' }, release.notes.slice(1).map((note) => h('li', {}, note))),
                  ),
            ),
            h('td', { class: 'nowrap' }, `${Math.round(release.size / 1024)} КБ`),
            h(
              'td',
              { class: 'right' },
              !release.isCurrent &&
                h(
                  'button',
                  {
                    class: 'button small',
                    onclick: async () => {
                      if (!confirm(`Сделать текущей версию ${release.version}? Консоль предложит установить её при следующем обновлении.`)) return;
                      try {
                        await api(`/releases/${encodeURIComponent(release.version)}/make-current`, { method: 'POST' });
                        toast(`Текущая версия: ${release.version}`);
                        await renderReleases(context);
                      } catch (error) {
                        toast(error.message, 'error');
                      }
                    },
                  },
                  'Сделать текущей',
                ),
            ),
          ),
        ),
      ),
    ),
  );
}
