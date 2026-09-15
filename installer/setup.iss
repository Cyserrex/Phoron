; ============================================================================
;  Phoron - skrip installer Inno Setup
;
;  Bangun dengan:  build_installer.bat        (dari folder induk)
;  atau manual  :  ISCC.exe installer\setup.iss
;
;  Syarat: dist\Phoron.exe sudah ada (jalankan build.bat dulu).
; ============================================================================

#define MyAppName "Phoron"
#define MyAppVersion "1.21.0"
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
Name: "id"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Buat ikon di Desktop"; GroupDescription: "Pintasan tambahan:"
; Menulis kunci Run yang sama persis dengan yang dipakai sakelar di halaman
; Pengaturan (Autostart.NamaNilai), jadi keduanya tidak pernah menggandakan
; entri dan aplikasi menampilkan keadaan yang sebenarnya.
Name: "autostart"; Description: "Jalankan Phoron saat Windows dinyalakan (mengecil ke baki sistem)"; GroupDescription: "Pintasan tambahan:"; Flags: unchecked

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
Name: "{group}\Folder proyek (www)"; Filename: "{app}\www"
Name: "{group}\Hapus {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Phoron"; ValueData: """{app}\{#MyAppExeName}"" --tray"; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Jalankan {#MyAppName} sekarang"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Hanya berkas yang MEMANG dibuat program. www\, data\, profiles\, dan bin\
; tidak disentuh di sini; penghapusannya ditawarkan terpisah di [Code].
Type: files; Name: "{app}\phoron.ini"
Type: filesandordirs; Name: "{app}\tmp"
Type: filesandordirs; Name: "{app}\logs"

[Messages]
; Inno Setup 6 belum menyertakan terjemahan Indonesia resmi, jadi teks yang
; benar-benar dibaca pengguna ditimpa di sini di atas Default.isl.
SetupAppTitle=Pemasang
SetupWindowTitle=Pemasang - %1
ExitSetupTitle=Keluar dari Pemasang
ExitSetupMessage=Pemasangan belum selesai. Kalau keluar sekarang, aplikasi tidak akan terpasang.%n%nYakin mau keluar?
ButtonBack=< &Kembali
ButtonNext=&Lanjut >
ButtonInstall=&Pasang
ButtonCancel=Batal
ButtonYes=&Ya
ButtonNo=&Tidak
ButtonFinish=&Selesai
ButtonBrowse=&Telusuri...
ClickNext=Klik Lanjut untuk melanjutkan, atau Batal untuk keluar.
BeveledLabel=

WelcomeLabel1=Selamat datang di pemasang [name]
WelcomeLabel2=Aplikasi ini akan memasang [name/ver] di komputer Anda.%n%nPhoron menjalankan Apache, PHP, dan MySQL secara lokal, dengan versi yang bisa ditukar lewat profil.

PrivilegesRequiredOverrideTitle=Pilih Cara Pemasangan
PrivilegesRequiredOverrideInstruction=Pilih untuk siapa aplikasi ini dipasang
PrivilegesRequiredOverrideText1=[name] bisa dipasang untuk semua pengguna (butuh hak administrator), atau hanya untuk Anda.
PrivilegesRequiredOverrideText2=[name] bisa dipasang hanya untuk Anda, atau untuk semua pengguna (butuh hak administrator).
PrivilegesRequiredOverrideAllUsers=Pasang untuk &semua pengguna (di C:\Phoron)
PrivilegesRequiredOverrideCurrentUser=Pasang hanya untuk &saya
PrivilegesRequiredOverrideCurrentUserRecommended=Pasang hanya untuk &saya (disarankan)

WizardSelectDir=Pilih Lokasi Pemasangan
SelectDirDesc=Di mana [name] akan dipasang?
SelectDirLabel3=Folder ini juga akan memuat proyek web Anda (www), konfigurasi (etc), dan basis data (data).
SelectDirBrowseLabel=Klik Lanjut untuk memakai folder ini. Untuk folder lain, klik Telusuri.
DiskSpaceGBLabel=Butuh ruang kosong minimal [gb] GB.
DiskSpaceMBLabel=Butuh ruang kosong minimal [mb] MB.
CannotInstallToNetworkDrive=Tidak bisa memasang ke drive jaringan.
InvalidPath=Masukkan jalur lengkap beserta huruf drive, contoh:%n%nC:\Phoron

WizardSelectTasks=Pilih Tugas Tambahan
SelectTasksDesc=Tugas tambahan apa yang perlu dijalankan?
SelectTasksLabel2=Pilih tugas tambahan, lalu klik Lanjut.

WizardReady=Siap Memasang
ReadyLabel1=Pemasang siap memasang [name] di komputer Anda.
ReadyLabel2a=Klik Pasang untuk mulai, atau Kembali untuk mengubah pilihan.
ReadyLabel2b=Klik Pasang untuk mulai memasang.
ReadyMemoDir=Lokasi pemasangan:
ReadyMemoTasks=Tugas tambahan:
ReadyMemoGroup=Folder Start Menu:

WizardPreparing=Menyiapkan
PreparingDesc=Menyiapkan pemasangan [name].
WizardInstalling=Memasang
InstallingLabel=Mohon tunggu, [name] sedang dipasang...

FinishedHeadingLabel=Pemasangan [name] selesai
FinishedLabelNoIcons=[name] sudah terpasang di komputer Anda.
FinishedLabel=[name] sudah terpasang. Jalankan lewat ikon yang dibuat.
ClickFinish=Klik Selesai untuk menutup pemasang.
RunEntryExec=Jalankan %1

ConfirmUninstall=Yakin mau menghapus %1 beserta seluruh komponennya?
UninstallStatusLabel=Mohon tunggu, %1 sedang dihapus...
UninstalledAll=%1 berhasil dihapus dari komputer Anda.
UninstalledMost=%1 sudah dihapus.%n%nBeberapa item tidak bisa dihapus dan bisa Anda hapus manual.
StatusExtractFiles=Menyalin berkas...
StatusCreateIcons=Membuat pintasan...
StatusUninstalling=Menghapus %1...
ErrorTitle=Galat
SetupAborted=Pemasangan tidak selesai.%n%nPerbaiki masalahnya lalu jalankan pemasang lagi.

[CustomMessages]
id.TawarTutup=Phoron sedang berjalan.%n%n%1%nPemasang perlu menutupnya lebih dulu supaya berkasnya bisa diganti dan layanan tidak tertinggal hidup.%n%nTutup Phoron sekarang?
id.GagalTutup=Phoron masih berjalan dan tidak bisa ditutup pemasang.%n%nKemungkinan ia dijalankan sebagai Administrator sementara pemasang ini tidak.%n%nTutup Phoron lewat tombol "Keluar" di panel kirinya, lalu jalankan pemasang ini lagi.
id.BatalTutup=Pemasangan dihentikan karena Phoron masih berjalan. Tutup Phoron lebih dulu, lalu jalankan pemasang ini lagi.
id.PortSisa=Port berikut masih terpakai meski Phoron sudah ditutup:%n%n%1%nBiasanya ini sisa proses dari Phoron versi lama yang berakhir tanpa sempat membersihkan diri.%n%nPemasangan tetap bisa dilanjutkan - Phoron versi baru punya tombol "Hentikan proses yang tertinggal" di Beranda untuk membereskannya.%n%nLanjutkan?
id.NeedDotNet=Aplikasi ini memerlukan Microsoft .NET Framework 4.8, yang belum terpasang di komputer ini.%n%nWindows 10 versi 1903 ke atas dan Windows 11 sudah membawanya. Pada Windows 7 SP1 atau 8.1, .NET Framework 4.8 perlu dipasang sekali (gratis, dari Microsoft).%n%nBuka halaman unduhannya sekarang?
id.HapusData=Hapus juga proyek web, basis data, profil, dan versi PHP/Apache yang tersimpan di%n%n%1%n%nPilih Tidak (disarankan) kalau Anda masih membutuhkan proyek di folder www.

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
       ikut berakhir bersama induknya dan tidak meninggalkan port terkunci. }
  Exec(ExpandConstant('{sys}	askkill.exe'), '/IM Phoron.exe /T /F',
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
      port := 'Tidak ada port yang sedang dipegangnya.' + #13#10
    else
      port := 'Layanan berikut masih memegang portnya:' + #13#10 + #13#10 + port;

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
      Result := 'Pemasangan dibatalkan atas permintaan Anda.';
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
