# Phoron

**A local web development environment for Windows — Apache or Nginx, PHP, and
MySQL — where every site can run its own PHP version, and a complete stack is a
_profile_ you switch in one click.**

Keep a CodeIgniter 2 app on PHP 5.6 and a Laravel app on PHP 8.3 open side by
side: each site picks its PHP, and both run at the same time. Profiles hold the
rest of the environment — the web server and its version, the MySQL version,
ports, project folders. When you need a different database or server, pick the
profile and press **Switch**: every generated config file — `httpd.conf`,
`php.ini`, `my.ini`, virtual hosts, the Windows hosts file — is rewritten, and the
services restart.

![Home](docs/beranda.png)
![Profiles](docs/profil.png)

---

## Why Phoron

- **Whole stacks, not single versions.** A profile stores the PHP version, the
  web server and its version, the MySQL version, all three ports, the enabled
  PHP extensions, `php.ini` overrides, and the project folders. Keep as many
  profiles as you like and switch between them as a unit.
- **Different PHP versions per site, at the same time.** Keep a CodeIgniter 2 app
  on PHP 5.6 and a Laravel app on PHP 8.3 open side by side — no profile switch,
  no restart in between. Sites that pick their own version are served by that
  version's `php-cgi` over FastCGI; everything else keeps the profile's PHP.
- **Never picks a combination that can't start.** Phoron pairs PHP with an Apache
  built by the same compiler toolset (VC11, VC15, VS16…) and the same
  architecture. A VC11 PHP module inside a VS16 Apache — or 32-bit PHP from
  XAMPP inside 64-bit Apache — dies instantly with no useful message elsewhere.
  Here it simply isn't offered, and you are warned if you force it.
- **Plays nicely with what you already have.** Phoron **never writes inside a
  `bin` folder**. It scans its own `bin` *and* `C:\laragon\bin` (and any folder you
  add), and writes every generated file under its own `etc\`. You can share
  Laragon's PHP, Apache and MySQL builds without the two tools overwriting each
  other. When two folders share a name — the same PHP build in Phoron's and
  Laragon's `bin`, or the `php`/`mysql` folders of two XAMPP installs — every
  list shows where each one comes from, your choice is stored by full path, and
  each MySQL keeps its own data folder.
- **Fast by default, and still safe for coding.** OPcache is on out of the box —
  one CodeIgniter request went from **34.8 ms to 17.0 ms**. It's tuned so a file
  you save takes effect on the very next request, with no restart and no stale
  code. Idle in the tray, Phoron uses **0% CPU**.
- **Careful with your data.** MySQL is always shut down cleanly — on Stop, on
  exit, during updates and at Windows shutdown — so InnoDB never has to recover and
  MyISAM tables don't get corrupted. Uninstalling never deletes your projects or
  databases unless you explicitly say so.
- **Careful with your system.** Edits to the Windows hosts file stay inside a
  marked block, every change is backed up first, the original file is kept
  forever, and Phoron refuses to write at all if it couldn't read the file
  reliably.
- **Tells you what is actually wrong.** "Port 80 is used by httpd (PID 23972)"
  instead of Apache exiting with code 1. A service that dies after starting turns
  red with the reason. Config that changed under a running server gets a
  **Restart services** button instead of silently serving the old one.
- **No administrator rights needed** to install or run. Services are ordinary
  child processes, not Windows services, and they end when Phoron ends — no
  orphaned `httpd.exe` holding port 80.
- **More than PHP.** A **Node / TypeScript** page runs Next.js, Astro, Vite and
  other `package.json` projects from any folder. A **Databases** page manages
  MySQL, and the installer can bundle **HeidiSQL 12.21**, opened already connected
  to the active profile.
- **One 3 MB executable.** Every dependency is embedded. The installer is optional.
- **Verifiable releases.** Every release is built by GitHub Actions from a public
  commit, ships SHA-256 checksums, and carries a build-provenance attestation you
  can verify yourself.

## How it compares

|  | Phoron | Laragon | XAMPP | WampServer |
|---|---|---|---|---|
| Switch PHP version | yes | yes | reinstall | yes |
| Switch web server version together with PHP | **automatic, per profile** | separately | reinstall | separately |
| Different PHP versions per site, simultaneously | **yes** | – | – | yes (FCGI per VirtualHost) |
| Named, saved stack combinations | **unlimited profiles** | – | – | – |
| Different ports per stack | **yes** | one global setting | one global setting | one global setting |
| PHP extensions & `php.ini` overrides per stack | **yes** | per PHP folder | one `php.ini` | per PHP folder |
| Writes nothing inside version folders | **yes** | – | – | – |
| Runs without administrator rights | **yes** | yes | partly | – |

_Compared against default installations at the time of writing. If a cell is
wrong or out of date, please open an issue — this table should be accurate, not
flattering._

What Phoron does **not** try to be: a production server, a Docker replacement, or
a cross-platform tool. It is a Windows desktop app for local development.

---

## Installation

Download `Phoron-<version>-Setup.exe` from the
[Releases page](https://github.com/Cyserrex/Phoron/releases), or just take
`Phoron.exe` — a single file you can run from anywhere.

The installer offers two modes:

- **For all users** (needs admin) → installs to `C:\Phoron`.
- **Just for me** (no admin) → installs to your user folder.

It deliberately does **not** install to Program Files: Phoron's folder contains
`www\`, `data\` and `etc\`, which are written constantly, and Windows redirects or
blocks writes under Program Files.

The **Full** setup type includes HeidiSQL; **Compact** leaves it out.

Uninstalling does **not** delete your projects or databases — the uninstaller
asks separately, and a silent uninstall never removes them.

### "Windows protected your PC"

The first time you run the installer, Windows SmartScreen shows **"Windows
protected your PC"**. Click **More info**, then **Run anyway**.

This is not a malware finding. SmartScreen looks at two things: whether the file
is signed with a known publisher certificate, and whether many people have
already downloaded it. Phoron isn't code-signed (that requires a paid
certificate), and every release has a new file hash, so its reputation always
starts from zero.

You can check the file yourself instead of relying on SmartScreen:

```powershell
Get-FileHash Phoron-1.34.0-Setup.exe -Algorithm SHA256
```

Compare the result with `SHA256SUMS.txt` on the release. With the GitHub CLI you
can go further and prove where the file came from:

```powershell
gh attestation verify Phoron-1.34.0-Setup.exe --repo Cyserrex/Phoron
```

That answers the question that actually matters: was this file produced by
Phoron's build workflow, from a commit you can read — and not altered on the way
to you.

### Start with Windows

**Settings → Start Phoron when Windows starts** (or tick it during setup).
Phoron starts minimised to the tray with no window; if **Start services
automatically when Phoron opens** is also on, Apache and MySQL are ready before
you open anything.
It uses your user's `Run` registry key — not a Windows service or scheduled task,
both of which would need admin rights.

---

## Quick start

1. Run `Phoron.exe`.
2. On first launch Phoron scans its `bin` folders — its own **and**
   `C:\laragon\bin` if present — and creates one profile per PHP version, each
   already paired with a compatible Apache.
3. Choose a profile on the **Home** page and press **Start all**.
4. Open `http://localhost/`.

To change versions, pick another profile and press **Switch**. If services are
running, Phoron stops them, rewrites the configuration and starts them again.

---

## Features

### Automatic sites

Every subfolder of your project folders gets its own virtual host and name —
`www\shop` becomes `http://shop.test`. A `public/` folder containing `index.php`
(Laravel, Symfony) is used as the document root automatically.

Virtual hosts are **off by default**: `.test` names only work once they are in the
Windows hosts file, which needs administrator rights. Without them every project
still works at `http://localhost/project/`. Turn on **Settings → Virtual Host: give
every project folder its own address**, then
press **Register site names once** — the entries persist, so Phoron never needs
admin rights for this again.

### PHP version per site

In **Sites**, each site has a **PHP** box: **Follow profile**, or any PHP
version installed on the machine. A CodeIgniter 2 project can stay on PHP 5.6
while a Laravel project next to it runs on PHP 8.3 — both at
`http://localhost/<folder>/` and at `<folder>.test`, at the same time. **Terminal
here** puts that site's PHP first on `PATH`, so `php artisan` and
`composer` get PHP 8.3 even when the profile is on 5.6.

How it works: the profile's PHP keeps running as an Apache module. Each *other*
PHP version in use gets one `php-cgi` pool on its own port (one pool per version,
not per site), and each site that picked it gets a `<Directory>` block routing its
`.php` files there. The same works with Nginx.

What it costs, measured on the developer's machine: **no extra CPU** while idle
(0 ms of CPU time over 30 seconds), about **76 MB of RAM per extra PHP version**
(one supervisor and two workers), and about 0.3 ms more per request than
`mod_php`. If no site picks its own version, nothing is added at all — the
generated configuration is identical.

Things to know:

- A site on another version uses **that version's `php.ini`**, not the profile's.
  Its extension list comes from a profile that uses that PHP as its main version,
  or a sensible default. Hovering the PHP box shows which `php.ini` it is.
- `php_value` / `php_flag` lines in `.htaccess` are **ignored under FastCGI**.
  Phoron warns you when you move a site that has them; use `.user.ini` instead.
- The `Authorization` header is passed through (`CGIPassAuth On`), so APIs with
  Bearer tokens keep working.
- For Oracle, each pool gets an Instant Client whose version matches its `oci8`
  extension (`oci8_19` needs client 19 or newer) placed first on its `PATH`.
- **PHP 5.x serves one request at a time** in this mode. Its `php-cgi` ignores
  `PHP_FCGI_CHILDREN` on Windows and runs as a single process (PHP 7 and 8 start
  the two workers as expected). Pages still work; simultaneous requests to that
  site simply queue. It also means less RAM than the figure above.
- Every PHP build is listed with the `bin` folder it comes from, so two copies
  of the same version (in Phoron's and Laragon's `bin`) or two XAMPP `php`
  folders can be told apart — and the one you pick is the one that runs.
- 32-bit PHP works here too, even with a 64-bit Apache: the pool is a separate
  process.
- Needs Apache 2.4.26 or newer. On older Apache the choice is kept but not
  applied, and Phoron tells you why.

### Project folders anywhere

Under **Profile → Project folders**, list as many folders as you need:

```
C:\Phoron\www
C:\laragon\www
D:\work\client-a
```

All of them are scanned. The first one is the main root served at
`http://localhost`. Folders are per profile, so a PHP 5.6 profile can point at
legacy projects while a PHP 8.3 profile points at new ones. Duplicate project
names get a numeric suffix (`api.test`, `api-2.test`) and Phoron tells you which
ones collided — nothing is dropped silently.

### Home page at http://localhost/

A summary of the active profile, the PHP/Apache/MySQL versions, clickable sites,
and the PHP extensions that are *actually* loaded. It's always available at
`/phoron/` too.

Only the exact root URL is redirected: `http://localhost/myproject/` and
`http://localhost/index.php` still belong to your project. The page lives in
`etc\dashboard\`, never in your project folders. Turn off **Settings → Show the
Phoron home page at http://localhost/** if your web root is an application of its
own.

### HTTPS

A wildcard certificate for `*.test` and `localhost` is created automatically with
the `openssl.exe` that ships with Apache. Browsers warn until it is trusted —
**Home → Trust SSL certificate** installs it into the Windows Trusted Root
store (admin, once). Chrome and Edge follow Windows immediately; Firefox needs
`security.enterprise_roots.enabled = true` in `about:config`.

If no certificate exists, the HTTPS port is not opened at all — a closed port is
less confusing than one that always fails — and the Home page says so.

**Watch out for HSTS.** If `localhost` ever received a
`Strict-Transport-Security` header (common after opening another project over
https), browsers force https for it *and hide the "add exception" button*. Trust
the certificate, forget the site in your browser history, or use your project's
`.test` name.

### The hosts file is treated as someone else's file

Because it is. Phoron writes only inside its own marked block:

```
# === Phoron mulai ===
127.0.0.1	shop.test
::1	shop.test
# === Phoron selesai ===
```

- The previous state is copied to `data\hosts-backup\` before every write. The
  very first copy, `hosts-asli.bak`, is **never deleted** — it is the state before
  Phoron touched anything. The ten most recent dated copies are kept too, and both
  can be restored from **Settings → Restore hosts file...**.
- If the hosts file can't be **read** — locked by antivirus, for example — Phoron
  refuses to write it. Writing after a failed read would throw away every line you
  own.
- Before saving, every line outside Phoron's block is compared before and after;
  if any would disappear, the write is cancelled.
- If nothing would change, the file isn't touched at all.

### Databases

The **Databases** page lists your databases with their size and table count, and
creates, drops, imports and exports them (`.sql`). One button opens **HeidiSQL**,
already connected to the active profile's MySQL. The password, if you set one, is
stored encrypted with Windows DPAPI and never passed on a command line.

### Node / TypeScript

Run Next.js, Astro, Vite and other Node projects from any folder — not just
`www`. Scripts are read from `package.json`, the URL is picked up from the dev
server's own output (ports shift when taken, so guessing would be wrong), and the
whole process tree stops when Phoron closes.

### PHP extensions

Tick extensions per profile, plus the `php.ini` settings people change most. **Test:
php -m** shows what really loads. New profiles start with the extensions that were
already active in that PHP's own `php.ini`, or a sensible default set
(curl, fileinfo, openssl, mbstring, exif, intl, gd, mysqli, pdo_mysql, pdo_sqlite,
sqlite3, zip) filtered to the DLLs the build actually contains.

### When something goes wrong

- Unexpected errors write `logs\crash-<date>.log` with the version, machine state
  and the **last 40 lines of the Activity panel** — which is usually what explains
  what Phoron was doing at the time. UI errors don't close the app: it is holding
  your running Apache and MySQL.
- `logs\phoron.log` rotates at 2 MB with three generations.
- The **Logs** page tails Apache, MySQL, PHP and Phoron logs live.

### Pages

- **Home** — choose a profile, **Switch & Run**, start/stop, shortcuts (www,
  localhost, a terminal with the profile's PHP on `PATH`, `phpinfo()`, Apache config
  test, SSL certificate).
- **Profiles** — versions, ports, project folders, site suffix.
- **Versions** — everything installed; add bin folders; download new PHP builds
  straight from windows.php.net, or Apache/MySQL/Nginx from `etc\catalog.ini`.
- **Sites** — sites, PHP version per site, new project, hosts & vhost status.
- **Databases** — see above.
- **Node / TS** — see above.
- **PHP extensions** — per-profile extensions and common `php.ini` settings.
- **Logs** — live log viewer.
- **Settings** — theme, language, bin folders, terminal, autostart, tray,
  OPcache, logging, updates.

---

## Speed

Measured on the developer's machine (PHP 5.6, Apache 2.4.38) while these
improvements were made, between Phoron 1.28 and 1.34:

| | |
|---|---|
| Phoron idle in the tray, 30 s | **0% CPU**, 0 disk operations |
| Apache, static file | **1.0 ms** |
| CodeIgniter page, OPcache off → on | **34.8 ms → 17.0 ms** |
| 10 simultaneous 1-second PHP requests over FastCGI, 1 worker → 4 workers | **10.1 s → 3.0 s** |
| Activity panel receiving 300 log lines in a burst | **12.2 s → 0.5 s**, 300 → 21 redraws |

OPcache runs with `validate_timestamps=1` and `revalidate_freq=0`. PHP's default
re-checks files at most every two seconds, which is where "OPcache broke my dev
setup" comes from: you save, refresh, and see the old code. With these settings a
saved file is picked up on the next request. You can switch OPcache off in
**Settings**, and a profile can still override any `opcache.*` value.

---

## Folder layout

```
C:\Phoron\
  Phoron.exe          single file, every DLL embedded
  phoron.ini          global settings (also marks the install root)
  bin\                Phoron's own versions (php\, apache\, mysql\, nginx\, heidisql\)
  profiles\*.ini      one file per profile, easy to edit by hand
  www\                projects
  etc\                ALL generated configuration
    apache2\httpd.conf, mod_php.conf, ssl.conf, sites-enabled\*.conf
    php\<version>\php.ini
    mysql\my.ini
    ssl\phoron.crt, phoron.key
    dashboard\        the http://localhost/ home page
    catalog.ini       download list, extendable
  data\<version>\     MySQL data, separate per version
  logs\               apache-error, mysql-error, php-error, phoron
  tmp\
```

A profile is a plain INI file:

```ini
[profil]
nama=PHP 8.3 + Apache 2.4.57
web_server=apache
php=php-8.3.12-Win32-vs16-x64
apache=httpd-2.4.57-win64-VS16
mysql=mysql-5.7.38-winx64
port_http=80
port_https=443
port_mysql=3306
folder_proyek=C:\Phoron\www;C:\laragon\www

[php]
ekstensi=curl,mbstring,openssl,pdo_mysql,gd

[php.ini]
memory_limit=512M
```

A profile moved to another PC keeps the versions it names even if that PC
doesn't have them; Phoron uses the closest available build, says so on the Home
page, and leaves the file unchanged.

---

## Languages and themes

The interface is available in **Bahasa Indonesia** (the original), **English**,
**Basa Jawa** and **Bahasa Banjar**. The installer asks first, and Phoron starts in
the language you picked. Language names are never translated — people look for
"English" or "Basa Jawa", not a translation they can't recognise if they picked
the wrong one.

The theme follows Windows, or can be forced to Light or Dark.

All interface text is translated. Some dialog messages and log lines are still in
Indonesian.

---

## Technical notes

**`php.ini` starts from the one already there, not the vendor default.** Order:
`php.ini.sebelum-phoron` → `php.ini` → `php.ini-development` →
`php.ini-production`. PHP folders are often borrowed from a tool that has been tuned
for years; starting from the vendor default silently flips settings like
`short_open_tag` back to `Off`, and working projects break with errors that point
nowhere near Phoron. Profile overrides always win on top.

**`php.ini` is written to `etc\php\<version>\`, not into the PHP folder.** Apache's
`PHPIniDir` and `PHPRC` point there, so two tools never fight over one file. If you
want `php.exe` outside Phoron (your editor, Composer) to use the same settings,
enable **Settings → Write php.ini into the PHP folder**; the original is backed up
once to `php.ini.sebelum-phoron`.

**`httpd.conf` is built from the pristine copy** (`conf\original\httpd.conf`), not a
`conf\httpd.conf` another tool may have edited. Only `SRVROOT` and `Listen` change;
everything else is appended as a block at the end, since later Apache directives
override earlier ones.

**`localhost` has its own virtual host.** Apache answers unmatched requests with the
*first* virtual host. Without a guard, `http://localhost` would be served by
whichever site sorts first alphabetically — a symptom that only appears after you
create your first site. Phoron always writes `sites-enabled\000-default.conf`.

**MySQL shutdown uses mysqld's own `MySQLShutdown<PID>` event** — the same path the
MySQL Windows service uses. It needs no password, so it keeps working after you set
a root password. `mysqladmin` is only a fallback for builds that don't create the
event, and a forced kill happens only if mysqld hasn't stopped after twelve seconds.

**Non-thread-safe PHP is served over FastCGI.** NTS builds have no Apache module, so
Phoron runs `php-cgi.exe` with four workers and points Apache at it through
`mod_proxy_fcgi`. Two Windows-specific details are needed or no `.php` file
opens at all. The FastCGI address must end in a slash, because Apache appends the
file path directly and Windows paths start with `C:`, not `/`. And
`SCRIPT_FILENAME` has to be rewritten via `ProxyFCGISetEnvIf`, taken from the path
Apache already mapped, so aliases such as `/phoron` work too.

**Starting and stopping can't collide.** A second Start while one is still running
reuses the first; a Stop that arrives mid-start cancels it and cleans up anything
it had already launched. A Windows shutdown that another app cancels leaves your
services running.

**Named objects work across privilege levels.** After "restart as Administrator",
clicking the taskbar icon starts a normal-privilege copy. Phoron's single-instance
mutex and "show window" event carry an explicit per-user ACL and a low integrity
label, so that copy can still bring the existing window forward instead of
failing.

**The installer closes Phoron politely, in layers.** First Restart Manager, which
Phoron answers by stopping Apache and MySQL. Then a named event,
`Phoron.KeluarSekarang`, that makes Phoron exit through its normal path. Only if
neither is answered does it force `taskkill`, and child processes are bound to a
Job Object so they end too.

**Updates come from the GitHub releases API**, not by scraping a page. Only
`*-Setup.exe` is downloaded, versions are compared numerically (so `1.10.0` is newer
than `1.9.0`), and the automatic check runs at most once every six hours because
the unauthenticated API allows 60 requests per hour per IP.

**Verbose logging is off by default.** Apache access logs and full service output
are only written when you ask for them — mysqld alone prints hundreds of lines on
every start. Error logs for Apache, MySQL and PHP are always on.

---

## Building from source

Requires the .NET SDK (the build targets .NET Framework 4.8, present on every
Windows 10/11).

```
build.bat              Release build -> dist\Phoron.exe (single file, ~3 MB)
build.bat run          Debug build, then run it
build.bat test         test harness (683 tests)
build.bat live         end-to-end: starts real Apache & MySQL
build.bat clean
build_installer.bat    exe + installer (needs Inno Setup 6; downloads HeidiSQL)
set_version.bat 1.2.0  bump the version in every file at once
```

**Releases are automatic.** Pushing to `main` with a new version number makes
GitHub Actions publish `v<version>` with the exe, the installer and checksums.
Existing tags are skipped, so an ordinary push never overwrites a release people
have already downloaded. Use `set_version.bat`; CI refuses to build if the version
differs between files.

The tests are deliberately not pure unit tests. `build.bat test` has the **real
`httpd.exe`** validate every generated `httpd.conf` (`httpd -t`, `httpd -S`), the
**real `nginx.exe`** validate every `nginx.conf`, and the **real `php.exe`** load every
generated `php.ini` (`php -m`, `php -l`). `build.bat live` goes further: it starts
Apache for every PHP + Apache combination available, fetches a PHP page over HTTP,
checks that the version answering is exactly the one the profile asked for, then
starts MySQL from an empty data folder and runs `SELECT VERSION()`.

HeidiSQL is not stored in the repository. `installer\ambil_heidisql.ps1` downloads a
pinned version and refuses it unless its SHA-256 matches. HeidiSQL is GPL-2.0 and
runs as a separate program; its licence and source location ship alongside it.

The application icon is generated by `assets\make_icon.ps1` from
`assets\phoron-logo.png`.

---

## Not yet

- Per-site certificates (today there is one wildcard certificate for `*.test`).
- Nginx is tested less extensively than Apache.
- Some dialog and log messages are Indonesian-only.
