import { api } from '../api.js';
import { errorBox, h, replace } from '../dom.js';
import { f, t } from '../i18n.js';
import { formatDate } from '../logic.js';

// The current release of the toolbox for anyone signed in: the window program and the console version.
export async function renderDownloads(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка…')));
  let info;
  try {
    info = await api('/downloads');
  } catch (error) {
    replace(main, h('h1', {}, t('Загрузки')), errorBox(error, () => renderDownloads(context)));
    return;
  }

  replace(
    main,
    h('h1', {}, t('Загрузки')),
    h(
      'p',
      { class: 'lead' },
      f('Текущая версия Тулбокса: {0} от {1}. Распакуйте архив в папку на флешке; для синхронизации нужен личный ключ (раздел «Ключи» или у администратора). Дальше программа обновляется сама.', info.version, formatDate(info.publishedAt)),
    ),
    h(
      'div',
      { class: 'card-grid' },
      downloadPanel(
        t('Оконная версия'),
        t('comp-info.exe — все разделы Тулбокса в одном окне. В архиве программа и настройки облака.'),
        'window',
        info.version,
        info.window,
      ),
      downloadPanel(
        t('Консольная версия'),
        t('Меню и скрипты PowerShell (console\\comp-info-menu.BAT) — для компьютеров, где удобнее консоль.'),
        'console',
        info.version,
        info.console,
      ),
    ),
    info.notes.length > 0 &&
      h('details', { class: 'panel' }, h('summary', {}, t('Что нового в этой версии')), h('ul', {}, info.notes.map((note) => h('li', {}, note)))),
    h(
      'section',
      { class: 'panel' },
      h('h2', {}, t('Программы для пользователей')),
      h(
        'p',
        { class: 'muted' },
        t('Их можно скачать без входа на сайте chagear.top: '),
        h('a', { href: 'https://chagear.top/suspector/', target: '_blank', rel: 'noopener' }, 'SUSpector'),
        ', ',
        h('a', { href: 'https://chagear.top/hashpot/', target: '_blank', rel: 'noopener' }, 'HashPot'),
        '.',
      ),
    ),
  );
}

function downloadPanel(title, text, kind, version, files) {
  const available = files.length > 0;
  return h(
    'section',
    { class: 'panel' },
    h('h2', {}, title),
    h('p', { class: 'muted' }, text),
    available
      ? h('a', { class: 'button primary', href: `/api/web/downloads/${kind}`, download: `comp-info-${version}-${kind}.zip` }, f('Скачать comp-info-{0}-{1}.zip', version, kind))
      : h('p', { class: 'empty' }, t('В текущей версии этого варианта нет.')),
    available && h('p', { class: 'muted small' }, f('Файлов: {0}', files.length)),
  );
}
