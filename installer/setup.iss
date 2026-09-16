; ============================================================================
;  Phoron - skrip installer Inno Setup
;
;  Bangun dengan:  build_installer.bat        (dari folder induk)
;  atau manual  :  ISCC.exe installer\setup.iss
;
;  Syarat: dist\Phoron.exe sudah ada (jalankan build.bat dulu).
;
;  BAHASA. Keempat bahasa di bawah persis sama dengan yang didukung Phoron
;  sendiri (lihat Lang.cs), dan pilihan di layar pertama pemasang ikut
;  tersimpan ke phoron.ini - jadi aplikasinya menyala dalam bahasa yang tadi
;  dipilih, bukan memaksa orang menggantinya lagi di dalam.
;
;  Tidak ada berkas .isl terpisah: Inno Setup 6 tidak menyertakan terjemahan
;  Indonesia, Jawa, maupun Banjar, dan menyalin empat berkas berisi ratusan
;  pesan hanya untuk menerjemahkan sebagian kecilnya justru lebih rapuh.
;  Keempatnya memakai Default.isl sebagai dasar, lalu pesan yang BENAR-BENAR
;  dibaca orang ditimpa per bahasa di bagian [Messages]. Pesan yang tidak
;  ditimpa jatuh kembali ke bahasa Inggris - persis seperti Lang.T di dalam
;  aplikasinya.
; ============================================================================

#define MyAppName "Phoron"
#define MyAppVersion "1.25.0"
#define MyAppExeName "Phoron.exe"
#define MyAppPublisher "Phoron"

[Setup]
; AppId menentukan identitas aplikasi bagi Windows. JANGAN diubah antar versi,
; kalau tidak setiap rilis akan terpasang sebagai aplikasi terpisah dan versi
; lama tidak pernah tergantikan.
AppId={{4E9F2A17-8C3D-4B65-9E10-F7A2D6C0B384}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppSupportURL=https://github.com/Cyserrex/Phoron
VersionInfoVersion={#MyAppVersion}

; BUKAN Program Files. Folder Phoron berisi www\, data\, dan etc\ yang ditulis
; terus-menerus saat aplikasi dipakai; di bawah Program Files, tulisan itu
; dialihkan Windows atau ditolak, dan proyek pengguna jadi susah ditemukan.
; C:\Phoron mengikuti pola yang sama dengan Laragon dan XAMPP - dan folder yang
; dibuat di akar C:\ mewarisi hak tulis untuk Authenticated Users, jadi Phoron
; tetap bisa dipakai tanpa admin setelah terpasang.
DefaultDirName={code:AkarBawaan}
DirExistsWarning=no
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

; "lowest" + OverridesAllowed=dialog: installer menawarkan pasang untuk semua
; pengguna (butuh admin, ke C:\Phoron) atau hanya untuk saya (tanpa admin, ke
; folder pengguna). Penting karena aplikasi ini sering dipakai di PC kantor
; yang penggunanya bukan admin.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; .NET Framework 4.8 sendiri berhenti di Windows 7 SP1.
MinVersion=6.1sp1

OutputDir=Output
OutputBaseFilename=Phoron-{#MyAppVersion}-Setup
SetupIconFile=..\assets\phoron.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

; Dialog bahasa SELALU ditampilkan, dan tanpa penebakan dari lokal Windows:
; bahasa pertama di daftar - Indonesia - jadi pilihan bawaannya. Dengan
; penebakan aktif, Windows berbahasa Inggris akan memilih Inggris sendiri,
; padahal bahasa asal aplikasi ini memang Indonesia.
;
; Pada pemasangan ulang, Inno memakai bahasa yang dipilih terakhir kali
; (UsePreviousLanguage bawaannya yes), jadi orang tidak perlu memilih lagi.
ShowLanguageDialog=yes
LanguageDetectionMethod=none

; Tutup aplikasi yang sedang jalan sebelum menimpa exe-nya, daripada gagal
; dengan "file sedang digunakan" di tengah pemasangan ulang.
;
; Phoron menanggapi permintaan Restart Manager ini dengan mematikan Apache dan
; MySQL lebih dulu (lihat PasangPengawasSesi di MainWindow). Tanpa kerja sama
; itu, penutupannya dialihkan jadi "mengecil ke baki sistem", prosesnya
; dihentikan paksa, dan layanan tertinggal hidup sambil memegang port 80.
CloseApplications=yes
CloseApplicationsFilter=Phoron.exe
RestartApplications=no

[Languages]
; Kode di kolom Name dipakai apa adanya sebagai nilai "bahasa" di phoron.ini,
; jadi keempatnya HARUS sama persis dengan Lang.Indonesia/Inggris/Jawa/Banjar.
; Uji "Bahasa pemasang" di harness menjaga kesamaan itu.
Name: "id"; MessagesFile: "compiler:Default.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "jv"; MessagesFile: "compiler:Default.isl"
Name: "bjn"; MessagesFile: "compiler:Default.isl"

[LangOptions]
; Tanpa ini keempatnya muncul sebagai "English" di dialog pemilihan, sebab
; semuanya berdasar Default.isl.
id.LanguageName=Bahasa Indonesia
en.LanguageName=English
jv.LanguageName=Basa Jawa
bjn.LanguageName=Bahasa Banjar

[Tasks]
Name: "desktopicon"; Description: "{cm:IkonDesktop}"; GroupDescription: "{cm:TugasTambahan}"
; Menulis kunci Run yang sama persis dengan yang dipakai sakelar di halaman
; Pengaturan (Autostart.NamaNilai), jadi keduanya tidak pernah menggandakan
; entri dan aplikasi menampilkan keadaan yang sebenarnya.
Name: "autostart"; Description: "{cm:JalanSaatBoot}"; GroupDescription: "{cm:TugasTambahan}"; Flags: unchecked

[Files]
Source: "..\dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Berkas .config menyatakan runtime yang dibutuhkan (.NET Framework 4.8).
; Tanpa itu, di komputer yang hanya punya 4.0-4.7 aplikasinya tetap mulai lalu
; gagal di tengah dengan galat yang tidak menjelaskan apa pun.
Source: "..\dist\{#MyAppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; Panduan.md pernah ikut dipasang sampai versi 1.9.1. Markdown mentah tidak
; berguna di folder instalasi - tidak semua Windows punya pembukanya, dan
; isinya toh selalu lebih baru di halaman GitHub. Dibuang di sini karena
; berkas yang tidak lagi terdaftar di [Files] tidak ikut terhapus sendiri
; saat pemasangan ulang; ia akan tertinggal selamanya.
Type: files; Name: "{app}\Panduan.md"

[Dirs]
; Kerangka folder dibuat sejak awal supaya pengguna langsung tahu ke mana
; proyeknya ditaruh, tanpa harus menjalankan aplikasinya lebih dulu.
; uninsneveruninstall: folder-folder ini berisi pekerjaan pengguna - jangan
; pernah dihapus otomatis saat aplikasi dicopot.
Name: "{app}\www"; Flags: uninsneveruninstall
Name: "{app}\bin"; Flags: uninsneveruninstall
Name: "{app}\etc"; Flags: uninsneveruninstall
Name: "{app}\data"; Flags: uninsneveruninstall
Name: "{app}\logs"; Flags: uninsneveruninstall
Name: "{app}\tmp"; Flags: uninsneveruninstall
Name: "{app}\profiles"; Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:FolderProyek}"; Filename: "{app}\www"
Name: "{group}\{cm:HapusPhoron}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Phoron"; ValueData: """{app}\{#MyAppExeName}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:JalankanSekarang}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Hanya berkas yang MEMANG dibuat program. www\, data\, profiles\, dan bin\
; tidak disentuh di sini; penghapusannya ditawarkan terpisah di [Code].
Type: files; Name: "{app}\phoron.ini"
Type: filesandordirs; Name: "{app}\tmp"
Type: filesandordirs; Name: "{app}\logs"

; ============================================================================
;  [Messages] - pesan bawaan Inno yang ditimpa per bahasa.
;
;  Awalan bahasa WAJIB ada. Entri tanpa awalan berlaku untuk SEMUA bahasa,
;  jadi teks Indonesia tanpa awalan akan ikut muncul saat orang memilih
;  English - itu keadaan sebelum berkas ini punya lebih dari satu bahasa.
;
;  Bahasa Inggris sengaja tidak ditimpa sama sekali kecuali dua baris di layar
;  pemilihan bahasa: Default.isl memang sudah bahasa Inggris yang baik.
; ============================================================================
[Messages]

; --- Layar pemilihan bahasa -------------------------------------------------
; Layar ini tampil SEBELUM bahasa dipilih, jadi ia memakai pesan bahasa
; pertama di daftar, yaitu Indonesia.
id.SelectLanguageTitle=Pilih Bahasa Pemasang
id.SelectLanguageLabel=Pilih bahasa yang dipakai selama pemasangan. Bahasa ini juga akan dipakai Phoron.
en.SelectLanguageLabel=Select the language to use during installation. Phoron itself will use it too.
jv.SelectLanguageTitle=Pilih Basa Pamasang
jv.SelectLanguageLabel=Pilih basa sing dianggo sajrone pamasangan. Basa iki uga bakal dianggo Phoron.
bjn.SelectLanguageTitle=Pilih Basa Pamasang
bjn.SelectLanguageLabel=Pilih basa nang dipakai wayah mamasang. Basa itu dipakai jua di Phoron.

; --- Indonesia --------------------------------------------------------------
id.SetupAppTitle=Pemasang
id.SetupWindowTitle=Pemasang - %1
id.ExitSetupTitle=Keluar dari Pemasang
id.ExitSetupMessage=Pemasangan belum selesai. Kalau keluar sekarang, aplikasi tidak akan terpasang.%n%nYakin mau keluar?
id.ButtonBack=< &Kembali
id.ButtonNext=&Lanjut >
id.ButtonInstall=&Pasang
id.ButtonCancel=Batal
id.ButtonYes=&Ya
id.ButtonNo=&Tidak
id.ButtonFinish=&Selesai
id.ButtonBrowse=&Telusuri...
id.ClickNext=Klik Lanjut untuk melanjutkan, atau Batal untuk keluar.
id.BeveledLabel=

id.WelcomeLabel1=Selamat datang di pemasang [name]
id.WelcomeLabel2=Aplikasi ini akan memasang [name/ver] di komputer Anda.%n%nPhoron menjalankan Apache, PHP, dan MySQL secara lokal, dengan versi yang bisa ditukar lewat profil.

id.PrivilegesRequiredOverrideTitle=Pilih Cara Pemasangan
id.PrivilegesRequiredOverrideInstruction=Pilih untuk siapa aplikasi ini dipasang
id.PrivilegesRequiredOverrideText1=[name] bisa dipasang untuk semua pengguna (butuh hak administrator), atau hanya untuk Anda.
id.PrivilegesRequiredOverrideText2=[name] bisa dipasang hanya untuk Anda, atau untuk semua pengguna (butuh hak administrator).
id.PrivilegesRequiredOverrideAllUsers=Pasang untuk &semua pengguna (di C:\Phoron)
id.PrivilegesRequiredOverrideCurrentUser=Pasang hanya untuk &saya
id.PrivilegesRequiredOverrideCurrentUserRecommended=Pasang hanya untuk &saya (disarankan)

id.WizardSelectDir=Pilih Lokasi Pemasangan
id.SelectDirDesc=Di mana [name] akan dipasang?
id.SelectDirLabel3=Folder ini juga akan memuat proyek web Anda (www), konfigurasi (etc), dan basis data (data).
id.SelectDirBrowseLabel=Klik Lanjut untuk memakai folder ini. Untuk folder lain, klik Telusuri.
id.DiskSpaceGBLabel=Butuh ruang kosong minimal [gb] GB.
id.DiskSpaceMBLabel=Butuh ruang kosong minimal [mb] MB.
id.CannotInstallToNetworkDrive=Tidak bisa memasang ke drive jaringan.
id.InvalidPath=Masukkan jalur lengkap beserta huruf drive, contoh:%n%nC:\Phoron

id.WizardSelectTasks=Pilih Tugas Tambahan
id.SelectTasksDesc=Tugas tambahan apa yang perlu dijalankan?
id.SelectTasksLabel2=Pilih tugas tambahan, lalu klik Lanjut.

id.WizardReady=Siap Memasang
id.ReadyLabel1=Pemasang siap memasang [name] di komputer Anda.
id.ReadyLabel2a=Klik Pasang untuk mulai, atau Kembali untuk mengubah pilihan.
id.ReadyLabel2b=Klik Pasang untuk mulai memasang.
id.ReadyMemoDir=Lokasi pemasangan:
id.ReadyMemoTasks=Tugas tambahan:
id.ReadyMemoGroup=Folder Start Menu:

id.WizardPreparing=Menyiapkan
id.PreparingDesc=Menyiapkan pemasangan [name].
id.WizardInstalling=Memasang
id.InstallingLabel=Mohon tunggu, [name] sedang dipasang...

id.FinishedHeadingLabel=Pemasangan [name] selesai
id.FinishedLabelNoIcons=[name] sudah terpasang di komputer Anda.
id.FinishedLabel=[name] sudah terpasang. Jalankan lewat ikon yang dibuat.
id.ClickFinish=Klik Selesai untuk menutup pemasang.
id.RunEntryExec=Jalankan %1

id.ConfirmUninstall=Yakin mau menghapus %1 beserta seluruh komponennya?
id.UninstallStatusLabel=Mohon tunggu, %1 sedang dihapus...
id.UninstalledAll=%1 berhasil dihapus dari komputer Anda.
id.UninstalledMost=%1 sudah dihapus.%n%nBeberapa item tidak bisa dihapus dan bisa Anda hapus manual.
id.StatusExtractFiles=Menyalin berkas...
id.StatusCreateIcons=Membuat pintasan...
id.StatusUninstalling=Menghapus %1...
id.ErrorTitle=Galat
id.SetupAborted=Pemasangan tidak selesai.%n%nPerbaiki masalahnya lalu jalankan pemasang lagi.

; --- Jawa -------------------------------------------------------------------
jv.SetupAppTitle=Pamasang
jv.SetupWindowTitle=Pamasang - %1
jv.ExitSetupTitle=Metu saka Pamasang
jv.ExitSetupMessage=Pamasangan durung rampung. Yen metu saiki, aplikasine ora bakal kepasang.%n%nYakin arep metu?
jv.ButtonBack=< &Mundur
jv.ButtonNext=&Terusake >
jv.ButtonInstall=&Pasang
jv.ButtonCancel=Batal
jv.ButtonYes=&Iya
jv.ButtonNo=&Ora
jv.ButtonFinish=&Rampung
jv.ButtonBrowse=&Golek...
jv.ClickNext=Klik Terusake kanggo nerusake, utawa Batal kanggo metu.
jv.BeveledLabel=

jv.WelcomeLabel1=Sugeng rawuh ing pamasang [name]
jv.WelcomeLabel2=Aplikasi iki bakal masang [name/ver] ing komputer sampeyan.%n%nPhoron nglakokake Apache, PHP, lan MySQL ing lokal, kanthi versi sing bisa diganti liwat profil.

jv.PrivilegesRequiredOverrideTitle=Pilih Cara Pamasangan
jv.PrivilegesRequiredOverrideInstruction=Pilih arep dipasang kanggo sapa
jv.PrivilegesRequiredOverrideText1=[name] bisa dipasang kanggo kabeh pangguna (butuh hak administrator), utawa mung kanggo sampeyan.
jv.PrivilegesRequiredOverrideText2=[name] bisa dipasang mung kanggo sampeyan, utawa kanggo kabeh pangguna (butuh hak administrator).
jv.PrivilegesRequiredOverrideAllUsers=Pasang kanggo &kabeh pangguna (ing C:\Phoron)
jv.PrivilegesRequiredOverrideCurrentUser=Pasang mung kanggo &aku
jv.PrivilegesRequiredOverrideCurrentUserRecommended=Pasang mung kanggo &aku (disaranake)

jv.WizardSelectDir=Pilih Panggonan Pamasangan
jv.SelectDirDesc=Ing ngendi [name] bakal dipasang?
jv.SelectDirLabel3=Folder iki uga bakal ngemot proyek web sampeyan (www), konfigurasi (etc), lan basis data (data).
jv.SelectDirBrowseLabel=Klik Terusake kanggo nganggo folder iki. Kanggo folder liya, klik Golek.
jv.DiskSpaceGBLabel=Butuh papan kosong paling sethithik [gb] GB.
jv.DiskSpaceMBLabel=Butuh papan kosong paling sethithik [mb] MB.
jv.CannotInstallToNetworkDrive=Ora bisa masang menyang drive jaringan.
jv.InvalidPath=Lebokna jalur lengkap saka huruf drive, tuladha:%n%nC:\Phoron

jv.WizardSelectTasks=Pilih Tugas Tambahan
jv.SelectTasksDesc=Tugas tambahan apa sing perlu dilakoni?
jv.SelectTasksLabel2=Pilih tugas tambahan, banjur klik Terusake.

jv.WizardReady=Siap Masang
jv.ReadyLabel1=Pamasang wis siap masang [name] ing komputer sampeyan.
jv.ReadyLabel2a=Klik Pasang kanggo miwiti, utawa Mundur kanggo ngowahi pilihan.
jv.ReadyLabel2b=Klik Pasang kanggo miwiti pamasangan.
jv.ReadyMemoDir=Panggonan pamasangan:
jv.ReadyMemoTasks=Tugas tambahan:
jv.ReadyMemoGroup=Folder Start Menu:

jv.WizardPreparing=Nyiapake
jv.PreparingDesc=Nyiapake pamasangan [name].
jv.WizardInstalling=Masang
jv.InstallingLabel=Mangga ngenteni, [name] lagi dipasang...

jv.FinishedHeadingLabel=Pamasangan [name] rampung
jv.FinishedLabelNoIcons=[name] wis kepasang ing komputer sampeyan.
jv.FinishedLabel=[name] wis kepasang. Bukak liwat ikon sing digawe.
jv.ClickFinish=Klik Rampung kanggo nutup pamasang.
jv.RunEntryExec=Bukak %1

jv.ConfirmUninstall=Yakin arep mbusak %1 sakabehane komponene?
jv.UninstallStatusLabel=Mangga ngenteni, %1 lagi dibusak...
jv.UninstalledAll=%1 kasil dibusak saka komputer sampeyan.
jv.UninstalledMost=%1 wis dibusak.%n%nSawetara item ora bisa dibusak lan bisa sampeyan busak dhewe.
jv.StatusExtractFiles=Nyalin berkas...
jv.StatusCreateIcons=Nggawe pintasan...
jv.StatusUninstalling=Mbusak %1...
jv.ErrorTitle=Galat
jv.SetupAborted=Pamasangan ora rampung.%n%nBenerake masalahe banjur bukak pamasang maneh.

; --- Banjar -----------------------------------------------------------------
bjn.SetupAppTitle=Pamasang
bjn.SetupWindowTitle=Pamasang - %1
bjn.ExitSetupTitle=Kaluar matan Pamasang
bjn.ExitSetupMessage=Pamasangan balum tuntung. Amun kaluar wayah ini, aplikasinya kada tapasang.%n%nBujur handak kaluar?
bjn.ButtonBack=< &Bulik
bjn.ButtonNext=&Tarusakan >
bjn.ButtonInstall=&Pasang
bjn.ButtonCancel=Batal
bjn.ButtonYes=&Iya
bjn.ButtonNo=&Kada
bjn.ButtonFinish=&Tuntung
bjn.ButtonBrowse=&Gagai...
bjn.ClickNext=Klik Tarusakan gasan manarusakan, atawa Batal gasan kaluar.
bjn.BeveledLabel=

bjn.WelcomeLabel1=Salamat datang di pamasang [name]
bjn.WelcomeLabel2=Aplikasi ini handak mamasang [name/ver] di komputer Pian.%n%nPhoron manjalanakan Apache, PHP, wan MySQL di lokal, lawan versi nang kawa ditukar liwat profil.

bjn.PrivilegesRequiredOverrideTitle=Pilih Cara Mamasang
bjn.PrivilegesRequiredOverrideInstruction=Pilih gasan siapa aplikasi ini dipasang
bjn.PrivilegesRequiredOverrideText1=[name] kawa dipasang gasan samunyaan pamakai (paralu hak administrator), atawa gasan Pian haja.
bjn.PrivilegesRequiredOverrideText2=[name] kawa dipasang gasan Pian haja, atawa gasan samunyaan pamakai (paralu hak administrator).
bjn.PrivilegesRequiredOverrideAllUsers=Pasang gasan &samunyaan pamakai (di C:\Phoron)
bjn.PrivilegesRequiredOverrideCurrentUser=Pasang gasan &ulun haja
bjn.PrivilegesRequiredOverrideCurrentUserRecommended=Pasang gasan &ulun haja (disarankan)

bjn.WizardSelectDir=Pilih Tampat Mamasang
bjn.SelectDirDesc=Di mana [name] handak dipasang?
bjn.SelectDirLabel3=Folder ini jua nang mamuat proyek web Pian (www), pangaturan (etc), wan basis data (data).
bjn.SelectDirBrowseLabel=Klik Tarusakan gasan mamakai folder ini. Gasan folder lain, klik Gagai.
bjn.DiskSpaceGBLabel=Paralu tampat kosong sadikitnya [gb] GB.
bjn.DiskSpaceMBLabel=Paralu tampat kosong sadikitnya [mb] MB.
bjn.CannotInstallToNetworkDrive=Kada kawa mamasang ka drive jaringan.
bjn.InvalidPath=Masukakan jalur lengkap wan huruf drive, misalnya:%n%nC:\Phoron

bjn.WizardSelectTasks=Pilih Gawian Tambahan
bjn.SelectTasksDesc=Gawian tambahan apa nang paralu dilakuakan?
bjn.SelectTasksLabel2=Pilih gawian tambahan, hanyar klik Tarusakan.

bjn.WizardReady=Siap Mamasang
bjn.ReadyLabel1=Pamasang siap mamasang [name] di komputer Pian.
bjn.ReadyLabel2a=Klik Pasang gasan mamulai, atawa Bulik gasan maubah pilihan.
bjn.ReadyLabel2b=Klik Pasang gasan mamulai mamasang.
bjn.ReadyMemoDir=Tampat mamasang:
bjn.ReadyMemoTasks=Gawian tambahan:
bjn.ReadyMemoGroup=Folder Start Menu:

bjn.WizardPreparing=Manyiapakan
bjn.PreparingDesc=Manyiapakan pamasangan [name].
bjn.WizardInstalling=Mamasang
bjn.InstallingLabel=Sabar ai, [name] lagi dipasang...

bjn.FinishedHeadingLabel=Pamasangan [name] tuntung
bjn.FinishedLabelNoIcons=[name] sudah tapasang di komputer Pian.
bjn.FinishedLabel=[name] sudah tapasang. Buka liwat ikon nang digawi.
bjn.ClickFinish=Klik Tuntung gasan manutup pamasang.
bjn.RunEntryExec=Buka %1

bjn.ConfirmUninstall=Bujur handak mahapus %1 wan samunyaan komponennya?
bjn.UninstallStatusLabel=Sabar ai, %1 lagi dihapus...
bjn.UninstalledAll=%1 sukses dihapus matan komputer Pian.
bjn.UninstalledMost=%1 sudah dihapus.%n%nBabarapa item kada kawa dihapus wan kawa Pian hapus surang.
bjn.StatusExtractFiles=Manyalin barakas...
bjn.StatusCreateIcons=Manggawi pintasan...
bjn.StatusUninstalling=Mahapus %1...
bjn.ErrorTitle=Kasalahan
bjn.SetupAborted=Pamasangan kada tuntung.%n%nBaiki masalahnya hanyar buka pamasang pulang.

; ============================================================================
;  [CustomMessages] - pesan milik Phoron sendiri.
;
;  Keempat bahasa ditulis LENGKAP, tanpa mengandalkan entri tanpa awalan
;  sebagai cadangan. CustomMessage() yang tidak menemukan padanannya gagal
;  saat pemasangan berjalan, bukan saat dirakit - jadi kesalahan seperti itu
;  baru ketahuan di komputer orang lain.
; ============================================================================
[CustomMessages]

; --- Tugas, ikon, dan tombol jalankan ---------------------------------------
id.TugasTambahan=Pintasan tambahan:
id.IkonDesktop=Buat ikon di Desktop
id.JalanSaatBoot=Jalankan Phoron saat Windows dinyalakan (mengecil ke baki sistem)
id.FolderProyek=Folder proyek (www)
id.HapusPhoron=Hapus Phoron
id.JalankanSekarang=Jalankan Phoron sekarang

en.TugasTambahan=Additional shortcuts:
en.IkonDesktop=Create a Desktop icon
en.JalanSaatBoot=Start Phoron when Windows starts (minimised to the system tray)
en.FolderProyek=Project folder (www)
en.HapusPhoron=Uninstall Phoron
en.JalankanSekarang=Run Phoron now

jv.TugasTambahan=Pintasan tambahan:
jv.IkonDesktop=Gawe ikon ing Desktop
jv.JalanSaatBoot=Bukak Phoron nalika Windows urip (ngalih menyang baki sistem)
jv.FolderProyek=Folder proyek (www)
jv.HapusPhoron=Busak Phoron
jv.JalankanSekarang=Bukak Phoron saiki

bjn.TugasTambahan=Pintasan tambahan:
bjn.IkonDesktop=Gawi ikon di Desktop
bjn.JalanSaatBoot=Hidupakan Phoron wayah Windows dihidupakan (mangacil ka baki sistem)
bjn.FolderProyek=Folder proyek (www)
bjn.HapusPhoron=Hapus Phoron
bjn.JalankanSekarang=Hidupakan Phoron wayah ini

; --- Menutup Phoron yang sedang berjalan ------------------------------------
id.TawarTutup=Phoron sedang berjalan.%n%n%1%nPemasang perlu menutupnya lebih dulu supaya berkasnya bisa diganti dan layanan tidak tertinggal hidup.%n%nTutup Phoron sekarang?
id.GagalTutup=Phoron masih berjalan dan tidak bisa ditutup pemasang.%n%nKemungkinan ia dijalankan sebagai Administrator sementara pemasang ini tidak.%n%nTutup Phoron lewat tombol "Keluar" di panel kirinya, lalu jalankan pemasang ini lagi.
id.BatalTutup=Pemasangan dihentikan karena Phoron masih berjalan. Tutup Phoron lebih dulu, lalu jalankan pemasang ini lagi.
id.TanpaPort=Tidak ada port yang sedang dipegangnya.
id.LayananPegangPort=Layanan berikut masih memegang portnya:
id.DibatalkanAnda=Pemasangan dibatalkan atas permintaan Anda.

en.TawarTutup=Phoron is running.%n%n%1%nSetup needs to close it first so its files can be replaced and no service is left running.%n%nClose Phoron now?
en.GagalTutup=Phoron is still running and Setup could not close it.%n%nIt was probably started as Administrator while this installer was not.%n%nClose Phoron with the "Quit" button in its left panel, then run this installer again.
en.BatalTutup=Setup stopped because Phoron is still running. Close Phoron first, then run this installer again.
en.TanpaPort=It is not holding any port.
en.LayananPegangPort=These services are still holding their ports:
en.DibatalkanAnda=Setup was cancelled at your request.

jv.TawarTutup=Phoron lagi mlaku.%n%n%1%nPamasang kudu nutup dhisik supaya berkase bisa diganti lan layanane ora ketinggalan urip.%n%nTutup Phoron saiki?
jv.GagalTutup=Phoron isih mlaku lan ora bisa ditutup pamasang.%n%nBisa uga dibukak minangka Administrator dene pamasang iki ora.%n%nTutup Phoron liwat tombol "Metu" ing panel kiwane, banjur bukak pamasang iki maneh.
jv.BatalTutup=Pamasangan dieureni amarga Phoron isih mlaku. Tutup Phoron dhisik, banjur bukak pamasang iki maneh.
jv.TanpaPort=Ora ana port sing lagi dicekel.
jv.LayananPegangPort=Layanan iki isih nyekel porte:
jv.DibatalkanAnda=Pamasangan dibatalake miturut panjaluk sampeyan.

bjn.TawarTutup=Phoron lagi bajalan.%n%n%1%nPamasang paralu manutupnya labih dahulu supaya barakasnya kawa diganti wan layanannya kada tatinggal hidup.%n%nTutup Phoron wayah ini?
bjn.GagalTutup=Phoron masih bajalan wan kada kawa ditutup pamasang.%n%nKamungkinan inya dibuka sabagai Administrator sedangkan pamasang ini kada.%n%nTutup Phoron liwat tombol "Kaluar" di panel kiwanya, hanyar buka pamasang ini pulang.
bjn.BatalTutup=Pamasangan dihantiakan karana Phoron masih bajalan. Tutup Phoron labih dahulu, hanyar buka pamasang ini pulang.
bjn.TanpaPort=Kada ada port nang lagi dipacangnya.
bjn.LayananPegangPort=Layanan barikut masih mamacang portnya:
bjn.DibatalkanAnda=Pamasangan dibatalakan sasuai pamintaan Pian.

; --- Port yang masih terpakai -----------------------------------------------
id.PortSisa=Port berikut masih terpakai meski Phoron sudah ditutup:%n%n%1%nBiasanya ini sisa proses dari Phoron versi lama yang berakhir tanpa sempat membersihkan diri.%n%nPemasangan tetap bisa dilanjutkan - Phoron versi baru punya tombol "Hentikan proses yang tertinggal" di Beranda untuk membereskannya.%n%nLanjutkan?
en.PortSisa=These ports are still in use even though Phoron has been closed:%n%n%1%nUsually these are leftovers from an older Phoron that ended without cleaning up.%n%nYou can still continue - the new Phoron has a "Hentikan proses yang tertinggal" button on its Home page that clears them.%n%nContinue?
jv.PortSisa=Port iki isih kepake senajan Phoron wis ditutup:%n%n%1%nBiasane iki turahan proses saka Phoron versi lawas sing mandheg tanpa sempat ngresiki awake.%n%nPamasangan isih bisa diterusake - Phoron versi anyar duwe tombol "Hentikan proses yang tertinggal" ing Ngarep kanggo mbenerake.%n%nDiterusake?
bjn.PortSisa=Port barikut masih tapakai walaupun Phoron sudah ditutup:%n%n%1%nBiasanya ini sisa proses matan Phoron versi lawas nang baranti tanpa sempat mambarasihi diri.%n%nPamasangan tatap kawa ditarusakan - Phoron versi hanyar ada tombol "Hentikan proses yang tertinggal" di Laman Muka gasan mambereskannya.%n%nTarusakan?

; --- .NET Framework ---------------------------------------------------------
id.NeedDotNet=Aplikasi ini memerlukan Microsoft .NET Framework 4.8, yang belum terpasang di komputer ini.%n%nWindows 10 versi 1903 ke atas dan Windows 11 sudah membawanya. Pada Windows 7 SP1 atau 8.1, .NET Framework 4.8 perlu dipasang sekali (gratis, dari Microsoft).%n%nBuka halaman unduhannya sekarang?
en.NeedDotNet=This application needs Microsoft .NET Framework 4.8, which is not installed on this computer.%n%nWindows 10 version 1903 and later, and Windows 11, already include it. On Windows 7 SP1 or 8.1 it has to be installed once (free, from Microsoft).%n%nOpen the download page now?
jv.NeedDotNet=Aplikasi iki mbutuhake Microsoft .NET Framework 4.8, sing durung kepasang ing komputer iki.%n%nWindows 10 versi 1903 munggah lan Windows 11 wis nggawa. Ing Windows 7 SP1 utawa 8.1, .NET Framework 4.8 kudu dipasang sepisan (gratis, saka Microsoft).%n%nBukak kaca undhuhane saiki?
bjn.NeedDotNet=Aplikasi ini mamarluakan Microsoft .NET Framework 4.8, nang balum tapasang di komputer ini.%n%nWindows 10 versi 1903 ka atas wan Windows 11 sudah mambawanya. Di Windows 7 SP1 atawa 8.1, .NET Framework 4.8 paralu dipasang sakali (gratis, matan Microsoft).%n%nBuka laman hunduhannya wayah ini?

; --- Menghapus data saat mencopot -------------------------------------------
id.HapusData=Hapus juga proyek web, basis data, profil, dan versi PHP/Apache yang tersimpan di%n%n%1%n%nPilih Tidak (disarankan) kalau Anda masih membutuhkan proyek di folder www.
en.HapusData=Also delete the web projects, databases, profiles, and PHP/Apache versions stored in%n%n%1%n%nChoose No (recommended) if you still need the projects in the www folder.
jv.HapusData=Busak uga proyek web, basis data, profil, lan versi PHP/Apache sing kasimpen ing%n%n%1%n%nPilih Ora (disaranake) yen sampeyan isih mbutuhake proyek ing folder www.
bjn.HapusData=Hapus jua proyek web, basis data, profil, wan versi PHP/Apache nang tasimpan di%n%n%1%n%nPilih Kada (disarankan) amun Pian masih mamarluakan proyek di folder www.

[Code]

/// Meminta Phoron menutup diri lewat event bernama yang didengarkannya.
/// Deklarasi API Windows langsung: Pascal Script tidak punya padanan untuk
/// event kernel, dan ini satu-satunya cara meminta dengan santun - bukan
/// memaksa - sehingga Apache dan MySQL sempat dimatikan dengan rapi.
function OpenEvent(dwDesiredAccess: LongWord; bInheritHandle: Boolean;
  lpName: String): LongWord; external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: LongWord): Boolean; external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: LongWord): Boolean; external 'CloseHandle@kernel32.dll stdcall';

const
  EVENT_MODIFY_STATE = $0002;

/// Tunggu sampai Phoron benar-benar berakhir, paling lama detik yang diminta.
function TungguPhoronBerakhir(detik: Integer): Boolean;
var
  i: Integer;
begin
  for i := 1 to detik * 4 do
  begin
    if not CheckForMutexes('Phoron.SingleInstance') then
    begin
      Result := True;
      Exit;
    end;
    Sleep(250);
  end;
  Result := not CheckForMutexes('Phoron.SingleInstance');
end;

/// Tutup Phoron: minta baik-baik dulu, paksa kalau perlu.
function TutupPhoron(): Boolean;
var
  h: LongWord;
  kode: Integer;
begin
  { 1. Sinyal tutup. Versi 1.9.1 ke atas mendengarkannya dan berhenti dengan
       rapi: layanan dimatikan lebih dulu, mysqld lewat mysqladmin shutdown. }
  h := OpenEvent(EVENT_MODIFY_STATE, False, 'Phoron.KeluarSekarang');
  if h <> 0 then
  begin
    SetEvent(h);
    CloseHandle(h);
    if TungguPhoronBerakhir(20) then
    begin
      Result := True;
      Exit;
    end;
  end;

  { 2. Versi lama tidak mengenal sinyal itu, jadi jalur paksa tetap perlu ada.
       Sejak 1.8.1 proses anak terikat Job Object, sehingga httpd dan mysqld
       ikut berakhir bersama induknya dan tidak meninggalkan port terkunci.

       Jalurnya disusun dengan pemisah folder yang jelas. Berkas ini pernah
       memuat karakter TAB di tempat "\t" seharusnya berada, sehingga jalur
       yang dijalankan menunjuk berkas yang tidak pernah ada - dan jalur paksa
       ini diam-diam tidak berbuat apa-apa selama beberapa rilis. }
  Exec(ExpandConstant('{sys}') + '\taskkill.exe', '/IM Phoron.exe /T /F',
       '', SW_HIDE, ewWaitUntilTerminated, kode);
  Result := TungguPhoronBerakhir(10);
end;

/// Apakah sebuah port TCP sedang didengarkan, dan oleh proses apa.
/// Dibaca dari netstat: Inno Setup tidak punya akses soket sendiri, dan
/// menambah DLL bantu hanya untuk satu pemeriksaan tidak sepadan.
function PortDipakai(Port: Integer; var Keterangan: String): Boolean;
var
  keluaran: AnsiString;
  berkas, baris: String;
  kode, i: Integer;
  daftar: TStringList;
begin
  Result := False;
  Keterangan := '';
  berkas := ExpandConstant('{tmp}\port.txt');
  { cmd /c dipakai supaya pengalihan keluaran ke berkas bekerja. }
  if not Exec(ExpandConstant('{cmd}'), '/c netstat -ano -p tcp | findstr /r /c:":'
      + IntToStr(Port) + ' .*LISTENING" > "' + berkas + '"',
      '', SW_HIDE, ewWaitUntilTerminated, kode) then
    Exit;
  if not LoadStringFromFile(berkas, keluaran) then Exit;
  if Trim(String(keluaran)) = '' then Exit;

  daftar := TStringList.Create;
  try
    daftar.Text := String(keluaran);
    for i := 0 to daftar.Count - 1 do
    begin
      baris := Trim(daftar[i]);
      if baris <> '' then
      begin
        Result := True;
        Keterangan := '  port ' + IntToStr(Port);
        Break;
      end;
    end;
  finally
    daftar.Free;
  end;
end;

/// Daftar port Phoron yang masih terpakai, satu per baris. Kosong = semua bebas.
function PortPhoronTerpakai(): String;
var
  ket: String;
begin
  Result := '';
  if PortDipakai(80, ket) then Result := Result + ket + ' (Apache)' + #13#10;
  if PortDipakai(443, ket) then Result := Result + ket + ' (Apache HTTPS)' + #13#10;
  if PortDipakai(3306, ket) then Result := Result + ket + ' (MySQL)' + #13#10;
end;

/// Folder bawaan: C:\Phoron kalau dipasang untuk semua pengguna, folder
/// pengguna kalau tidak. Ditentukan lewat kode karena pilihan hak akses baru
/// diketahui setelah dialog "untuk siapa" dijawab.
function AkarBawaan(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{sd}\Phoron')
  else
    Result := ExpandConstant('{localappdata}\Phoron');
end;

/// Rilis .NET Framework 4.8 bernomor 528040 ke atas.
function DotNet48Installed(): Boolean;
var
  release: Cardinal;
begin
  Result := RegQueryDWordValue(
    HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full',
    'Release', release) and (release >= 528040);
end;

function InitializeSetup(): Boolean;
var
  dummy: Integer;
begin
  Result := True;
  { Windows 10 1903 ke atas dan Windows 11 sudah membawa 4.8, jadi kotak ini
    praktis hanya muncul di Windows 7 SP1 / 8.1 - dan justru di situ ia paling
    dibutuhkan: tanpa pemeriksaan ini pemasangan tetap "berhasil" lalu
    aplikasinya gagal dibuka tanpa penjelasan apa pun. }
  if not DotNet48Installed() then
  begin
    if MsgBox(ExpandConstant('{cm:NeedDotNet}'), mbConfirmation,
              MB_YESNO) = IDYES then
      ShellExec('open',
                'https://dotnet.microsoft.com/download/dotnet-framework/net48',
                '', '', SW_SHOW, ewNoWait, dummy);
  end;
end;

/// Dijalankan SETELAH Restart Manager menutup aplikasi yang berjalan, tepat
/// sebelum berkas disalin. Di sinilah keadaan sebenarnya bisa diperiksa: kalau
/// Phoron masih hidup, menimpa exe-nya akan menghasilkan pemasangan setengah
/// jadi - dan layanan yang masih memegang port tetap tertinggal setelahnya.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  port: String;
begin
  Result := '';

  if CheckForMutexes('Phoron.SingleInstance') then
  begin
    port := PortPhoronTerpakai();
    if port = '' then
      port := CustomMessage('TanpaPort') + #13#10
    else
      port := CustomMessage('LayananPegangPort') + #13#10 + #13#10 + port;

    if MsgBox(FmtMessage(CustomMessage('TawarTutup'), [port]), mbConfirmation,
              MB_YESNO) = IDNO then
    begin
      Result := CustomMessage('BatalTutup');
      Exit;
    end;

    if not TutupPhoron() then
    begin
      Result := CustomMessage('GagalTutup');
      Exit;
    end;
  end;

  { Phoron sudah tidak berjalan, tapi portnya masih dipegang sesuatu - hampir
    selalu sisa httpd/mysqld dari versi lama yang dihentikan paksa. Ini bukan
    penghalang pemasangan, jadi cukup diberitahukan. }
  port := PortPhoronTerpakai();
  if port <> '' then
    if MsgBox(FmtMessage(CustomMessage('PortSisa'), [port]), mbConfirmation,
              MB_YESNO) = IDNO then
      Result := CustomMessage('DibatalkanAnda');
end;

/// Tuliskan bahasa yang dipilih di pemasang ke phoron.ini, supaya Phoron
/// menyala dalam bahasa itu tanpa perlu diatur lagi dari dalam.
///
/// Tidak ditulis begitu saja setiap kali. Orang bisa saja memasang dalam
/// bahasa Indonesia lalu menggantinya ke Inggris di halaman Pengaturan;
/// menimpanya di tiap pemasangan ulang akan mengembalikannya ke Indonesia
/// berulang-ulang tanpa sebab yang jelas. Karena itu pilihan pemasang ikut
/// dicatat, dan bahasanya hanya ditulis kalau ini pemasangan pertama, atau
/// kalau pilihan di pemasang MEMANG berubah dari yang terakhir dipakai.
///
/// Kunci bahasa_pemasang tidak dikenal Phoron, dan itu tidak masalah:
/// Settings.Save() memuat ulang berkasnya lalu menyetel kunci yang
/// dikenalnya saja, jadi kunci asing tetap terjaga.
procedure SimpanBahasaKePhoron();
var
  ini, sekarang, sebelumnya: String;
begin
  ini := ExpandConstant('{app}\phoron.ini');
  sekarang := ActiveLanguage();
  sebelumnya := GetIniString('umum', 'bahasa_pemasang', '', ini);

  if (GetIniString('umum', 'bahasa', '', ini) = '') or (sebelumnya <> sekarang) then
    SetIniString('umum', 'bahasa', sekarang, ini);

  SetIniString('umum', 'bahasa_pemasang', sekarang, ini);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SimpanBahasaKePhoron();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  app: String;
begin
  { Ditanyakan, tidak pernah dilakukan diam-diam: folder www bisa berisi
    berbulan-bulan pekerjaan, dan folder data berisi basis data yang tidak ada
    salinannya di tempat lain. }

  { Pencopotan senyap TIDAK pernah menghapus data. Dengan /SUPPRESSMSGBOXES,
    MsgBox di bawah menjawab Ya sendiri - MB_DEFBUTTON2 tidak berlaku di mode
    senyap - sehingga tanpa penjaga ini, satu pencopotan lewat skrip atau
    perkakas manajemen akan melenyapkan seluruh folder www dan basis data
    tanpa seorang pun sempat membacanya. Ini bukan hipotesis: uji pencopotan
    pertama memang menghapus semuanya. }
  if UninstallSilent then Exit;

  if CurUninstallStep = usPostUninstall then
  begin
    app := ExpandConstant('{app}');
    if DirExists(app + '\www') or DirExists(app + '\data') then
    begin
      if MsgBox(FmtMessage(CustomMessage('HapusData'), [app]),
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(app + '\www', True, True, True);
        DelTree(app + '\data', True, True, True);
        DelTree(app + '\profiles', True, True, True);
        DelTree(app + '\bin', True, True, True);
        DelTree(app + '\etc', True, True, True);
        DelTree(app, True, True, True);
      end;
    end;
  end;
end;
