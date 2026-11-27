//! Fitur: halaman beranda Phoron di http://localhost/phoron (`etc\dashboard\index.php`).
//!
//! Daftar periksa:
//! - Ditaruh di etc\, BUKAN di folder proyek, dan dijangkau lewat Alias Apache /
//!   location nginx. Folder proyek milik pengguna (sering www Laragon yang sudah
//!   punya index.php sendiri); menaruh berkas di sana menimpa pekerjaan orang.
//! - Data disuntikkan saat penulisan, bukan dipindai ulang oleh PHP: yang tampil
//!   adalah apa yang BENAR-BENAR dikonfigurasi Phoron.
//! - Semua teks dari pengguna (nama folder, profil) dikutip aman untuk string
//!   PHP berpetik tunggal: satu nama berisi petik atau garis miring terbalik
//!   tidak boleh membuat seluruh halaman gagal diurai.
//! - Sintaks PHP-nya harus sah di PHP 5.6 (tanpa "??", tanpa fungsi panah):
//!   halaman yang gagal diurai muncul sebagai layar putih.
//! - Tanpa CSS/JS terpisah dan tanpa alamat luar: tampil utuh tanpa internet.
//! - Versi PHP per situs adalah versi yang BENAR-BENAR melayaninya (kolam
//!   FastCGI bila ada), bukan selalu PHP profil.
//! - Menulis beranda tidak menandai "perlu restart": PHP biasa berlaku pada
//!   permintaan berikutnya (ditangani `tulis::write_if_changed`).

use crate::model::{BinPackage, Profile, Site};
use crate::situs;
use crate::tulis::{beranda_folder, write_if_changed};

/// Tulis `index.php` beranda. `versi_php` memberi versi PHP yang melayani
/// sebuah situs; `None` = PHP profil untuk semua.
pub fn tulis(
    profile: Option<&Profile>,
    sites: &[Site],
    php: Option<&BinPackage>,
    web: Option<&BinPackage>,
    mysql: Option<&BinPackage>,
    versi_php: Option<&dyn Fn(&Site) -> String>,
) -> std::io::Result<bool> {
    let dir = beranda_folder();
    std::fs::create_dir_all(&dir)?;
    write_if_changed(&dir.join("index.php"), &halaman(profile, sites, php, web, mysql, versi_php))
}

/// Kutip aman untuk string PHP berpetik tunggal.
fn q(s: &str) -> String {
    format!("'{}'", s.replace('\\', "\\\\").replace('\'', "\\'"))
}

pub fn halaman(
    profile: Option<&Profile>,
    sites: &[Site],
    php: Option<&BinPackage>,
    web: Option<&BinPackage>,
    mysql: Option<&BinPackage>,
    versi_php: Option<&dyn Fn(&Site) -> String>,
) -> String {
    let v = crate::VERSI;
    let mut s = String::new();
    let mut baris = |x: &str| {
        s.push_str(x);
        s.push('\n');
    };
    baris("<?php");
    baris(&format!("// Dibuat otomatis oleh Phoron {v}."));
    baris("// Jangan disunting: berkas ini ditulis ulang tiap kali konfigurasi dibuat.");
    baris("$phoron = array(");
    baris(&format!("  'versi'   => {},", q(v)));
    baris(&format!("  'profil'  => {},", q(profile.map_or("-", |p| p.name.as_str()))));
    baris(&format!("  'php'     => {},", q(php.map_or("-", |p| p.version.as_str()))));
    baris(&format!("  'web'     => {},", q(web.map_or("-", |p| p.id.as_str()))));
    baris(&format!("  'mysql'   => {},", q(mysql.map_or("-", |p| p.version.as_str()))));
    baris(&format!("  'dbport'  => {},", profile.map_or(3306, |p| p.mysql_port)));
    baris(&format!("  'akar'    => {},", q(&situs::document_root(profile))));
    baris("  'situs'   => array(");
    let port = match profile {
        Some(p) if p.http_port != 80 => format!(":{}", p.http_port),
        _ => String::new(),
    };
    for st in sites {
        let v_php = match versi_php {
            Some(f) => f(st),
            None => php.map(|p| p.version.clone()).unwrap_or_default(),
        };
        baris(&format!(
            "    array('nama' => {}, 'url' => {}, 'folder' => {}, 'php' => {}),",
            q(&st.folder),
            q(&format!("http://{}{}/", st.host_name, port)),
            q(&st.path),
            q(&v_php)
        ));
    }
    baris("  ),");
    baris(");");
    baris("$ext = get_loaded_extensions();");
    baris("sort($ext);");
    baris("?>");
    s.push_str(TEMPLAT);
    s
}

/// Bagian HTML-nya (lihat daftar periksa: PHP 5.6, tanpa alamat luar).
const TEMPLAT: &str = r##"<!doctype html>
<html lang="id">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Phoron</title>
<style>
  :root { color-scheme: light dark; --bg:#f4f5f7; --kartu:#fff; --teks:#1a1a1c;
          --redup:#6b7280; --garis:#e5e7eb; --aksen:#2563eb; }
  @media (prefers-color-scheme: dark) {
    :root { --bg:#17171a; --kartu:#222226; --teks:#ececf0; --redup:#9ca3af;
            --garis:#33333a; --aksen:#60a5fa; }
  }
  * { box-sizing: border-box; }
  body { margin:0; padding:32px 20px; background:var(--bg); color:var(--teks);
         font:15px/1.6 system-ui,'Segoe UI',sans-serif; }
  .bungkus { max-width:960px; margin:0 auto; }
  header { display:flex; align-items:baseline; gap:12px; margin-bottom:4px; }
  h1 { font-size:30px; margin:0; letter-spacing:-.5px; }
  .versi { color:var(--redup); font-size:13px; }
  .sub { color:var(--redup); margin:0 0 26px; }
  .kisi { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr));
          gap:14px; margin-bottom:26px; }
  .kartu { background:var(--kartu); border:1px solid var(--garis); border-radius:10px;
           padding:14px 16px; }
  .label { color:var(--redup); font-size:11px; text-transform:uppercase;
           letter-spacing:.6px; margin-bottom:3px; }
  .nilai { font-size:20px; font-weight:600; }
  .kecil { color:var(--redup); font-size:12px; word-break:break-all; }
  h2 { font-size:15px; margin:26px 0 10px; }
  ul.situs { list-style:none; margin:0; padding:0; display:grid;
             grid-template-columns:repeat(auto-fit,minmax(240px,1fr)); gap:10px; }
  ul.situs li { background:var(--kartu); border:1px solid var(--garis);
                border-radius:10px; padding:12px 14px; }
  ul.situs a { color:var(--aksen); text-decoration:none; font-weight:600; }
  ul.situs a:hover { text-decoration:underline; }
  .kosong { background:var(--kartu); border:1px dashed var(--garis);
            border-radius:10px; padding:18px; color:var(--redup); }
  .ext { display:flex; flex-wrap:wrap; gap:6px; }
  .ext span { background:var(--kartu); border:1px solid var(--garis);
              border-radius:999px; padding:2px 10px; font-size:12px; }
  footer { margin-top:32px; padding-top:16px; border-top:1px solid var(--garis);
           color:var(--redup); font-size:12px; }
  footer a { color:var(--aksen); }
</style>
</head>
<body>
<div class="bungkus">
  <header>
    <h1>Phoron</h1>
    <span class="versi"><?= htmlspecialchars($phoron['versi']) ?></span>
  </header>
  <p class="sub">Profil aktif: <strong><?= htmlspecialchars($phoron['profil']) ?></strong></p>

  <div class="kisi">
    <div class="kartu">
      <div class="label">PHP</div>
      <div class="nilai"><?= htmlspecialchars(PHP_VERSION) ?></div>
      <div class="kecil"><?= htmlspecialchars(php_sapi_name()) ?></div>
    </div>
    <div class="kartu">
      <div class="label">Web server</div>
      <div class="nilai"><?= htmlspecialchars(isset($_SERVER['SERVER_SOFTWARE'])
            ? preg_replace('/\s.*$/', '', $_SERVER['SERVER_SOFTWARE']) : '-') ?></div>
      <div class="kecil"><?= htmlspecialchars($phoron['web']) ?></div>
    </div>
    <div class="kartu">
      <div class="label">MySQL</div>
      <div class="nilai"><?= htmlspecialchars($phoron['mysql']) ?></div>
      <div class="kecil">port <?= (int)$phoron['dbport'] ?></div>
    </div>
    <div class="kartu">
      <div class="label">Ekstensi</div>
      <div class="nilai"><?= count($ext) ?></div>
      <div class="kecil">termuat</div>
    </div>
  </div>

  <h2>Situs</h2>
  <?php if (count($phoron['situs'])): ?>
    <ul class="situs">
      <?php foreach ($phoron['situs'] as $s): ?>
        <li>
          <a href="<?= htmlspecialchars($s['url']) ?>"><?= htmlspecialchars($s['nama']) ?></a>
          <div class="kecil"><?php if ($s['php'] !== ''): ?>PHP <?= htmlspecialchars($s['php']) ?> &middot; <?php endif; ?><?= htmlspecialchars($s['folder']) ?></div>
        </li>
      <?php endforeach; ?>
    </ul>
  <?php else: ?>
    <div class="kosong">Belum ada subfolder proyek di
      <code><?= htmlspecialchars($phoron['akar']) ?></code>.
      Buat lewat halaman <strong>Situs</strong> di Phoron.</div>
  <?php endif; ?>

  <h2>Ekstensi PHP yang termuat</h2>
  <div class="ext">
    <?php foreach ($ext as $e): ?><span><?= htmlspecialchars($e) ?></span><?php endforeach; ?>
  </div>

  <footer>
    Akar utama <code><?= htmlspecialchars($phoron['akar']) ?></code> &middot;
    <a href="?phpinfo=1">phpinfo()</a> &middot;
    <a href="https://github.com/Cyserrex/Phoron">Phoron di GitHub</a>
  </footer>
</div>
<?php if (isset($_GET['phpinfo'])) { phpinfo(); } ?>
</body>
</html>
"##;

#[cfg(test)]
mod uji {
    use super::*;
    use crate::paths;

    #[test]
    fn nama_berpetik_dikutip_dan_versi_per_situs() {
        let (_g, d) = paths::sandbox_uji("beranda");
        let p = Profile { name: "Kopi's \\ Pagi".into(), http_port: 8080, ..Profile::default() };
        let site = Site {
            folder: "o'brien".into(),
            path: r"C:\www\o'brien".into(),
            host_name: "o-brien.test".into(),
            ..Site::default()
        };
        let php = BinPackage::baru(crate::model::BinKind::Php, "php-8", r"C:\php", "8.3.12");
        let f = |_: &Site| "5.6.40".to_string();
        let h = halaman(Some(&p), std::slice::from_ref(&site), Some(&php), None, None, Some(&f));
        assert!(h.contains(r"'profil'  => 'Kopi\'s \\ Pagi',"));
        assert!(h.contains(r"array('nama' => 'o\'brien', 'url' => 'http://o-brien.test:8080/', 'folder' => 'C:\\www\\o\'brien', 'php' => '5.6.40'),"));
        assert!(h.contains("'web'     => '-',"));
        assert!(h.ends_with("</html>\n"));
        // Sintaks PHP 7+ tidak boleh muncul.
        assert!(!h.contains("??") && !h.contains("fn("));

        // Ditulis ke etc\dashboard, tidak menandai config web berubah.
        let v0 = crate::tulis::versi_konfig_web();
        assert!(tulis(None, &[], None, None, None, None).unwrap());
        assert!(d.join(r"etc\dashboard\index.php").is_file());
        assert_eq!(crate::tulis::versi_konfig_web(), v0);
        assert!(!tulis(None, &[], None, None, None, None).unwrap());
        let _ = std::fs::remove_dir_all(&d);
    }
}
