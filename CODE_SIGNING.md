# Подпись программ

> Статус: план. Заявка в SignPath Foundation ещё не подана; программы пока не подписаны.

Неподписанные `comp-info.exe`, `SUSpector.exe` и `youSBee.exe` Windows SmartScreen останавливает окном
«Система Windows защитила ваш компьютер». Платный сертификат (OV/EV) и Azure Trusted Signing недоступны: бюджета нет,
а Trusted Signing не выдаётся частным лицам из Украины. Поэтому подписываем бесплатно, через программу для открытых
проектов: сервис [SignPath.io](https://about.signpath.io/), сертификат выдан на [SignPath Foundation](https://signpath.org/).
В свойствах файла издателем будет указан «SignPath Foundation», а не CHAGEAR.

## Правила подписи

Они же опубликованы в [README.md](README.md) (раздел «Code signing policy») — этого требует SignPath Foundation.

- Подписываются только программы, собранные GitHub Actions из этого открытого репозитория. Собранное на своём
  компьютере не подписывается никогда: SignPath проверяет, что файл пришёл из сборки на сервере GitHub по коммиту
  репозитория.
- Каждый выпуск вручную одобряет утверждающий (approver) в веб-интерфейсе SignPath; без одобрения подписи нет.
- Роли: коммиты и ревью — bobanko; одобрение выпусков — bobanko. Вход в GitHub и SignPath — только с двухфакторной
  проверкой.
- Программы ничего не отправляют в сеть без ведома пользователя; политика конфиденциальности — в README.

Нарушение правил (подпись чужого или собранного вручную файла, скрытое поведение программы) — повод отозвать
участие в программе, а с ним и подпись всех выпусков.

## Что проверяет SignPath

- **Лицензия** — открытая, одобренная OSI: [GPL-3.0-or-later](LICENSE). Закрытых частей в программах нет.
- **Сведения в файле.** `tools\build.BAT` записывает в каждый exe название продукта (`AssemblyProduct`:
  «CHAGEAR Toolbox», «SUSpector», «youSBee»), версию, компанию CHAGEAR и copyright. В настройках артефакта SignPath
  можно потребовать, чтобы название и версия совпадали с ожидаемыми, — тогда подписать «не тот» файл не получится.
- **Происхождение** — сборка на runner'е GitHub (`windows-latest`) из тега этого репозитория.

## Перед заявкой

1. Репозиторий открыт, в нём LICENSE и README с правилами подписи и политикой конфиденциальности — готово.
2. **youSBee собирать в GitHub Actions.** Сейчас `youSBee.exe` собирается локально и сайт chagear.top берёт его с
   диска ([YOUSBEE.md](YOUSBEE.md)), такой файл подписать нельзя. Нужен свой тег (например `yousbee-vВЕРСИЯ`) и выпуск:
   Actions собирает и подписывает exe, а сайт берёт уже подписанный (из облака, как SUSpector, или из GitHub Release).
3. Хотя бы один выпуск каждой программы уже опубликован (SignPath не принимает проекты без выпусков) — есть
   у Тулбокса и SUSpector, у youSBee есть на сайте.
4. Перечитать актуальные условия на [signpath.org](https://signpath.org/) — они могут меняться.

## Заявка

Форма — на [signpath.org](https://signpath.org/) (раздел для open source проектов). В заявке:

- ссылка на репозиторий и на страницы загрузки (chagear.top/toolbox, /suspector, /yousbee);
- что делают программы. Описать честно и прямо то, что может насторожить проверяющих: Тулбокс меняет сетевые
  настройки, имя компьютера и членство в домене (по команде администратора), SUSpector удаляет найденные документы
  и отключает синхронизацию OneDrive (только по нажатию пользователя). Ничего не делается скрыто или само;
- подписываемые файлы: три exe. Скрипты консольной версии (`*.ps1`) SignPath тоже умеет подписывать — можно
  добавить позже, если понадобится запуск при политике `AllSigned`.

## Подпись в GitHub Actions (после одобрения)

SignPath выдаёт идентификатор организации, slug проекта и политики подписи; API-токен кладётся в секрет репозитория
`SIGNPATH_API_TOKEN`. В [release.yml](.github/workflows/release.yml) подпись встаёт между сборкой и упаковкой:
`tools/build-release.ps1` и `tools/build-suspector-release.ps1` берут exe из корня, поэтому подписанные файлы
кладутся туда же до упаковки.

```yaml
      - name: Build the programs and run their checks
        shell: cmd
        run: tools\build.BAT

      - name: Upload the programs for signing
        id: unsigned
        uses: actions/upload-artifact@v4
        with:
          name: unsigned
          path: |
            comp-info.exe
            SUSpector.exe
            youSBee.exe

      - name: Sign with SignPath
        uses: signpath/github-action-submit-signing-request@v1
        with:
          api-token: ${{ secrets.SIGNPATH_API_TOKEN }}
          organization-id: '<из SignPath>'
          project-slug: '<из SignPath>'
          signing-policy-slug: 'release-signing'
          github-artifact-id: ${{ steps.unsigned.outputs.artifact-id }}
          wait-for-completion: true            # ждёт ручного одобрения выпуска
          output-artifact-directory: signed

      - name: Use the signed programs
        shell: pwsh
        run: Copy-Item signed/*.exe . -Force

      # дальше без изменений: Build the toolbox release / Build the SUSpector release / Publish to the cloud
```

Проверка после выпуска — свойства exe → «Цифровые подписи», или:

```powershell
Get-AuthenticodeSignature .\SUSpector.exe | Format-List Status, SignerCertificate
```

## Чего ждать от подписи

Подпись убирает «неизвестного издателя», но SmartScreen доверяет не сразу: у сертификата SignPath Foundation уже есть
репутация, и обычно окно пропадает быстро, но для нового файла предупреждение ещё может появляться, пока его не скачают
достаточно раз. Каждая новая версия подписывается тем же сертификатом, поэтому репутация переходит к ней.

Если заявку не примут — запасной вариант Certum Open Source Code Signing (недорогой, платный), только с согласия владельца.
