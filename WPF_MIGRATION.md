# Перевод comp-info.exe на WPF — памятка

Опыт двух уже переведённых программ соседнего проекта `..\SupraWeed` (HashPot — 19.11.2026, SupraWeed — 20.11.2026).
Код оттуда можно читать и копировать (это отдельный репозиторий: файлы копируются, а не подключаются ссылкой).

## Что смотреть в ..\SupraWeed

| Файл | Что там |
|---|---|
| `src\SupraWeed\SupraWeed.csproj`, `src\HashPot\HashPot.csproj` | SDK-проект на `net481`, `UseWPF`, `LangVersion latest`, ядро подключено ссылками `<Compile Include="..\X.cs" Link=…>`, версия из своего `obj\Version.cs` (автогенерация версий выключена), `DebugType none`, `SatelliteResourceLanguages` |
| `build.bat` (метка `:dotnet` и вызовы `dotnet build`) | поиск .NET SDK (PATH, Program Files, `%LOCALAPPDATA%\Microsoft\dotnet`), `dotnet build -c Release -o obj\…-out`, копия exe в выходную папку; запущенный exe сначала переносится в `obj\*.previous.exe`, поэтому сборка работает при открытой программе |
| `src\Wpf\Styles.xaml` | палитра (кисти светлой и тёмной темы) и стили в духе WinUI 3: TextBox, Button, AccentButton, Link, Flyout, ScrollBar, ToolTip, таблица `Table` (ListView + GridView), `FocusFrame`, календарь `ThemedCalendar` |
| `src\Wpf\WpfTheme.cs` | смена темы подменой кистей `DynamicResource` (окна перекрашиваются на месте, без пересоздания), «как в Windows» — реестр + `SystemEvents.UserPreferenceChanged`, тёмный заголовок через DWM |
| `src\Wpf\Dialog.cs` | замена MessageBox (вид ContentDialog WinUI): `Dialog.Show` / `Dialog.Ask` |
| `src\Wpf\InfoBar.cs` | сообщения как InfoBar WinUI 3: Info/Success/Warning/Error, кнопки-действия |
| `src\Wpf\AboutWindow.*` | «О программе»: картинка, переключатель темы, разделы со значками, `AboutWindow.Keep` (неразрывные пробелы, «висячие» слова) |
| `src\Wpf\SearchBox.cs`, `ChipBox.cs`, `DateBox.cs` | список с поиском, фишки, поле даты с календарём |
| `src\SupraWeed\MainWindow.*`, `DirectoryWindow.cs`, `RecordWindow.cs` | пример главного окна, таблицы-справочника и формы записи |
| `src\SupraWeed\ShellIcons.cs` | значки файлов от Windows (SHGetFileInfo) для WPF |
| `CLAUDE.md` (разделы про HashPot и «Перенос SupraWeed на WPF») | подробная история решений |

## Решения, которые уже приняты

- **Технология:** C# + WPF на .NET Framework 4.8.1 (есть в Windows 11, ничего не устанавливать на целевых ПК), сборка .NET SDK
  (на компьютере сборки SDK ставить можно), свежий C# в окнах, **без сторонних пакетов**. Пробовали и отклонили:
  библиотека WPF UI (+4 МБ, ~160 МБ памяти), WinUI 3 (13–37 МБ, распаковка в %TEMP%, запуск ~2 с).
- **Ядро остаётся на C# 5** и собирается обоими компиляторами (встроенный `csc` для консольных/старых exe и тестов, SDK для окна).
  В ядре: без `$"..."`, `?.`, `=>`-членов, `out var`, кортежей.
- **Переход параллельный:** рядом собирается отдельный `*-wpf.exe`, разделы переносятся по одному; когда WPF догоняет,
  он занимает имя основного exe, WinForms-код удаляется, `build` удаляет оставшийся `*-wpf.exe`.
- Вид — WinUI 3 / Fluent, но своими стилями. Значки только из шрифта Segoe Fluent Icons (запасной — Segoe MDL2 Assets), своих Path нет.

## Подводные камни (уже наступали)

- **Фокус:** только `FocusVisualStyle` (рамка 2 px снаружи) — видна лишь при переходе с клавиатуры. Триггеры
  `IsKeyboardFocused` в стилях не использовать: срабатывают и после клика мышью.
- **Кнопки отмены:** `IsCancel` в WPF отвечает только на Esc — закрывать окно своим Click.
- **Запуск:** до показа главного окна `ShutdownMode=OnExplicitShutdown`, иначе первое окно сообщения станет «главным»
  и его закрытие завершит программу.
- **MessageBox** Windows в WPF-окнах не использовать (светлый, не в стиле) — только `Dialog`.
- **Выпадающие списки:** в comp-info — обычный `ComboBox` со своим стилем (`Styles.xaml`: `ComboItem`, `Flyout`); у самодельных списков SupraWeed пункт выбирать по `PreviewMouseLeftButtonDown` (иначе ListBoxItem забирает фокус и поле закрывает
  список до отпускания кнопки); клик в окне вне поля закрывает список.
- **Высота окна по содержимому:** после смены текста нужен `UpdateLayout()`, иначе Measure корня отдаёт старый размер.
- **Перетаскивание:** пелена-подсказка анимирует Opacity только при включении/выключении; перезапуск анимации
  на каждый DragOver даёт «лаг».
- **Подпись поля:** клик по подписи ставит курсор в её поле (как `<label>` в вебе).
- **Выбор папки:** в WPF на .NET Framework своего диалога нет — `UseWindowsForms` в csproj ради `FolderBrowserDialog`.
- **Снимки окна для проверки:** сразу после запуска при `SizeToContent` PrintWindow даёт белое окно — снимать позже;
  при масштабе 125 % скрипту нужен `SetProcessDPIAware`, иначе снимок обрезан. Щелчок через PostMessage WPF не принимает —
  проверять событием внутри процесса.
- Пиксельные правила раскладки WinForms (подгонка высот, сдвиги на 1 px) в WPF не нужны.

## Что в comp-info иначе, чем в SupraWeed

- **Акцент синий** и палитра повторяют веб (`cloud/public/app.css`) — палитру в `Styles.xaml` взять из `Theme.Apply`,
  а не зелёную из SupraWeed. Цвет «инфо» в InfoBar при синем акценте стоит проверить на путаницу с акцентом.
- **Три языка** (`L.T` / `L.F`, `Strings.cs`, проверка переводов при сборке): в XAML тексты не вшивать —
  задавать из кода через `L.T(...)` или сделать markup-расширение поверх `L.T`; проверка переводов должна видеть и новые строки.
- **Смена темы/языка:** сейчас окно строится заново; в WPF тема меняется на месте (DynamicResource), язык — можно
  оставить пересоздание окна.
- **Меню разделов** (`Ctrl+1…9`, `Ctrl+Tab`), значок щита у разделов с правами администратора, повышение прав с передачей
  формы через именованный канал — перенести без изменения логики (она в ядре/`SystemChanges`).
- **`--screenshot <раздел> <файл.png>`** — сохранить и в WPF-версии (RenderTargetBitmap), иначе нечем проверять вёрстку.
- Сборка сейчас не требует ничего, кроме встроенного `csc`; после перехода `tools\build.BAT` будет искать .NET SDK
  (пример — метка `:dotnet` в `..\SupraWeed\build.bat`). Тесты `app\tests` продолжают собираться встроенным `csc`.
- `SUSpector.exe` и `youSBee.exe` собираются из того же кода. Решение (20.11.2026): на WPF переводятся все программы.

## Ход перевода

Порядок: youSBee, затем SUSpector, затем comp-info.exe. Каждая программа идёт параллельно: рядом собирается `*-wpf.exe`.

**comp-info — все 12 разделов перенесены (20.11.2026), осталась замена exe.** `comp-info-wpf.exe` из `app\wpf\comp-info`:
`ToolboxWindow.cs` — главное окно (бренд радужными буквами с шестерёнкой и режимом для дальтоников по щелчку, меню со щитом
и глобусом, заголовок и описание раздела, Ctrl+1…9 / Ctrl+0 / Ctrl+Tab / Ctrl+PageUp/PageDown, остальные клавиши —
открытому разделу, WM_DEVICECHANGE — ему же), `App.xaml.cs` — запуск (`comp-info.ini` через `Settings` из `Lang.cs`:
тема, язык, `colorblind`, `securityLayout`), `--page`, `--screenshot <раздел> <файл.png> [мс]` как у comp-info.exe,
пересборка окна на том же разделе после смены языка или режима для дальтоников; тема меняется на месте.
Раздел — `Section` (ключ, названия, значок, щит/глобус) с ленивой страницей; страница реализует `ISection`
(`Shared\Section.cs`: `Opened`, `Shortcut`, `CanLeave`, `DevicesChanged`).

| Раздел | Состояние |
|---|---|
| Быстрый ping | готов (`Pages\PingView`) |
| Кибербезопасность | готов (`Pages\SecurityView`, общий с SUSpector) |
| USB-накопители | готов (`Pages\UsbView`, общий с youSBee) |
| О программе | готов (`ToolboxAbout` в `ToolboxWindow.cs`; содержимое — `Shared\AboutWindow.cs` → `AboutContent`, общее с отдельными программами) |
| Поиск | готов (`Pages\SearchView`; папка — `FolderBrowserDialog` WinForms, ссылка на `System.Windows.Forms`). Проверено: поиск по именам на всех дисках — 211 тыс. файлов за 25 с |
| Обновление | готов (`Pages\UpdateView`; ключ облака — `Pages\Cloud.cs`: `CloudConnect`, окно `KeyWindow`). Проверено: «Проверить обновления» с ключом из `cloud-key.txt`. Перезапуск запускает тот же exe с `--page update` |
| База устройств | готов (`Pages\DatabaseView`, карточка — `Pages\RecordWindow.cs`, подсветка найденного — `Pages\Marked.cs`). Логика вынесена из `DatabasePage.cs` в ядро `app\src\DeviceList.cs` (повторы, фильтры, поиск, порядок, политика имён, поля карточки, сохранение комментария; тест `device list`). Ширины колонок считает `FitColumns` по измеренному тексту: в узком окне уступают серийный номер, пользователь, ESET, облако, имя; IP целиком; заголовки получают оставшееся место. `--screenshot database` сохраняет и `-card.png`. Проверено: поиск «десктоп 4бн» → 5 из 91 с подсветкой, сортировка по IP |
| Сбор информации | готов (`Pages\CollectView.cs`; чтение и сохранение — `Collection` из `Collector.cs`). Как у comp-info.exe, запись сохраняется сама: при запуске в фоне на любом разделе (`ToolboxWindow` → `StartInBackground`), при каждом открытии раздела и через секунду после правки комментария; в режиме снимков не пишет (`AutoSave`). «Синхронизировать» открывает раздел синхронизации (`ToolboxWindow.OpenSync`, `SyncStarter`). Проверено на копии базы (`COMPINFO_ROOT`): комментарий сохраняется, статус облака меняется на «есть изменения» |
| Синхронизация | готов (`Pages\SyncView.cs`: страница, `SyncQuestions` — вопросы `CloudSync` из фонового потока в окна, `ConflictWindow` — конфликт по полям; «запись удалена в облаке» — `Dialog.Choose` с тремя ответами). `--screenshot sync` сохраняет и `-conflict.png`, `-key.png`, `-ask.png`. Проверено на тестовом облаке (`obj\tests.exe --fake-cloud <папка>` + `COMPINFO_ROOT=<папка>`): конфликт, восстановление удалённой в облаке записи, новая запись из облака |
| Настройка сети | готов (`Pages\LanView.cs`). Как у comp-info.exe, права администратора запрашивает только сама команда (`LanSetup.Apply` запускает Windows PowerShell с повышением), перезапуск программы не нужен. Списки сегментов и корпусов — отдельная сетка, делящая с адресом только ширины первых колонок (длинные названия корпусов не раздвигают октеты). Проверено: форма читается с адаптера («уже настроен так»), «Применить» → вопрос → «Отмена» |
| Имя компьютера, Домен | готов (`Pages\ComputerView.cs`, форма — `Pages\SystemForms.cs`: `FormGrid`, `AdminGlyph`, `Choice`). Из `MainForm.cs`/`SystemPages.cs` в ядро `app\src\ComputerForms.cs` вынесены `AdminHandoff` (путь exe — `Process.MainModule`, а не WinForms), `DomainAccount` (запомненная доменная учётная запись) и `RecordUpdate`. Перезапуск с правами и передача формы — `ToolboxWindow.RestartAsAdministrator`, приём — `--handoff` в `App.xaml.cs` и `Resumable.Resume` раздела. Проверено: формы обоих разделов и ввода в домен (`COMPINFO_UI_PREVIEW=1`), «тест 5» → `IF-042-TEST-5`, «Переименовать» → вопрос → «Назад». Само изменение и перезапуск через UAC вживую не запускались |

Проверять разделы, которые пишут в `data`, лучше на копии: `COMPINFO_ROOT=<папка с data, cloud-state.json, cloud-config.json>`;
синхронизацию — на тестовом облаке: `obj\tests.exe --fake-cloud <папка>` готовит в папке копию с конфликтом, записью,
удалённой в облаке, и новой записью в облаке, и держит облако на localhost, пока в папке не появится файл `stop`.
Снимки окон захватом экрана (CopyFromScreen) из скрипта UI Automation бывают белыми (так вышло у окон синхронизации; с выводом окна вперёд через SetForegroundWindow обычно получается): вёрстку надёжнее проверять
через `--screenshot` (RenderTargetBitmap).
Проверено: снимки всех разделов, обе темы, режим для дальтоников; через UI Automation — `--page`,
переход по меню, пересборка на английском и обратно на том же разделе, закрытие.

Замена exe (по слову пользователя, для каждой программы отдельно): `*-wpf.exe` становится `*.exe`, WinForms-код этой
программы удаляется, `build.BAT` собирает её только через `:wpf` и удаляет оставшийся `*-wpf.exe`. У comp-info.exe ещё:
архив обновления (`tools\build-release.ps1` берёт `comp-info.exe` из корня — после замены это WPF-версия, сборка выпуска
потребует .NET SDK), проверка подписи SignPath (имя продукта и версия — из того же `obj\Version.cs`) и снимки разделов
в документации. До замены стоит вживую проверить перезапуск с правами и передачу формы (UAC), а также «Применить»
на тестовом компьютере.

**SUSpector — шаги 1–6 готовы (20.11.2026), осталась замена exe.** `SUSpector-wpf.exe` из `app\wpf\SUSpector`
(`App.xaml.cs` — окно `SuspectorWindow`, `UpdateWindow.cs` — «Обновление»), страница `app\wpf\Pages\SecurityView`
(+ `Pages\Files.cs`: значки файлов Windows и метки-«таблетки»). Логика найденных файлов вынесена из `SecurityPage.cs`
в ядро `app\src\SecurityFiles.cs` (там же разбор меток `FileChips.Parse` и их цвет `FileChips.ColorKey`; WinForms-часть
`FileChips` — `partial`, только рисование). `StandaloneWindow` умеет кнопки внизу (у SUSpector своя панель сверху)
и `CanLeave` (окно не закрывается и не пересобирается, пока идёт проверка). В `build.BAT` WPF-сборка — подпрограмма
`:wpf <имя> <версия>`. Проверено: снимки двух раскладок и тем, `--demo`; настоящая проверка через UI Automation
(без документов — 9 проверок за 2 с, с поиском документов — 46 с на этом компьютере), «Проверить обновления» сверяет версию с сайтом, переключение раскладки пишется в `SUSpector.ini`.
Установка обновления вживую не проверялась: опубликованная версия не новее.

**youSBee — шаги 1–6 готовы (20.11.2026), осталась замена exe (шаг 7).** `tools\build.BAT yousbee` собирает рядом
с WinForms-версией `youSBee-wpf.exe`. Если .NET SDK не найден, WPF-сборка пропускается, остальное собирается как раньше.

- **Папка `app\wpf\`** (название временное, после перехода можно переименовать). `Directory.Build.props` уводит
  всё, что оставляет сборка, в корневой `obj\wpf\<папка>\`; в `app\wpf` только исходники.
  - `Shared\` — общее для WPF-программ: `Styles.xaml` (стили SupraWeed с палитрой веба, скругление 6, без календаря и фишек;
    новые `IconButton`, `Segmented`/`Segment`, `TableFrame`/`TableHeader`, `BareListBox`), `WpfTheme.cs` (палитра
    `Theme.Apply` + `Flash`, `Honey`, `*Line` для панелей строки состояния), `Dialog.cs`, `InfoBar.cs`, `StatusLine.cs`,
    `Brand.cs` (`Glyph` — значки шрифта, `Brand.Title` — имя с цветной частью в `[...]`, `Brand.Keep` — неразрывные
    пробелы), `AboutWindow.cs` (как «О программе» WinForms: картинка, язык и тема под ней), `Standalone.cs`
    (`StandaloneWindow` — шапка с кнопками справа, `StandaloneStart` — запуск, место окна, снимки, пересборка
    при смене языка).
  - `Pages\UsbView.xaml` — раздел «USB-накопители», потом войдёт и в WPF-версию comp-info.
  - `youSBee\` — `youSBee.csproj`, `App.xaml(.cs)` с окном `YouSBeeWindow`, `app.manifest` (PerMonitorV2).
- **Ядро:** `IniSettings` вынесен из `Standalone.cs` в `app\src\IniSettings.cs`. Логика списка флешек
  (`UsbList`, `UsbEntry`: ключ, «отключена», «только что подключена», «Обновить») — в `app\src\UsbList.cs`,
  общая для WinForms `UsbPage`, WPF `UsbView` и теста `USB drive list`.
- **Проект:** `net481` (как у SupraWeed, справочные сборки уже есть), но атрибут целевой платформы, как и
  название и версия, берётся из `obj\YouSBeeVersion.cs` (4.8: без режима совместимости 4.0; `GenerateAssemblyInfo`
  и `GenerateTargetFrameworkAttribute` выключены). Ссылка на `System.Web` нужна только компилятору XAML:
  он загружает её ради `System.Web.Extensions` (`Json.cs`).
- **Переводы:** проверка `translations cover the interface` смотрит `app\src` и `app\wpf` со всеми подпапками,
  а кириллица в `.xaml` вне комментариев считается ошибкой: все тексты задаются из кода через `L.T`.
- **Место окна** в ini хранится в пикселях экрана, как у WinForms (`GetWindowRect` / `SetWindowPos`): на 125 %
  WPF-версия открывается ровно там, где закрылась WinForms-версия, и наоборот.
- **Снимки:** `youSBee-wpf.exe --theme light|dark --language uk|en|ru --screenshot файл.png [мс]`; дополнительно
  `--about` (снимок «О программе»), `--width N` (узкое или широкое окно) и `--demo` (выдуманные флешки: скопированная,
  вынутая, без номера, только что подключённая). На снимке только клиентская часть окна, заголовок рисует Windows.
- **Проверка кнопок:** UI Automation (PowerShell, `System.Windows.Automation`) — кнопки со значком и текстом получают
  `AutomationProperties.Name` (`Glyph.Label`), заодно их читает экранный диктор. Скрипт PowerShell 5 с кириллицей
  нужно сохранять с BOM.
- **Цифры** (20.11.2026, тот же компьютер): окно через ~0,5 с у обеих версий; память 92 МБ (WPF) против 46 МБ (WinForms);
  exe 0,49 МБ у обеих.

Подводные камни, найденные на youSBee:
- `Icon` и `Clip` — свойства `Window`/`UIElement`: статические классы с такими именами внутри окна не видны
  (поэтому `Glyph` и `ClipText`).
- Внутри окна `Settings` — это `CompInfo.Settings` из `Lang.cs`, то есть `comp-info.ini` тулбокса. Настройки отдельной
  программы писать только через явное `App.Settings`.

Шаг 7 для youSBee: по слову пользователя `youSBee-wpf.exe` становится `youSBee.exe`, удаляются `YouSBee.cs`
и WinForms-сборка youSBee в `build.BAT` (`UsbPage.cs` и `Standalone.cs` остаются для comp-info и SUSpector),
`build.BAT` удаляет оставшийся `youSBee-wpf.exe`, затем новая версия и публикация сайта (YOUSBEE.md).
