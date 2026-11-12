using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CompInfo
{
    // Public updater for the standalone application. It accepts only SUSpector.exe packages.
    public static class SuspectorUpdate
    {
        // The main site passes /api/suspector/* to the Comp Info cloud (chagear-site/worker.js);
        // versions before 2026.11.13 ask comp-info.chagear.top directly, which keeps working.
        public const string ApiUrl = "https://chagear.top/api/suspector";
        public const string ExeName = "SUSpector.exe";

        public static JsonObject Latest()
        {
            using (WebClient client = Client())
            {
                string json = client.DownloadString(ApiUrl + "/latest");
                return Json.ParseObject(json);
            }
        }

        public static void Download(string version, string path)
        {
            if (!IsVersion(version)) throw new InvalidDataException(L.T("Облако вернуло недопустимый номер версии."));
            using (WebClient client = Client()) client.DownloadFile(ApiUrl + "/" + version + "/package", path);
        }

        static WebClient Client()
        {
            WebClient client = new WebClient();
            client.Encoding = System.Text.Encoding.UTF8;
            client.Headers[HttpRequestHeader.UserAgent] = "SUSpector/" + AppVersion.Text;
            return client;
        }

        // Version strings come from the cloud and go into a URL and a file name: only digits and dots.
        public static bool IsVersion(string value)
        {
            return value != null && Regex.IsMatch(value, @"^\d{4}\.\d{2}\.\d{2}\.\d{1,4}$");
        }

        public static Version ParseVersion(string value)
        {
            Version parsed;
            return Version.TryParse(value ?? "", out parsed) ? parsed : null;
        }

        public static string Sha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream input = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        // Verifies a package and replaces target: the running executable, whatever name the user gave it
        // (browsers save a second download as "SUSpector (1).exe"). The old executable remains as
        // <target>.old until the freshly started version removes it.
        public static void Install(string target, string zipPath, string expectedVersion)
        {
            string extract = Path.Combine(Path.GetTempPath(), "suspector-update-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    if (archive.Entries.Count != 2) throw new InvalidDataException(L.T("Архив SUSpector содержит лишние файлы."));
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = entry.FullName.Replace('/', '\\');
                        if (!string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(name, ExeName, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(L.T("Недопустимый файл в архиве: ") + entry.FullName);
                    }
                }
                ZipFile.ExtractToDirectory(zipPath, extract);
                string manifestPath = Path.Combine(extract, "manifest.json");
                if (!File.Exists(manifestPath)) throw new InvalidDataException(L.T("В архиве нет manifest.json."));
                JsonObject manifest = Files.ReadRecord(manifestPath);
                if (manifest.Text("version") != expectedVersion)
                    throw new InvalidDataException(L.T("Архив содержит версию ") + manifest.Text("version") + L.T(", ожидалась ") + expectedVersion + ".");
                IList files = manifest["files"] as IList;
                if (files == null || files.Count != 1) throw new InvalidDataException(L.T("Архив SUSpector должен содержать один файл программы."));
                JsonObject file = files[0] as JsonObject;
                string nameInManifest = file == null ? "" : file.Text("path") ?? "";
                if (!string.Equals(nameInManifest, ExeName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(L.T("Недопустимый файл в архиве: ") + nameInManifest);
                string source = Path.Combine(extract, ExeName);
                string sha256 = (file.Text("sha256") ?? "").ToLowerInvariant();
                if (!File.Exists(source)) throw new InvalidDataException(L.T("В архиве нет файла ") + ExeName + ".");
                if (new FileInfo(source).Length != Convert.ToInt64(file["size"]) || Sha256(source) != sha256)
                    throw new InvalidDataException(L.T("Файл ") + ExeName + L.T(" в архиве не совпадает с manifest.json."));

                string old = target + ".old";
                if (!File.Exists(target)) throw new FileNotFoundException(L.T("Не найден установленный SUSpector.exe."), target);
                if (File.Exists(old)) File.Delete(old);
                // A running executable cannot be overwritten, but Windows lets it be renamed.
                File.Move(target, old);
                try
                {
                    File.Copy(source, target);
                    if (Sha256(target) != sha256) throw new IOException(L.T("Новый SUSpector.exe записан с ошибкой."));
                }
                catch
                {
                    try { if (File.Exists(target)) File.Delete(target); } catch (Exception) { }
                    if (File.Exists(old)) File.Move(old, target);
                    throw;
                }
            }
            finally
            {
                try { if (Directory.Exists(extract)) Directory.Delete(extract, true); } catch (Exception) { }
            }
        }

        public static void RemoveLeftovers(string executablePath)
        {
            try
            {
                string old = executablePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch (Exception) { }
        }
    }
}
