import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate } from '../logic.js';

export async function renderReleases(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка версий…'));
  let releases;
  let suspector;
  try {
    [releases, suspector] = await Promise.all([api('/releases'), api('/suspector-releases')]);
    releases = releases.releases;
    suspector = suspector.releases;
  } catch (error) {
    replace(main, h('h1', {}, 'Версии программ'), errorBox(error, () => renderReleases(context)));
    return;
  }

  replace(
    main,
    h('h1', {}, 'Версии программ'),
    h('h2', { class: 'section-title first' }, 'Comp Info'),
    h(
      'p',
      { class: 'lead' },
      'Версии публикует GitHub Actions по тегу. Консоль предлагает установить текущую версию в пункте «Обновить программу». Если новая версия оказалась неудачной, сделайте текущей предыдущую — консоль предложит откат.',
    ),
    releases.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, 'Версий пока нет. Выпустите первую: tools/update-version.BAT, коммит, тег vВЕРСИЯ и push.'))
      : h('section', { class: 'panel' }, releasesTable(context, releases, COMP_INFO)),
    h('h2', { class: 'section-title' }, 'SUSpector'),
    h(
      'p',
      { class: 'lead' },
      'Отдельная программа для пользователей: публикуется тем же тегом, но своим архивом, и скачивается без входа и ключа. Ссылка для пользователей: ',
      h('a', { href: SUSPECTOR_PAGE, target: '_blank', rel: 'noopener' }, location.origin + SUSPECTOR_PAGE),
      h('button', {
        class: 'button small copy-link',
        onclick: async () => {
          try {
            await navigator.clipboard.writeText(location.origin + SUSPECTOR_PAGE);
            toast('Ссылка скопирована');
          } catch {
            toast('Не удалось скопировать ссылку', 'error');
          }
        },
      }, 'Копировать'),
    ),
    suspector.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, 'Версий SUSpector пока нет: они появятся со следующим тегом vВЕРСИЯ.'))
      : h('section', { class: 'panel' }, releasesTable(context, suspector, SUSPECTOR)),
  );
}

const SUSPECTOR_PAGE = '/suspector/';

const COMP_INFO = {
  path: '/releases',
  confirm: (version) => `Сделать текущей версию ${version}? Консоль предложит установить её при следующем обновлении.`,
  done: (version) => `Текущая версия: ${version}`,
};

const SUSPECTOR = {
  path: '/suspector-releases',
  confirm: (version) => `Сделать текущей версию SUSpector ${version}? Её будут скачивать со страницы, и кнопка «Обновление» в SUSpector предложит её установить.`,
  done: (version) => `Текущая версия SUSpector: ${version}`,
};

function releasesTable(context, releases, kind) {
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
                      if (!confirm(kind.confirm(release.version))) return;
                      try {
                        await api(`${kind.path}/${encodeURIComponent(release.version)}/make-current`, { method: 'POST' });
                        toast(kind.done(release.version));
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
