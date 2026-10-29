# EbookSkies Reader (Windows 7 experiment)

A small native Windows Forms EPUB reader prototype for older Windows machines. It avoids Tk, GTK, Qt, Electron, and bundled Chromium.

## How it works

- C# / Windows Forms provides the desktop UI.
- The built-in Windows WebBrowser control renders chapter XHTML, CSS, images, and fonts using the installed Internet Explorer/MSHTML engine.
- Reads the EPUB ZIP package, META-INF/container.xml, OPF manifest/spine, EPUB 3 navigation document, and EPUB 2 NCX table of contents.
- Extracts to a unique temporary directory with path traversal checks.
- Strips script elements and inline event-handler attributes from XHTML that parses as XML.
- Page Up/Page Down or Space scrolls in screen-sized steps; crossing a chapter boundary moves to the adjacent spine item.
- Includes a chapter tree, font-size controls, basic chapter text search, and command-line opening.

## Build

Requires .NET Framework 4.5 or later on Windows, including the framework assemblies and C# compiler.

1. Run build.bat.
2. Run EbookSkiesReader.exe.
3. Choose Open EPUB, or pass an EPUB path as the first command-line argument.

On a Windows 7 machine without .NET Framework 4.5 or later, install a compatible .NET Framework first.

## Important limitations

This is an early test build, not a claim of complete EPUB 3 conformance. Rendering is delegated to the installed IE/MSHTML version, so CSS support varies and modern EPUB layouts may look wrong. Fixed-layout publications, scripted publications, media overlays, DRM-protected books, and advanced EPUB accessibility features are not implemented. Page turning is scrolling-based, not true reflow pagination. Some malformed XHTML may bypass XML-based script stripping. The WebBrowser engine is legacy software; use this for offline reading of trusted local books, not browsing untrusted websites.

No Windows build or real-book regression suite has been run in this environment. Please test a few EPUBs, especially ones with images, CSS, nested chapters, and unusual fonts, and report which book fails and what happens.

## Controls

- Open EPUB or Ctrl+O: choose a book.
- Page Down / Space: move forward; at chapter end, open the next chapter.
- Page Up: move backward; at chapter start, open the previous chapter.
- A− / A+: change text size.
- Find or Ctrl+F: search within the current chapter.
- Select a chapter in the left tree to jump there.

## License

This experimental reader follows the repository's license, if present.
