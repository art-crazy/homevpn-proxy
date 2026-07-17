# homevpn-proxy

Прокси на домашнем роутере, который заставляет трафик ChatGPT / Claude / Claude Code / Codex CLI / браузера идти через домашний VPN (ZeroBlock на OpenWrt) — даже при включённом корпоративном Check Point VPN, без изменения его настроек.

Механизм и обоснование — в [docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md).

## Установка на роутер

```sh
scp -r router/ root@192.168.2.1:/tmp/homevpn-proxy-install
ssh root@192.168.2.1 'sh /tmp/homevpn-proxy-install/install.sh'
```

Требует пакет `kmod-veth` — ставится сам через `opkg` при первом запуске (роутеру нужен интернет).

После установки прокси слушает на `192.168.2.250:2080` — один порт на SOCKS5 и HTTP CONNECT (sing-box сам определяет протокол).

Также ставится cron-задача (раз в 5 минут), которая реальным запросом через прокси проверяет, что он не просто "запущен", а действительно работает, и сама перезапускает сервис при тихом отказе — см. [docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md#самовосстановление).

**Откат:** `ssh root@192.168.2.1 'sh /tmp/homevpn-proxy-install/uninstall.sh'` — останавливает сервис и удаляет всё, что создал `install.sh`. Больше ничего на роутере не трогается.

## Настройка на Windows

### Трей-приложение (рекомендуется)

`windows/HomeVpnProxyTray/` — приложение в трее, включает/выключает всё одной кнопкой (переменные окружения + PAC), показывает статус: доступен ли прокси, подключён ли Check Point, какие домены сейчас туннелируются.

Есть также опциональные кнопки «Проверить» и «Починить» — по SSH делают на роутере ту же реальную проверку, что и cron. «Проверить» только показывает статус (зелёная/красная точка); «Починить» становится активной только после того, как «Проверить» показала проблему, и тогда перезапускает сервис на роутере. Требует один раз сохранить в приложении IP/логин/пароль роутера — пароль хранится в `%LOCALAPPDATA%\HomeVpnProxyTray\router.json` зашифрованным через Windows DPAPI (привязан к вашей учётке на этом ПК, не хранится в открытом виде и никуда не отправляется). Это добровольная функция — без сохранённых данных кнопки просто неактивны, остальной функционал приложения от неё не зависит.

Готовый exe: [Releases](https://github.com/art-crazy/homevpn-proxy/releases/latest) → положить в `%LOCALAPPDATA%\HomeVpnProxyTray\HomeVpnProxyTray.exe` → запустить → включить тумблер «Автозапуск с Windows». Нужен установленный [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (не self-contained — так меньше ест памяти в простое).

Сборка из исходников:

```powershell
cd windows/HomeVpnProxyTray
dotnet publish -c Release
New-Item -ItemType Directory -Force "$env:LOCALAPPDATA\HomeVpnProxyTray" | Out-Null
Copy-Item "bin\Release\net8.0-windows\win-x64\publish\HomeVpnProxyTray.exe" "$env:LOCALAPPDATA\HomeVpnProxyTray\" -Force
```

(В `%LOCALAPPDATA%`, а не в папку сборки — она стирается при пересборке, а автозапуск запоминает путь запуска.)

**Обновление:** закрыть приложение через меню в трее, повторить `dotnet publish` + `Copy-Item` — файл просто перезаписывается.

**Удаление:** выйти через меню в трее, снять тумблер автозапуска (или удалить значение `HomeVpnProxyTray` в `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), удалить `%LOCALAPPDATA%\HomeVpnProxyTray\`.

### Вручную, без приложения

```powershell
windows/set-proxy.ps1 / unset-proxy.ps1   # переменные окружения (CLI: Claude Code, Codex CLI, curl, git...)
windows/set-pac.ps1 / unset-pac.ps1       # PAC-скрипт (браузер, только claude/chatgpt-домены, остальное напрямую)
```

## Как добавить домен

Список доменов, которые реально уходят в VPN, задаётся в ZeroBlock: LuCI → ZeroBlock → профиль `opera` → список доменов.

PAC-файл для браузера (`windows/homevpn-proxy.pac`) — **отдельный** список, синхронизируется вручную: после правки залить его на роутер (`/www/homevpn-proxy.pac`), на стороне Windows ничего менять не нужно.

Сейчас в списке: `claude.ai`, `anthropic.com`, `claude.com`, `chatgpt.com`, `openai.com`, `oaistatic.com`, `oaiusercontent.com`.

## Известные ограничения

- Список доменов дублируется (ZeroBlock + PAC), синхронизация вручную.
