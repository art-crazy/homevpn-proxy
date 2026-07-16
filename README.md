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

**Откат:** `ssh root@192.168.2.1 'sh /tmp/homevpn-proxy-install/uninstall.sh'` — останавливает сервис и удаляет всё, что создал `install.sh`. Больше ничего на роутере не трогается.

## Настройка на Windows

### Трей-приложение (рекомендуется)

`windows/HomeVpnProxyTray/` — приложение в трее, включает/выключает всё одной кнопкой (переменные окружения + PAC), показывает статус: доступен ли прокси, подключён ли Check Point, какие домены сейчас туннелируются.

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

- Список доменов дублируется (ZeroBlock + PAC), синхронизация вручную — осознанно, чтобы не хранить пароль роутера в Windows-приложении.
