using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CompInfo
{
    // A file whose name or text contains the phrase.
    public class SearchHit
    {
        public string Path, DisplayPath, Type, Note, Fragment, ToolTip;
        public int Count;
        public bool InName, Recycle;
    }

    public class SearchProgress
    {
        public bool Collecting;
        public int Found, Checked, Total, Matches;
    }

    public class SearchOptions
    {
        public string Phrase = "";
        public List<string> Roots = new List<string>();
        public bool Contents = true;
        public bool Recycle = true;
    }

    public class SearchResult
    {
        public readonly List<SearchHit> Hits = new List<SearchHit>();
        public int Files, Documents, SkippedFolders, Unreadable, MatchCount;
        public bool Limit, Cancelled;
        public string RecycleError = "";
    }

    // Free-text search over file names and the text of documents, on all local disks like the ДСК check.
    // Windows Search is not used: its index covers only some folders, is often switched off and does not see the Recycle Bin.
    public static class DocumentSearch
    {
        public const int MaxHits = 500;
        const long MaxDocumentBytes = 50L * 1024 * 1024, MaxBinaryBytes = 20L * 1024 * 1024, MaxTextBytes = 20L * 1024 * 1024;

        static readonly HashSet<string> OpenXml = Set(".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xltx", ".xltm", ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm");
        static readonly HashSet<string> OpenDocument = Set(".odt", ".ods", ".odp", ".ott", ".ots", ".otp");
        static readonly HashSet<string> Binary = Set(".doc", ".dot", ".xls", ".xlt", ".ppt", ".pot", ".pps");
        static readonly HashSet<string> Plain = Set(".txt", ".csv", ".log", ".md", ".xml", ".json", ".htm", ".html", ".ini", ".cfg");
        static readonly HashSet<string> Rtf = Set(".rtf");

        static HashSet<string> Set(params string[] values) { return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase); }

        // Formats whose text is read; other files are found by name only (PDF among them: its text is usually compressed
        // with font-specific encodings and cannot be read reliably without a PDF library).
        public static bool ReadsContents(string path)
        {
            string e = System.IO.Path.GetExtension(path ?? "");
            return OpenXml.Contains(e) || OpenDocument.Contains(e) || Binary.Contains(e) || Plain.Contains(e) || Rtf.Contains(e);
        }

        public static List<string> LocalDisks()
        {
            List<string> roots = new List<string>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                try { if (drive.IsReady && drive.DriveType == DriveType.Fixed) roots.Add(drive.RootDirectory.FullName); } catch (Exception) { }
            return roots;
        }

        public static string Normalize(string text)
        {
            return Regex.Replace(text ?? "", @"\s+", " ").Trim();
        }

        public static SearchResult Run(SearchOptions options, CancellationToken cancel, Action<SearchProgress> progress, Action<SearchHit> found)
        {
            SearchResult result = new SearchResult();
            string phrase = Normalize(options.Phrase);
            if (phrase.Length == 0) return result;
            List<Candidate> documents = new List<Candidate>();
            Report(progress, true, 0, 0, 0, 0);
            try
            {
                foreach (string root in options.Roots) Walk(root, phrase, options, result, documents, cancel, progress, found);
                if (options.Recycle) CollectRecycleBin(phrase, options, result, documents, progress, found);
                result.Documents = documents.Count;
                Report(progress, false, result.Files, 0, documents.Count, result.MatchCount);
                for (int i = 0; i < documents.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Inspect(documents[i], phrase, true, result, found);
                    if ((i + 1) % 10 == 0 || i + 1 == documents.Count) Report(progress, false, result.Files, i + 1, documents.Count, result.MatchCount);
                }
            }
            catch (OperationCanceledException) { result.Cancelled = true; }
            return result;
        }

        class Candidate
        {
            public string Path, DisplayPath, DeletedAt;
            public bool Recycle;
        }

        static void Report(Action<SearchProgress> progress, bool collecting, int found, int done, int total, int matches)
        {
            if (progress == null) return;
            SearchProgress p = new SearchProgress();
            p.Collecting = collecting; p.Found = found; p.Checked = done; p.Total = total; p.Matches = matches;
            progress(p);
        }

        static readonly HashSet<string> SkipTopFolders = Set("windows", "program files", "program files (x86)", "programdata",
            "$recycle.bin", "system volume information", "recovery", "perflogs", "msocache", "config.msi");

        static void Walk(string root, string phrase, SearchOptions options, SearchResult result, List<Candidate> documents,
            CancellationToken cancel, Action<SearchProgress> progress, Action<SearchHit> found)
        {
            string top = System.IO.Path.GetFullPath(root).TrimEnd('\\');
            // Skipping system folders only makes sense at the root of a disk, not inside a folder the user picked.
            bool disk = System.IO.Path.GetPathRoot(top).TrimEnd('\\').Equals(top, StringComparison.OrdinalIgnoreCase);
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                try
                {
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        result.Files++;
                        Candidate c = new Candidate();
                        c.Path = c.DisplayPath = file;
                        Consider(c, phrase, options, result, documents, found);
                        if (result.Files % 200 == 0) Report(progress, true, result.Files, 0, 0, result.MatchCount);
                    }
                    foreach (string child in Directory.GetDirectories(directory))
                    {
                        DirectoryInfo info = new DirectoryInfo(child);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (disk && string.Equals(directory.TrimEnd('\\'), top, StringComparison.OrdinalIgnoreCase) && SkipTopFolders.Contains(info.Name)) continue;
                        pending.Push(child);
                    }
                }
                catch (UnauthorizedAccessException) { result.SkippedFolders++; }
                catch (IOException) { result.SkippedFolders++; }
            }
        }

        static void CollectRecycleBin(string phrase, SearchOptions options, SearchResult result, List<Candidate> documents,
            Action<SearchProgress> progress, Action<SearchHit> found)
        {
            try
            {
                foreach (RecycleFile file in RecycleBin.Files())
                {
                    if (!File.Exists(file.Path)) continue;
                    result.Files++;
                    Candidate c = new Candidate();
                    c.Path = file.Path;
                    c.DisplayPath = file.OriginalFolder.Length == 0 ? file.OriginalName : System.IO.Path.Combine(file.OriginalFolder, file.OriginalName);
                    c.DeletedAt = file.DeletedAt;
                    c.Recycle = true;
                    Consider(c, phrase, options, result, documents, found);
                }
            }
            catch (Exception e) { result.RecycleError = e.Message; }
        }

        // Documents are read after the walk (with a real progress bar); anything else is matched by name at once.
        static void Consider(Candidate c, string phrase, SearchOptions options, SearchResult result, List<Candidate> documents, Action<SearchHit> found)
        {
            if (options.Contents && ReadsContents(c.DisplayPath)) documents.Add(c);
            else Inspect(c, phrase, false, result, found);
        }

        static void Inspect(Candidate c, string phrase, bool contents, SearchResult result, Action<SearchHit> found)
        {
            string name = System.IO.Path.GetFileName(c.DisplayPath);
            bool inName = Normalize(name).IndexOf(phrase, StringComparison.CurrentCultureIgnoreCase) >= 0;
            int count = 0;
            string fragment = "";
            if (contents)
            {
                string text = null;
                try { text = ReadText(c.Path, System.IO.Path.GetExtension(c.DisplayPath)); }
                catch (Exception) { result.Unreadable++; }
                if (text != null) count = Count(text, phrase, out fragment);
            }
            if (!inName && count == 0) return;
            result.MatchCount++;
            if (result.Hits.Count >= MaxHits) { result.Limit = true; return; }
            SearchHit hit = new SearchHit();
            hit.Path = c.Path;
            hit.DisplayPath = c.DisplayPath;
            hit.Recycle = c.Recycle;
            hit.InName = inName;
            hit.Count = count;
            hit.Fragment = fragment.Length > 0 ? fragment : inName ? L.T("в имени файла") : "";
            hit.Type = TypeOf(c.DisplayPath);
            List<string> tags = SecurityAudit.LocationTags(c.DisplayPath, c.Recycle);
            string where = string.Join(" ", tags.ToArray());
            string what = count > 0 ? count.ToString() : L.T("имя");
            hit.Note = where.Length > 0 ? where + " · " + what : what;
            List<string> tip = new List<string> { c.DisplayPath };
            if (inName) tip.Add(L.T("Совпадение в имени файла"));
            if (count > 0) tip.Add(L.T("Совпадений в тексте: ") + count);
            if (!string.IsNullOrEmpty(c.DeletedAt)) tip.Add(L.T("Удалён: ") + c.DeletedAt);
            hit.ToolTip = string.Join("\r\n", tip.ToArray());
            result.Hits.Add(hit);
            if (found != null) found(hit);
        }

        public static string TypeOf(string path)
        {
            string e = System.IO.Path.GetExtension(path ?? "").ToLowerInvariant();
            if (e.StartsWith(".doc") || e.StartsWith(".dot") || e == ".rtf" || e == ".odt" || e == ".ott") return "WORD";
            if (e.StartsWith(".xls") || e.StartsWith(".xlt") || e == ".csv" || e == ".ods" || e == ".ots") return "EXCEL";
            if (e.StartsWith(".ppt") || e.StartsWith(".pot") || e.StartsWith(".pps") || e == ".odp" || e == ".otp") return "PPT";
            if (e == ".pdf") return "PDF";
            if (Plain.Contains(e)) return "TXT";
            return e.Length > 1 ? e.Substring(1).ToUpperInvariant() : "";
        }

        // Occurrences of the phrase (case and line breaks ignored) and a short piece of text around the first one.
        public static int Count(string text, string phrase, out string fragment)
        {
            fragment = "";
            text = Normalize(text);
            phrase = Normalize(phrase);
            if (phrase.Length == 0) return 0;
            int count = 0, start = 0, first = -1;
            while (start < text.Length)
            {
                int at = text.IndexOf(phrase, start, StringComparison.CurrentCultureIgnoreCase);
                if (at < 0) break;
                if (first < 0) first = at;
                count++;
                start = at + phrase.Length;
            }
            if (first >= 0)
            {
                int from = Math.Max(0, first - 40), to = Math.Min(text.Length, first + phrase.Length + 60);
                fragment = (from > 0 ? "…" : "") + text.Substring(from, to - from).Trim() + (to < text.Length ? "…" : "");
            }
            return count;
        }

        // Text of a document, or null for formats that are not read. Throws when the file cannot be read.
        public static string ReadText(string path, string extension)
        {
            long length = new FileInfo(path).Length;
            if (OpenXml.Contains(extension))
                return length > MaxDocumentBytes ? null : ZipText(path, delegate (string n)
                {
                    // Only the text parts: styles and font tables would match on font and style names.
                    return n.StartsWith("word/document") || n.StartsWith("word/header") || n.StartsWith("word/footer") ||
                        n.StartsWith("word/footnotes") || n.StartsWith("word/endnotes") || n.StartsWith("word/comments") || n.StartsWith("xl/sharedStrings") || n.StartsWith("xl/worksheets/") || n.StartsWith("ppt/slides/") || n.StartsWith("ppt/notesSlides/");
                });
            if (OpenDocument.Contains(extension))
                return length > MaxDocumentBytes ? null : ZipText(path, delegate (string n) { return n == "content.xml"; });
            if (Binary.Contains(extension)) return length > MaxBinaryBytes ? null : BinaryText(File.ReadAllBytes(path));
            if (Rtf.Contains(extension)) return length > MaxTextBytes ? null : RtfText(DecodeText(File.ReadAllBytes(path)));
            if (Plain.Contains(extension)) return length > MaxTextBytes ? null : DecodeText(File.ReadAllBytes(path));
            return null;
        }

        static string ZipText(string path, Predicate<string> wanted)
        {
            StringBuilder all = new StringBuilder();
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                long read = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || !wanted(name)) continue;
                    if (entry.Length > 16L * 1024 * 1024 || read + entry.Length > 64L * 1024 * 1024) break;
                    read += entry.Length;
                    using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8, true))
                        all.Append(XmlText(reader.ReadToEnd())).Append(' ');
                }
            }
            return all.ToString();
        }

        // Word splits a word into several runs (<w:r><w:t>Д</w:t></w:r><w:r><w:t>СК</w:t></w:r>) when formatting or
        // spell checking changes inside it: tags are removed without a space, and only paragraph, cell, tab and line
        // ends become spaces, so the word is found whole.
        static readonly Regex Breaks = new Regex(@"</(w:p|a:p|text:p|text:h|si|c|row|table:table-cell)>|<(w:tab|w:br|w:cr|a:br|text:s|text:tab|text:line-break)\b[^>]*>", RegexOptions.IgnoreCase);
        static readonly Regex Tags = new Regex("<[^>]*>");

        public static string XmlText(string xml)
        {
            string spaced = Breaks.Replace(xml ?? "", " ");
            return WebUtility.HtmlDecode(Tags.Replace(spaced, ""));
        }

        // Old binary Office files keep text as UTF-16 (Cyrillic always) or as single-byte characters.
        // Reading the bytes in both ways finds the phrase without parsing the format.
        public static string BinaryText(byte[] bytes)
        {
            StringBuilder text = new StringBuilder();
            text.Append(Readable(Encoding.Unicode.GetString(bytes))).Append(' ');
            if (bytes.Length > 1) text.Append(Readable(Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1))).Append(' ');
            text.Append(Readable(Encoding.GetEncoding(1251).GetString(bytes)));
            return text.ToString();
        }

        // Control characters and binary noise become spaces, so they do not glue to the words around them.
        static string Readable(string value)
        {
            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (char.IsControl(chars[i]) || char.IsSurrogate(chars[i]) || chars[i] == '\uFFFD') chars[i] = ' ';
            return new string(chars);
        }

        // UTF-8 / UTF-16 by BOM, then UTF-8 if the bytes are valid UTF-8, otherwise the Cyrillic Windows code page.
        public static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (ArgumentException) { return Encoding.GetEncoding(1251).GetString(bytes); }
        }

        // RTF keeps non-Latin letters as \'e4 (code page bytes) or \uN with N the UTF-16 code; control words are dropped.
        static readonly Regex RtfToken = new Regex(@"\\'([0-9a-fA-F]{2})|\\u(-?\d+) ?|\\(par|line|tab|cell|row)\b ?|\\[a-zA-Z]+-?\d* ?|\\([{}\\])|[{}]");

        public static string RtfText(string rtf)
        {
            Encoding codePage = Encoding.GetEncoding(1251);
            Match ansi = Regex.Match(rtf ?? "", @"\\ansicpg(\d+)");
            if (ansi.Success) try { codePage = Encoding.GetEncoding(int.Parse(ansi.Groups[1].Value)); } catch (Exception) { }
            rtf = rtf ?? "";
            List<byte> pending = new List<byte>();
            StringBuilder text = new StringBuilder();
            int last = 0;
            // After a \uN letter Word writes a fallback for old readers (\'e4 or a «?»): one character, skipped.
            int fallback = 0;
            foreach (Match m in RtfToken.Matches(rtf))
            {
                if (m.Index > last)
                {
                    int from = last;
                    if (fallback > 0) { from++; fallback = 0; }
                    Flush(pending, codePage, text);
                    if (from < m.Index) text.Append(rtf, from, m.Index - from);
                }
                last = m.Index + m.Length;
                if (m.Groups[1].Success)
                {
                    if (fallback > 0) { fallback = 0; continue; }
                    pending.Add(Convert.ToByte(m.Groups[1].Value, 16));
                    continue;
                }
                Flush(pending, codePage, text);
                fallback = 0;
                if (m.Groups[2].Success)
                {
                    int code = int.Parse(m.Groups[2].Value);
                    text.Append((char)(code < 0 ? code + 65536 : code));
                    fallback = 1;
                }
                else if (m.Groups[3].Success) text.Append(' ');
                else if (m.Groups[4].Success) text.Append(m.Groups[4].Value);
            }
            Flush(pending, codePage, text);
            int tail = last + (fallback > 0 ? 1 : 0);
            if (tail < rtf.Length) text.Append(rtf, tail, rtf.Length - tail);
            return text.ToString();
        }

        static void Flush(List<byte> pending, Encoding codePage, StringBuilder text)
        {
            if (pending.Count == 0) return;
            text.Append(codePage.GetString(pending.ToArray()));
            pending.Clear();
        }
    }
}
