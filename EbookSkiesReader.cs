using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;

namespace EbookSkies
{
    internal sealed class BookItem
    {
        public string Title, Href, FilePath, Fragment;
        public int SpineIndex;
        public override string ToString() { return Title; }
    }

    internal sealed class EpubBook
    {
        public string Title, RootDirectory;
        public readonly List<BookItem> Spine = new List<BookItem>();
        public readonly List<BookItem> Toc = new List<BookItem>();

        static string Attr(XElement e, string name)
        {
            if (e == null) return null;
            XAttribute a = e.Attributes().FirstOrDefault(x => x.Name.LocalName == name);
            return a == null ? null : a.Value;
        }
        static string TextOf(XElement e, string name)
        {
            XElement c = e == null ? null : e.Elements().FirstOrDefault(x => x.Name.LocalName == name);
            return c == null ? null : c.Value.Trim();
        }
        static string SafePath(string root, string relative)
        {
            relative = Uri.UnescapeDataString(relative.Replace('/', Path.DirectorySeparatorChar));
            string full = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The EPUB contains a path outside its archive.");
            return full;
        }
        static string ResolveHref(string baseFile, string href)
        {
            if (String.IsNullOrEmpty(href)) return "";
            Uri u = new Uri(new Uri(baseFile), href.Replace(" ", "%20"));
            return Uri.UnescapeDataString(u.LocalPath);
        }

        public static EpubBook Open(string epubPath)
        {
            string temp = Path.Combine(Path.GetTempPath(), "EbookSkies-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(epubPath))
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (String.IsNullOrEmpty(entry.Name)) continue;
                    string target = SafePath(temp, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream src = entry.Open())
                    using (FileStream dst = File.Create(target)) src.CopyTo(dst);
                }

                string container = Path.Combine(temp, "META-INF", "container.xml");
                XDocument cx = XDocument.Load(container);
                XElement rootfile = cx.Descendants().FirstOrDefault(x => x.Name.LocalName == "rootfile");
                if (rootfile == null) throw new InvalidDataException("container.xml has no rootfile.");
                string opfPath = SafePath(temp, Attr(rootfile, "full-path"));
                XDocument opf = XDocument.Load(opfPath);
                XElement package = opf.Root;
                if (package == null) throw new InvalidDataException("The package document is empty.");

                EpubBook book = new EpubBook();
                book.RootDirectory = temp;
                XElement metadata = package.Elements().FirstOrDefault(x => x.Name.LocalName == "metadata");
                book.Title = TextOf(metadata, "title");
                if (String.IsNullOrWhiteSpace(book.Title)) book.Title = Path.GetFileNameWithoutExtension(epubPath);

                XElement manifest = package.Elements().FirstOrDefault(x => x.Name.LocalName == "manifest");
                XElement spine = package.Elements().FirstOrDefault(x => x.Name.LocalName == "spine");
                if (manifest == null || spine == null) throw new InvalidDataException("The EPUB package is missing its manifest or spine.");
                Dictionary<string, string> paths = new Dictionary<string, string>();
                Dictionary<string, XElement> items = new Dictionary<string, XElement>();
                foreach (XElement item in manifest.Elements().Where(x => x.Name.LocalName == "item"))
                {
                    string id = Attr(item, "id"), href = Attr(item, "href");
                    if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(href)) continue;
                    paths[id] = ResolveHref(opfPath, href);
                    items[id] = item;
                }
                foreach (XElement r in spine.Elements().Where(x => x.Name.LocalName == "itemref"))
                {
                    string idref = Attr(r, "idref");
                    if (idref == null || !paths.ContainsKey(idref) || !File.Exists(paths[idref])) continue;
                    book.Spine.Add(new BookItem { Href = paths[idref], FilePath = paths[idref],
                        SpineIndex = book.Spine.Count, Title = Path.GetFileNameWithoutExtension(paths[idref]) });
                }
                if (book.Spine.Count == 0) throw new InvalidDataException("No readable chapters were found in the EPUB spine.");

                XElement navItem = items.Values.FirstOrDefault(x => (Attr(x, "properties") ?? "").Split(' ').Contains("nav"));
                if (navItem != null && paths.ContainsKey(Attr(navItem, "id"))) book.ReadNav(paths[Attr(navItem, "id")]);
                if (book.Toc.Count == 0)
                {
                    XElement ncx = items.Values.FirstOrDefault(x => (Attr(x, "media-type") ?? "") == "application/x-dtbncx+xml");
                    if (ncx != null) book.ReadNcx(paths[Attr(ncx, "id")]);
                }
                if (book.Toc.Count == 0)
                    foreach (BookItem bi in book.Spine)
                        book.Toc.Add(new BookItem { Title = bi.Title, Href = bi.Href, FilePath = bi.FilePath, SpineIndex = bi.SpineIndex });
                book.SanitizeContent();
                return book;
            }
            catch
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
                throw;
            }
        }

        void AddToc(string title, string href, string baseFile)
        {
            if (String.IsNullOrWhiteSpace(href)) return;
            try
            {
                string clean = href.Replace('\\', '/');
                string pathPart = clean.Split('#')[0];
                string fragment = clean.Contains("#") ? clean.Substring(clean.IndexOf('#') + 1) : null;
                string file = ResolveHref(baseFile, pathPart);
                int index = Spine.FindIndex(x => String.Equals(x.FilePath, file, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    index = Spine.FindIndex(x => String.Equals(Path.GetFileName(x.FilePath), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
                if (index < 0) return;
                Toc.Add(new BookItem { Title = String.IsNullOrWhiteSpace(title) ? Spine[index].Title : title.Trim(),
                    Href = href, FilePath = Spine[index].FilePath, SpineIndex = index, Fragment = fragment });
            }
            catch { }
        }
        void ReadNav(string navPath)
        {
            try
            {
                XDocument doc = XDocument.Load(navPath);
                XElement nav = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "nav" &&
                    x.Attributes().Any(a => a.Name.LocalName == "type" && a.Value.EndsWith(":toc", StringComparison.OrdinalIgnoreCase)));
                if (nav == null) nav = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "nav");
                if (nav == null) return;
                foreach (XElement a in nav.Descendants().Where(x => x.Name.LocalName == "a"))
                    AddToc(a.Value, Attr(a, "href"), navPath);
            }
            catch { }
        }
        void ReadNcx(string ncxPath)
        {
            try
            {
                XDocument doc = XDocument.Load(ncxPath);
                foreach (XElement p in doc.Descendants().Where(x => x.Name.LocalName == "navPoint"))
                {
                    XElement label = p.Descendants().FirstOrDefault(x => x.Name.LocalName == "text");
                    XElement content = p.Elements().FirstOrDefault(x => x.Name.LocalName == "content");
                    AddToc(label == null ? null : label.Value, Attr(content, "src"), ncxPath);
                }
            }
            catch { }
        }
        void SanitizeContent()
        {
            foreach (BookItem item in Spine)
            {
                string ext = Path.GetExtension(item.FilePath).ToLowerInvariant();
                if (ext != ".xhtml" && ext != ".html" && ext != ".htm") continue;
                try
                {
                    XDocument doc = XDocument.Load(item.FilePath, LoadOptions.PreserveWhitespace);
                    foreach (XElement script in doc.Descendants().Where(x => x.Name.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase)).ToList()) script.Remove();
                    foreach (XElement el in doc.DescendantsAndSelf())
                        foreach (XAttribute a in el.Attributes().Where(x => x.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ToList()) a.Remove();
                    doc.Save(item.FilePath);
                }
                catch { }
            }
        }
    }

    internal sealed class ReaderForm : Form
    {
        WebBrowser browser;
        TreeView toc;
        ToolStripStatusLabel status;
        ToolStripButton previousButton, nextButton;
        ToolStripTextBox searchBox;
        EpubBook book;
        int chapterIndex = -1, fontPercent = 100;

        public ReaderForm()
        {
            Text = "EbookSkies Reader"; Width = 1000; Height = 700; MinimumSize = new Size(620, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BuildUi();
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1])) LoadBook(args[1]);
        }
        void BuildUi()
        {
            ToolStrip bar = new ToolStrip(); bar.GripStyle = ToolStripGripStyle.Hidden; bar.Dock = DockStyle.Top;
            ToolStripButton open = new ToolStripButton("Open EPUB");
            previousButton = new ToolStripButton("◀ Previous"); nextButton = new ToolStripButton("Next ▶");
            ToolStripButton smaller = new ToolStripButton("A−"), larger = new ToolStripButton("A+");
            searchBox = new ToolStripTextBox(); searchBox.AutoSize = false; searchBox.Width = 150;
            ToolStripButton find = new ToolStripButton("Find");
            open.Click += (s, e) => ChooseBook();
            previousButton.Click += (s, e) => TurnPage(-1); nextButton.Click += (s, e) => TurnPage(1);
            smaller.Click += (s, e) => ChangeFont(-10); larger.Click += (s, e) => ChangeFont(10);
            find.Click += (s, e) => FindText();
            searchBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { FindText(); e.SuppressKeyPress = true; } };
            bar.Items.Add(open); bar.Items.Add(new ToolStripSeparator()); bar.Items.Add(previousButton); bar.Items.Add(nextButton);
            bar.Items.Add(new ToolStripSeparator()); bar.Items.Add(new ToolStripLabel("Text")); bar.Items.Add(smaller); bar.Items.Add(larger);
            bar.Items.Add(new ToolStripSeparator()); bar.Items.Add(searchBox); bar.Items.Add(find);
            Controls.Add(bar);

            SplitContainer split = new SplitContainer(); split.Dock = DockStyle.Fill; split.SplitterDistance = 235;
            toc = new TreeView(); toc.Dock = DockStyle.Fill; toc.HideSelection = false;
            toc.AfterSelect += (s, e) => { BookItem bi = e.Node.Tag as BookItem; if (bi != null) OpenChapter(bi.SpineIndex, bi.Fragment); };
            split.Panel1.Controls.Add(toc);
            browser = new WebBrowser(); browser.Dock = DockStyle.Fill; browser.ScriptErrorsSuppressed = true;
            browser.IsWebBrowserContextMenuEnabled = false; browser.WebBrowserShortcutsEnabled = true; browser.AllowWebBrowserDrop = false;
            browser.Navigating += (s, e) => { if (e.Url != null && !e.Url.IsFile && e.Url.Scheme != "about") e.Cancel = true; };
            browser.DocumentCompleted += (s, e) => ApplyReaderStyle();
            split.Panel2.Controls.Add(browser); Controls.Add(split); split.BringToFront();
            StatusStrip bottom = new StatusStrip(); status = new ToolStripStatusLabel("Open an EPUB to begin.");
            bottom.Items.Add(status); Controls.Add(bottom); bottom.BringToFront();
            previousButton.Enabled = nextButton.Enabled = false; KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Space) { TurnPage(1); e.Handled = true; }
                else if (e.KeyCode == Keys.PageUp) { TurnPage(-1); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.O) ChooseBook();
                else if (e.Control && e.KeyCode == Keys.F) { searchBox.Focus(); searchBox.SelectAll(); }
            };
            FormClosed += (s, e) => { try { if (book != null && Directory.Exists(book.RootDirectory)) Directory.Delete(book.RootDirectory, true); } catch { } };
        }
        void ChooseBook()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Open an EPUB book"; dlg.Filter = "EPUB books (*.epub)|*.epub|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadBook(dlg.FileName);
            }
        }
        void LoadBook(string path)
        {
            try
            {
                EpubBook fresh = EpubBook.Open(path);
                if (book != null) { try { Directory.Delete(book.RootDirectory, true); } catch { } }
                book = fresh; chapterIndex = -1; Text = book.Title + " — EbookSkies Reader";
                toc.BeginUpdate(); toc.Nodes.Clear();
                foreach (BookItem item in book.Toc) { TreeNode node = new TreeNode(item.Title); node.Tag = item; toc.Nodes.Add(node); }
                toc.EndUpdate(); OpenChapter(0, null);
                status.Text = Path.GetFileName(path) + "  |  " + book.Spine.Count + " chapters";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't open this EPUB.\r\n\r\n" + ex.Message, "EbookSkies Reader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        void OpenChapter(int index, string fragment)
        {
            if (book == null || index < 0 || index >= book.Spine.Count) return;
            chapterIndex = index; BookItem item = book.Spine[index];
            if (!String.IsNullOrEmpty(fragment)) browser.DocumentCompleted += JumpToFragmentOnce(fragment);
            browser.Navigate(new Uri(item.FilePath));
            previousButton.Enabled = chapterIndex > 0; nextButton.Enabled = chapterIndex < book.Spine.Count - 1;
            status.Text = book.Title + "  |  Chapter " + (chapterIndex + 1) + " of " + book.Spine.Count;
        }
        WebBrowserDocumentCompletedEventHandler JumpToFragmentOnce(string fragment)
        {
            WebBrowserDocumentCompletedEventHandler handler = null;
            handler = (s, e) =>
            {
                browser.DocumentCompleted -= handler;
                try
                {
                    string escaped = fragment.Replace("\\", "\\\\").Replace("'", "\\'");
                    browser.Document.InvokeScript("eval", new object[] { "var el=document.getElementById('" + escaped + "')||document.getElementsByName('" + escaped + "')[0];if(el)el.scrollIntoView(true);" });
                }
                catch { }
            };
            return handler;
        }
        void ApplyReaderStyle()
        {
            try
            {
                HtmlElement head = browser.Document.GetElementsByTagName("head").Cast<HtmlElement>().FirstOrDefault();
                if (head == null) return;
                HtmlElement old = browser.Document.GetElementById("ebookskies-reader-style");
                if (old != null) old.OuterHtml = "";
                HtmlElement style = browser.Document.CreateElement("style"); style.Id = "ebookskies-reader-style";
                style.SetAttribute("type", "text/css");
                style.InnerText = "html,body{font-size:" + fontPercent + "% !important;line-height:1.55 !important;} body{padding:1.4em 2em !important;max-width:52em !important;margin:0 auto !important;overflow-wrap:break-word;} img,svg{max-width:100% !important;height:auto !important;} pre{white-space:pre-wrap !important;}";
                head.AppendChild(style);
            }
            catch { }
        }
        void ChangeFont(int delta) { fontPercent = Math.Max(60, Math.Min(200, fontPercent + delta)); ApplyReaderStyle(); }
        void TurnPage(int direction)
        {
            if (book == null || browser.Document == null) return;
            try
            {
                object result = browser.Document.InvokeScript("eval", new object[] {
                    "(function(){var d=document.documentElement,b=document.body;var y=window.pageYOffset||d.scrollTop||b.scrollTop||0;var h=window.innerHeight||d.clientHeight;var max=Math.max(d.scrollHeight,b.scrollHeight)-h;if(" + direction + ">0){if(y+h>=max-8)return 'END';window.scrollTo(0,Math.min(max,y+h*0.88));}else{if(y<=8)return 'START';window.scrollTo(0,Math.max(0,y-h*0.88));}return 'OK';})()"
                });
                if (String.Equals(Convert.ToString(result), "END", StringComparison.Ordinal) && chapterIndex + 1 < book.Spine.Count) OpenChapter(chapterIndex + 1, null);
                else if (String.Equals(Convert.ToString(result), "START", StringComparison.Ordinal) && chapterIndex > 0) OpenChapter(chapterIndex - 1, null);
            }
            catch
            {
                if (direction > 0 && chapterIndex + 1 < book.Spine.Count) OpenChapter(chapterIndex + 1, null);
                else if (direction < 0 && chapterIndex > 0) OpenChapter(chapterIndex - 1, null);
            }
        }
        void FindText()
        {
            if (browser.Document == null || String.IsNullOrWhiteSpace(searchBox.Text)) return;
            try
            {
                string q = searchBox.Text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
                object found = browser.Document.InvokeScript("eval", new object[] { "(function(){var q=\"" + q + "\";if(!q)return false;return window.find?window.find(q,false,false,true,false,false,false):false;})()" });
                if (!Convert.ToBoolean(found)) status.Text = "No more matches in this chapter.";
            }
            catch { status.Text = "Find is not supported by this document."; }
        }
    }
    internal static class Program
    {
        [STAThread] static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReaderForm());
        }
    }
}