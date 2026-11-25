using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace CompInfo.Wpf
{
    // One copy: a volume's contents -> this flash drive's folder (the TrayController keeps one folder per serial).
    // Runs on a background thread so the icon and menu do not freeze. Managed copy, no child processes.
    // Re-inserting the same flash drive does not make a new backup: only new and changed files are copied into
    // the same folder, and what was added is appended to a log inside it.
    public class CopyJob
    {
        readonly string sourceRoot;   // the volume root, e.g. "E:\"
        readonly string destFolder;   // this flash drive's folder (the same one across insertions)
        readonly string[] extensions; // extension filter or null
        readonly bool excludeExt;     // true: the filter lists what NOT to copy
        readonly long maxFileBytes;   // do not copy files larger than this; 0 = no limit
        readonly bool stubs;          // leave a placeholder instead of large files
        readonly bool zip;

        volatile bool cancel;
        public volatile bool Done;     // the thread finished (read by the UI thread on a timer)
        public volatile bool Success;  // the copy finished without a fatal error
        public volatile int Percent;   // progress 0..100 of the current phase (read by the UI thread)
        public volatile bool Packing;  // the copy is done and the ZIP is being written
        public event Action Finished;  // raised on the worker thread when the job is over
        public int Copied;             // new and updated files
        public int Stubbed;            // large files replaced with a placeholder
        public int Skipped;            // unchanged — skipped
        long totalBytes, doneBytes;    // for the progress percent
        Thread thread;

        public string Label { get; private set; }

        // The volume's service folders that should not be copied.
        static readonly string[] SkipDirs = { "System Volume Information", "$RECYCLE.BIN", "Found.000" };
        const string StubSuffix = " (пропущен).txt";
        const string LogName = "_журнал.txt";

        public CopyJob(string sourceRoot, string destFolder, string label, ArchiveOptions opts)
        {
            this.sourceRoot = sourceRoot;
            this.destFolder = destFolder;
            this.Label = label;
            this.extensions = opts.Extensions;
            this.excludeExt = opts.ExtensionsExclude;
            this.maxFileBytes = opts.MaxFileBytes;
            this.stubs = opts.Stubs;
            this.zip = opts.Zip;
        }

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "flash-copy" };
            thread.Start();
        }

        public void Cancel() { cancel = true; }

        void Run()
        {
            try
            {
                Directory.CreateDirectory(destFolder);
                totalBytes = Measure(sourceRoot);   // total size to process, for the percent
                CopyDir(sourceRoot, destFolder);
                if (!cancel) WriteLog();
                if (zip && !cancel) ZipUp();         // its own phase with its own percent
                Success = !cancel;
            }
            catch (Exception) { Success = false; }   // fatal (e.g. out of space) — mark as an error
            finally
            {
                Done = true;
                Action h = Finished;
                if (h != null) { try { h(); } catch (Exception) { } }
            }
        }

        // The extension filter: a list of what to copy, or (excludeExt) of what to skip.
        bool Wanted(string file)
        {
            if (extensions == null) return true;
            bool listed = Array.IndexOf(extensions, Path.GetExtension(file).ToLowerInvariant()) >= 0;
            return excludeExt ? !listed : listed;
        }

        void CopyDir(string src, string dst)
        {
            if (cancel) return;
            string[] files;
            try { files = Directory.GetFiles(src); } catch (Exception) { files = new string[0]; }
            foreach (string file in files)
            {
                if (cancel) return;
                if (!Wanted(file)) continue;

                string destPath = Path.Combine(dst, Path.GetFileName(file));
                string stubPath = destPath + StubSuffix;
                long len = -1;
                try { len = new FileInfo(file).Length; } catch (Exception) { }

                try
                {
                    // A file over the limit is not copied: optionally leave a placeholder describing it.
                    if (maxFileBytes > 0 && len > maxFileBytes)
                    {
                        TryDelete(destPath);                 // remove a real copy left from before, if any
                        if (stubs) WriteStub(file, stubPath, len);
                    }
                    else
                    {
                        TryDelete(stubPath);                 // the file fits now — remove an old placeholder
                        if (Unchanged(file, destPath, len)) Skipped++;   // incremental
                        else { File.Copy(file, destPath, true); Copied++; }
                    }
                }
                catch (Exception) { }   // a busy/inaccessible file — skip it, copy the rest

                doneBytes += len > 0 ? len : 0;
                if (totalBytes > 0) Percent = (int)Math.Min(100, doneBytes * 100 / totalBytes);
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(src); } catch (Exception) { dirs = new string[0]; }
            foreach (string dir in dirs)
            {
                if (cancel) return;
                string name = Path.GetFileName(dir);
                if (IsSkipped(name)) continue;
                try
                {
                    // Do not follow junctions/symlinks (guard against loops).
                    var attr = new DirectoryInfo(dir).Attributes;
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch (Exception) { continue; }
                string target = Path.Combine(dst, name);
                try { Directory.CreateDirectory(target); } catch (Exception) { continue; }
                CopyDir(dir, target);
            }
        }

        // Total size of the files that will be processed (for the progress percent); same filters as the copy.
        long Measure(string dir)
        {
            if (cancel) return 0;
            long sum = 0;
            string[] files;
            try { files = Directory.GetFiles(dir); } catch (Exception) { files = new string[0]; }
            foreach (string f in files)
            {
                if (!Wanted(f)) continue;
                try { sum += new FileInfo(f).Length; } catch (Exception) { }
            }
            string[] dirs;
            try { dirs = Directory.GetDirectories(dir); } catch (Exception) { dirs = new string[0]; }
            foreach (string d in dirs)
            {
                if (cancel) break;
                if (IsSkipped(Path.GetFileName(d))) continue;
                try { if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) continue; } catch (Exception) { continue; }
                sum += Measure(d);
            }
            return sum;
        }

        // The file is already copied and unchanged: same length and the source is not newer than the copy.
        static bool Unchanged(string src, string destPath, long srcLen)
        {
            try
            {
                var di = new FileInfo(destPath);
                if (!di.Exists) return false;
                if (srcLen >= 0 && di.Length != srcLen) return false;
                // FAT keeps time with a 2 s resolution — a small tolerance.
                return File.GetLastWriteTimeUtc(src) <= di.LastWriteTimeUtc.AddSeconds(2);
            }
            catch (Exception) { return false; }
        }

        static bool IsSkipped(string name)
        {
            foreach (string s in SkipDirs)
                if (string.Equals(name, s, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }

        // A placeholder instead of a large file: a text file with the original's name, size and date.
        void WriteStub(string original, string stubPath, long len)
        {
            DateTime mod = DateTime.MinValue;
            try { mod = new FileInfo(original).LastWriteTime; } catch (Exception) { }
            string text =
                "Файл не скопирован — больше лимита (" + HumanSize(maxFileBytes) + ")." + Environment.NewLine +
                "Оригинал: " + Path.GetFileName(original) + Environment.NewLine +
                "Размер:   " + (len >= 0 ? HumanSize(len) : "—") + Environment.NewLine +
                "Изменён:  " + (mod == DateTime.MinValue ? "—" : mod.ToString("yyyy-MM-dd HH:mm")) + Environment.NewLine;
            try { File.WriteAllText(stubPath, text, new UTF8Encoding(false)); Stubbed++; } catch (Exception) { }
        }

        // A log inside the flash drive's folder: one line per sync, so you can see what was added over time.
        void WriteLog()
        {
            if (Copied == 0 && Stubbed == 0) return;   // nothing new — do not clutter the log
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                    " — добавлено/обновлено: " + Copied +
                    (Stubbed > 0 ? ", пустышек: " + Stubbed : "") +
                    (Skipped > 0 ? ", без изменений: " + Skipped : "") +
                    Environment.NewLine;
                File.AppendAllText(Path.Combine(destFolder, LogName), line, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        static string HumanSize(long bytes)
        {
            if (bytes <= 0) return "0";
            string[] u = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0") : v.ToString("0.#")) + " " + u[i];
        }

        // ZIP mode: pack the folder into a matching .zip and remove the folder, so one flash drive = one file.
        // No compression (stored): the point is a single backup file, not saving space — and it is fast.
        // The folder is a staging area only, so in ZIP mode every sync is a full snapshot.
        void ZipUp()
        {
            string zipPath = destFolder + ".zip";
            string tmpPath = zipPath + ".tmp";
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch (Exception) { }

            string[] files = Directory.GetFiles(destFolder, "*", SearchOption.AllDirectories);
            long total = 0, done = 0;
            foreach (string f in files) { try { total += new FileInfo(f).Length; } catch (Exception) { } }
            Percent = 0;
            Packing = true;

            // Entry by entry (not ZipFile.CreateFromDirectory) so the packing has a real percent.
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string f in files)
                {
                    if (cancel) break;
                    string name = f.Substring(destFolder.Length).TrimStart('\\', '/').Replace('\\', '/');
                    archive.CreateEntryFromFile(f, name, CompressionLevel.NoCompression);
                    try { done += new FileInfo(f).Length; } catch (Exception) { }
                    Percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 100;
                }
            }
            if (cancel) { try { File.Delete(tmpPath); } catch (Exception) { } return; }

            // swap in the new archive only once it is complete
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch (Exception) { }
            File.Move(tmpPath, zipPath);
            try { Directory.Delete(destFolder, true); } catch (Exception) { }
        }
    }
}
