import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { formatDate } from '../logic.js';

export async function renderReleases(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка версий…')));
  let releases;
  let suspector;
  try {
    [releases, suspector] = await Promise.all([api('/releases'), api('/suspector-releases')]);
    releases = releases.releases;
    suspector = suspector.releases;
  } catch (error) {
    replace(main, h('h1', {}, t('Версии программ')), errorBox(error, () => renderReleases(context)));
    return;
  }

  replace(
    main,
    h('h1', {}, t('Версии программ')),
    h('h2', { class: 'section-title first' }, 'Comp Info'),
    h(
      'p',
      { class: 'lead' },
      t('Версии публикует GitHub Actions по тегу. Консоль предлагает установить текущую версию в пункте «Обновить программу». Если новая версия оказалась неудачной, сделайте текущей предыдущую — консоль предложит откат.'),
    ),
    releases.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, t('Версий пока нет. Выпустите первую: tools/update-version.BAT, коммит, тег vВЕРСИЯ и push.')))
      : releaseCards(context, releases, COMP_INFO),
    h('h2', { class: 'section-title' }, 'SUSpector'),
    h(
      'p',
      { class: 'lead' },
      t('Отдельная программа для пользователей: публикуется тем же тегом, но своим архивом, и скачивается без входа и ключа. Ссылка для пользователей: '),
      h('a', { href: SUSPECTOR_PAGE, target: '_blank', rel: 'noopener' }, SUSPECTOR_PAGE),
      h('button', {
        class: 'button small copy-link',
        onclick: async () => {
          try {
            await navigator.clipboard.writeText(SUSPECTOR_PAGE);
            toast(t('Ссылка скопирована'));
          } catch {
            toast(t('Не удалось скопировать ссылку'), 'error');
          }
        },
      }, t('Копировать')),
    ),
    suspector.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, t('Версий SUSpector пока нет: они появятся со следующим тегом vВЕРСИЯ.')))
      : releaseCards(context, suspector, SUSPECTOR),
  );
}

const SUSPECTOR_PAGE = 'https://chagear.top/suspector/';

const COMP_INFO = {
  path: '/releases',
  confirm: (version) => f('Сделать текущей версию {0}? Консоль предложит установить её при следующем обновлении.', version),
  done: (version) => f('Текущая версия: {0}', version),
};

const SUSPECTOR = {
  path: '/suspector-releases',
  confirm: (version) => f('Сделать текущей версию SUSpector {0}? Её будут скачивать со страницы, и кнопка «Обновление» в SUSpector предложит её установить.', version),
  done: (version) => f('Текущая версия SUSpector: {0}', version),
};

// One card per version, the newest first: version and state, then date, commit and size, then what is new.
function releaseCards(context, releases, kind) {
  return h('div', { class: 'release-cards' }, releases.map((release) => releaseCard(context, release, kind)));
}

function releaseCard(context, release, kind) {
  const makeCurrent = async () => {
    if (!confirm(kind.confirm(release.version))) return;
    try {
      await api(`${kind.path}/${encodeURIComponent(release.version)}/make-current`, { method: 'POST' });
      toast(kind.done(release.version));
      await renderReleases(context);
    } catch (error) {
      toast(error.message, 'error');
    }
  };
  const [first, ...rest] = release.notes;
  return h(
    'article',
    { class: release.isCurrent ? 'panel release-card current' : 'panel release-card' },
    h(
      'div',
      { class: 'release-card-head' },
      h('span', { class: 'mono release-version' }, release.version),
      release.isCurrent && h('span', { class: 'badge badge-ok' }, t('текущая')),
      h('span', { class: 'spacer' }),
      !release.isCurrent && h('button', { class: 'button small', onclick: makeCurrent }, t('Сделать текущей')),
    ),
    h(
      'p',
      { class: 'muted small' },
      formatDate(release.publishedAt),
      ' · ',
      h('span', { class: 'mono', title: release.commit }, release.commit.slice(0, 7)),
      ' · ',
      f('{0} КБ', Math.round(release.size / 1024)),
    ),
    first === undefined
      ? h('p', { class: 'muted small' }, t('Без описания изменений.'))
      : rest.length === 0
        ? h('p', { class: 'release-note' }, first)
        : h(
            'details',
            { class: 'release-note' },
            h('summary', {}, first, f(' и ещё {0}', rest.length)),
            h('ul', { class: 'notes' }, rest.map((note) => h('li', {}, note))),
          ),
  );
}
