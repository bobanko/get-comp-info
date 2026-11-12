# youSBee

`youSBee.exe` — самостоятельная программа: серийные номера подключённых флешек и USB-дисков.
Это раздел «USB-накопители» Тулбокса (`app/src/UsbPage.cs`) в отдельном окне — для тех, кому из всего Тулбокса нужен
только он. Список обновляется сам, когда накопитель подключают или отключают; кнопка «Копировать», двойной щелчок
или Ctrl+C копирует номер. Права администратора и интернет не нужны, программа ничего не отправляет и не меняет.

## Сборка

`tools\build.BAT yousbee` (или без аргументов — вместе с остальными) собирает `youSBee.exe` в корень.
Версия своя — `app/youSBee.version`, поднимает `tools\update-version.BAT yousbee`.
Окно, «О программе» (с языком и темой) и запуск общие с SUSpector — `app/src/Standalone.cs`; настройки — `youSBee.ini`
рядом с программой (тема и язык). Проверка вёрстки без показа окна:

```bat
youSBee.exe --theme light --language uk --screenshot yousbee.png 1500
```

Иконка — `app\yousbee.ico`, картинка «О программе» — `app\yousbee.png`; обе делает `tools\make-icon.BAT`
из `app\yousbee-source.png` (пчела-флешка на фиолетовой плашке; чёрный фон вокруг вырезается).
Чтобы поменять — заменить `app\yousbee-source.png`, запустить `tools\make-icon.BAT`, затем `tools\build.BAT yousbee`
и скопировать `app\yousbee.png` в `chagear-site/public/img/`.

## Распространение

Как HashPot: страница https://chagear.top/yousbee/ и ссылка https://chagear.top/download/youSBee.exe. Сборка сайта
(`chagear-site`, `npm run deploy`) копирует собранный `youSBee.exe` и берёт версию из `app/youSBee.version`.
Новая версия: `update-version.BAT yousbee`, `build.BAT yousbee`, коммит, затем заново опубликовать сайт.
Кнопки «Обновление» в программе нет.
