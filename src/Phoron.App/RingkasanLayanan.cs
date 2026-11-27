using System.Collections.Generic;
using Phoron.Core;

namespace Phoron.App
{
    /// <summary>
    /// Keadaan layanan PHP dalam satu baris: "PHP siap" dan rinciannya.
    ///
    /// Bagi kebanyakan orang tumpukan PHP adalah latar - ia menyala sendiri dan
    /// situsnya tinggal dibuka - jadi Beranda dan menu baki cukup menyebutnya
    /// dalam satu baris, dan baru mencolok kalau ada yang salah. Dihitung di
    /// satu tempat supaya kedua layar tidak pernah menyebut hal yang berbeda.
    /// </summary>
    internal sealed class RingkasanLayanan
    {
        public ServiceState Keadaan;
        public string Judul;
        public string Rincian;
        /// <summary>Ada yang menyala atau sedang dinyalakan: tombolnya berarti "hentikan".</summary>
        public bool AdaYangHidup;
        public bool Sibuk;

        public static RingkasanLayanan Hitung(Engine e)
        {
            var r = new RingkasanLayanan();
            var p = e.Active;
            bool pakaiWeb = p != null && p.PakaiWeb;
            bool pakaiDb = p != null && p.PakaiMySql;
            var web = e.Services.WebState;
            var db = e.Services.DbState;

            var dipakai = new List<ServiceState>();
            if (pakaiWeb) dipakai.Add(web);
            if (pakaiDb) dipakai.Add(db);

            r.Sibuk = dipakai.Contains(ServiceState.Menyalakan) || dipakai.Contains(ServiceState.Mematikan);
            r.AdaYangHidup = dipakai.Contains(ServiceState.Jalan) || dipakai.Contains(ServiceState.Menyalakan);

            if (p == null) { r.Keadaan = ServiceState.Berhenti; r.Judul = Lang.T("Belum ada profil"); }
            else if (dipakai.Count == 0) { r.Keadaan = ServiceState.Berhenti; r.Judul = Lang.T("Profil ini tidak memakai layanan"); }
            else if (dipakai.Contains(ServiceState.Gagal)) { r.Keadaan = ServiceState.Gagal; r.Judul = Lang.T("Ada layanan yang gagal"); }
            else if (dipakai.Contains(ServiceState.Menyalakan)) { r.Keadaan = ServiceState.Menyalakan; r.Judul = Lang.T("Menyalakan layanan..."); }
            else if (dipakai.Contains(ServiceState.Mematikan)) { r.Keadaan = ServiceState.Mematikan; r.Judul = Lang.T("Mematikan layanan..."); }
            else if (dipakai.TrueForAll(x => x == ServiceState.Jalan)) { r.Keadaan = ServiceState.Jalan; r.Judul = Lang.T("PHP siap"); }
            else if (dipakai.Contains(ServiceState.Jalan)) { r.Keadaan = ServiceState.Menyalakan; r.Judul = Lang.T("Sebagian layanan jalan"); }
            else { r.Keadaan = ServiceState.Berhenti; r.Judul = Lang.T("Layanan berhenti"); }

            var bagian = new List<string>();
            if (pakaiWeb)
            {
                var paket = p.WebServer == "nginx" ? e.Nginx : e.Apache;
                var nama = p.WebServer == "nginx" ? "Nginx" : "Apache";
                bagian.Add(nama + (paket != null ? " " + paket.Version : "") + " " + Lang.T(web.ToString().ToLowerInvariant()));
            }
            if (e.Php != null) bagian.Add("PHP " + e.Php.Version);
            if (pakaiDb)
                bagian.Add("MySQL" + (e.MySql != null ? " " + e.MySql.Version : "") + " " + Lang.T(db.ToString().ToLowerInvariant()));
            if (e.Sites != null && e.Sites.Count > 0) bagian.Add(Lang.T("{0} situs", e.Sites.Count));
            r.Rincian = string.Join(" · ", bagian);
            return r;
        }
    }
}
