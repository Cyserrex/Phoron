using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Phoron.Core
{
    /// <summary>
    /// Menulis halaman beranda Phoron ke etc\dashboard\index.php.
    ///
    /// Ditaruh di etc\, BUKAN di folder proyek, dan dijangkau lewat Alias Apache.
    /// Folder proyek itu milik pengguna - bisa jadi www milik Laragon yang sudah
    /// punya index.php sendiri - dan menaruh berkas di sana berarti menimpa
    /// pekerjaan orang. Dengan alias, beranda ini selalu ada di /phoron apa pun
    /// isi folder proyeknya.
    ///
    /// Datanya disuntikkan saat penulisan, bukan dipindai ulang oleh PHP: yang
    /// perlu ditampilkan adalah apa yang BENAR-BENAR dikonfigurasi Phoron, bukan
    /// tebakan halaman itu sendiri terhadap isi folder.
    /// </summary>
    public static class Beranda
    {
        public static string Folder { get { return Path.Combine(Paths.Etc, "dashboard"); } }

        /// <summary>Jalur URL tetap; dipakai juga di berkas konfigurasi Apache dan Nginx.</summary>
        public const string Alias = "/phoron";

        public static void Tulis(Profile profile, List<Site> sites, BinPackage php,
                                 BinPackage web, BinPackage mysql,
                                 Func<Site, string> versiPhp = null)
        {
            Directory.CreateDirectory(Folder);
            ConfigWriter.WriteIfChanged(Path.Combine(Folder, "index.php"),
                Halaman(profile, sites, php, web, mysql, versiPhp));
        }

        /// <summary>
        /// Kutip aman untuk string PHP bertanda petik tunggal. Nama folder bisa
        /// memuat petik atau garis miring terbalik, dan tanpa ini satu nama saja
        /// cukup membuat seluruh halaman gagal diurai.
        /// </summary>
        static string Q(string s)
        {
            return "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }

        static string Halaman(Profile profile, List<Site> sites, BinPackage php,
                              BinPackage web, BinPackage mysql, Func<Site, string> versiPhp = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?php");
            sb.AppendLine("// Dibuat otomatis oleh Phoron " + AppInfo.Version + ".");
            sb.AppendLine("// Jangan disunting: berkas ini ditulis ulang tiap kali konfigurasi dibuat.");
            sb.AppendLine("$phoron = array(");
            sb.AppendLine("  'versi'   => " + Q(AppInfo.Version) + ",");
            sb.AppendLine("  'profil'  => " + Q(profile != null ? profile.Name : "-") + ",");
            sb.AppendLine("  'php'     => " + Q(php != null ? php.Version : "-") + ",");
            sb.AppendLine("  'web'     => " + Q(web != null ? web.Id : "-") + ",");
            sb.AppendLine("  'mysql'   => " + Q(mysql != null ? mysql.Version : "-") + ",");
            sb.AppendLine("  'dbport'  => " + (profile != null ? profile.MySqlPort : 3306) + ",");
            sb.AppendLine("  'akar'    => " + Q(SiteScanner.DocumentRoot(profile)) + ",");
            sb.AppendLine("  'situs'   => array(");
            foreach (var s in sites ?? new List<Site>())
            {
                var port = profile != null && profile.HttpPort != 80 ? ":" + profile.HttpPort : "";
                // Versi PHP yang BENAR-BENAR melayani situs ini - bisa berbeda
                // dari PHP profil bila situsnya memilih versi sendiri.
                var vPhp = versiPhp != null ? versiPhp(s) : (php != null ? php.Version : "");
                sb.AppendLine("    array('nama' => " + Q(s.Folder)
                              + ", 'url' => " + Q("http://" + s.HostName + port + "/")
                              + ", 'folder' => " + Q(s.Path)
                              + ", 'php' => " + Q(vPhp ?? "") + "),");
            }
            sb.AppendLine("  ),");
            sb.AppendLine(");");
            sb.AppendLine("$ext = get_loaded_extensions();");
            sb.AppendLine("sort($ext);");
            sb.AppendLine("?>");
            sb.Append(Template());
            return sb.ToString();
        }

        /// <summary>
        /// Bagian HTML-nya. Sengaja tanpa berkas CSS atau JS terpisah dan tanpa
        /// satu pun alamat luar: halaman ini harus tampil utuh di mesin yang
        /// sedang tidak tersambung internet.
        ///
        /// Semua sintaksnya harus sah di PHP 5.6 - tidak ada "??", tidak ada
        /// fungsi panah. Profil PHP 5.6 masih dipakai orang, dan halaman yang
        /// gagal diurai justru muncul sebagai layar putih.
        /// </summary>
        static string Template()
        {
            return
"<!doctype html>\n" +
"<html lang=\"id\">\n" +
"<head>\n" +
"<meta charset=\"utf-8\">\n" +
"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
"<title>Phoron</title>\n" +
"<style>\n" +
"  :root { color-scheme: light dark; --bg:#f4f5f7; --kartu:#fff; --teks:#1a1a1c;\n" +
"          --redup:#6b7280; --garis:#e5e7eb; --aksen:#2563eb; }\n" +
"  @media (prefers-color-scheme: dark) {\n" +
"    :root { --bg:#17171a; --kartu:#222226; --teks:#ececf0; --redup:#9ca3af;\n" +
"            --garis:#33333a; --aksen:#60a5fa; }\n" +
"  }\n" +
"  * { box-sizing: border-box; }\n" +
"  body { margin:0; padding:32px 20px; background:var(--bg); color:var(--teks);\n" +
"         font:15px/1.6 system-ui,'Segoe UI',sans-serif; }\n" +
"  .bungkus { max-width:960px; margin:0 auto; }\n" +
"  header { display:flex; align-items:baseline; gap:12px; margin-bottom:4px; }\n" +
"  h1 { font-size:30px; margin:0; letter-spacing:-.5px; }\n" +
"  .versi { color:var(--redup); font-size:13px; }\n" +
"  .sub { color:var(--redup); margin:0 0 26px; }\n" +
"  .kisi { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr));\n" +
"          gap:14px; margin-bottom:26px; }\n" +
"  .kartu { background:var(--kartu); border:1px solid var(--garis); border-radius:10px;\n" +
"           padding:14px 16px; }\n" +
"  .label { color:var(--redup); font-size:11px; text-transform:uppercase;\n" +
"           letter-spacing:.6px; margin-bottom:3px; }\n" +
"  .nilai { font-size:20px; font-weight:600; }\n" +
"  .kecil { color:var(--redup); font-size:12px; word-break:break-all; }\n" +
"  h2 { font-size:15px; margin:26px 0 10px; }\n" +
"  ul.situs { list-style:none; margin:0; padding:0; display:grid;\n" +
"             grid-template-columns:repeat(auto-fit,minmax(240px,1fr)); gap:10px; }\n" +
"  ul.situs li { background:var(--kartu); border:1px solid var(--garis);\n" +
"                border-radius:10px; padding:12px 14px; }\n" +
"  ul.situs a { color:var(--aksen); text-decoration:none; font-weight:600; }\n" +
"  ul.situs a:hover { text-decoration:underline; }\n" +
"  .kosong { background:var(--kartu); border:1px dashed var(--garis);\n" +
"            border-radius:10px; padding:18px; color:var(--redup); }\n" +
"  .ext { display:flex; flex-wrap:wrap; gap:6px; }\n" +
"  .ext span { background:var(--kartu); border:1px solid var(--garis);\n" +
"              border-radius:999px; padding:2px 10px; font-size:12px; }\n" +
"  footer { margin-top:32px; padding-top:16px; border-top:1px solid var(--garis);\n" +
"           color:var(--redup); font-size:12px; }\n" +
"  footer a { color:var(--aksen); }\n" +
"</style>\n" +
"</head>\n" +
"<body>\n" +
"<div class=\"bungkus\">\n" +
"  <header>\n" +
"    <h1>Phoron</h1>\n" +
"    <span class=\"versi\"><?= htmlspecialchars($phoron['versi']) ?></span>\n" +
"  </header>\n" +
"  <p class=\"sub\">Profil aktif: <strong><?= htmlspecialchars($phoron['profil']) ?></strong></p>\n" +
"\n" +
"  <div class=\"kisi\">\n" +
"    <div class=\"kartu\">\n" +
"      <div class=\"label\">PHP</div>\n" +
"      <div class=\"nilai\"><?= htmlspecialchars(PHP_VERSION) ?></div>\n" +
"      <div class=\"kecil\"><?= htmlspecialchars(php_sapi_name()) ?></div>\n" +
"    </div>\n" +
"    <div class=\"kartu\">\n" +
"      <div class=\"label\">Web server</div>\n" +
"      <div class=\"nilai\"><?= htmlspecialchars(isset($_SERVER['SERVER_SOFTWARE'])\n" +
"            ? preg_replace('/\\s.*$/', '', $_SERVER['SERVER_SOFTWARE']) : '-') ?></div>\n" +
"      <div class=\"kecil\"><?= htmlspecialchars($phoron['web']) ?></div>\n" +
"    </div>\n" +
"    <div class=\"kartu\">\n" +
"      <div class=\"label\">MySQL</div>\n" +
"      <div class=\"nilai\"><?= htmlspecialchars($phoron['mysql']) ?></div>\n" +
"      <div class=\"kecil\">port <?= (int)$phoron['dbport'] ?></div>\n" +
"    </div>\n" +
"    <div class=\"kartu\">\n" +
"      <div class=\"label\">Ekstensi</div>\n" +
"      <div class=\"nilai\"><?= count($ext) ?></div>\n" +
"      <div class=\"kecil\">termuat</div>\n" +
"    </div>\n" +
"  </div>\n" +
"\n" +
"  <h2>Situs</h2>\n" +
"  <?php if (count($phoron['situs'])): ?>\n" +
"    <ul class=\"situs\">\n" +
"      <?php foreach ($phoron['situs'] as $s): ?>\n" +
"        <li>\n" +
"          <a href=\"<?= htmlspecialchars($s['url']) ?>\"><?= htmlspecialchars($s['nama']) ?></a>\n" +
"          <div class=\"kecil\"><?php if ($s['php'] !== ''): ?>PHP <?= htmlspecialchars($s['php']) ?> &middot; <?php endif; ?><?= htmlspecialchars($s['folder']) ?></div>\n" +
"        </li>\n" +
"      <?php endforeach; ?>\n" +
"    </ul>\n" +
"  <?php else: ?>\n" +
"    <div class=\"kosong\">Belum ada subfolder proyek di\n" +
"      <code><?= htmlspecialchars($phoron['akar']) ?></code>.\n" +
"      Buat lewat halaman <strong>Situs</strong> di Phoron.</div>\n" +
"  <?php endif; ?>\n" +
"\n" +
"  <h2>Ekstensi PHP yang termuat</h2>\n" +
"  <div class=\"ext\">\n" +
"    <?php foreach ($ext as $e): ?><span><?= htmlspecialchars($e) ?></span><?php endforeach; ?>\n" +
"  </div>\n" +
"\n" +
"  <footer>\n" +
"    Akar utama <code><?= htmlspecialchars($phoron['akar']) ?></code> &middot;\n" +
"    <a href=\"?phpinfo=1\">phpinfo()</a> &middot;\n" +
"    <a href=\"https://github.com/Cyserrex/Phoron\">Phoron di GitHub</a>\n" +
"  </footer>\n" +
"</div>\n" +
"<?php if (isset($_GET['phpinfo'])) { phpinfo(); } ?>\n" +
"</body>\n" +
"</html>\n";
        }
    }
}
