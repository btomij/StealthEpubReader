using System.IO;
using System.IO.Compression;
using System.Text;

namespace StealthEpubReader.Services;

public static class SampleEpubGenerator
{
    public static string EnsureSampleEpubCreated(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        string epubPath = Path.Combine(dataDir, "StealthReader_Guide.epub");
        if (File.Exists(epubPath))
        {
            return epubPath;
        }

        using var fs = new FileStream(epubPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        // 1. mimetype (must be first and uncompressed per EPUB spec)
        var mimeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(mimeEntry.Open(), Encoding.ASCII))
        {
            writer.Write("application/epub+zip");
        }

        // 2. META-INF/container.xml
        WriteEntry(archive, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        // 3. OEBPS/content.opf
        WriteEntry(archive, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>上班摸魚生存指南（內建範例書）</dc:title>
                <dc:creator>StealthEpubReader</dc:creator>
                <dc:language>zh-TW</dc:language>
                <dc:identifier id="BookId">urn:uuid:stealth-epub-sample-001</dc:identifier>
              </metadata>
              <manifest>
                <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
                <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
                <item id="ch2" href="ch2.xhtml" media-type="application/xhtml+xml"/>
                <item id="ch3" href="ch3.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine toc="ncx">
                <itemref idref="ch1"/>
                <itemref idref="ch2"/>
                <itemref idref="ch3"/>
              </spine>
            </package>
            """);

        // 4. OEBPS/toc.ncx
        WriteEntry(archive, "OEBPS/toc.ncx", """
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:uid" content="urn:uuid:stealth-epub-sample-001"/>
              </head>
              <docTitle><text>上班摸魚生存指南（內建範例書）</text></docTitle>
              <navMap>
                <navPoint id="navPoint-1" playOrder="1">
                  <navLabel><text>第一章：快速上手與防抓包秘訣</text></navLabel>
                  <content src="ch1.xhtml"/>
                </navPoint>
                <navPoint id="navPoint-2" playOrder="2">
                  <navLabel><text>第二章：外觀偽裝與雙層透明度</text></navLabel>
                  <content src="ch2.xhtml"/>
                </navPoint>
                <navPoint id="navPoint-3" playOrder="3">
                  <navLabel><text>第三章：極簡圖書館與測試長文</text></navLabel>
                  <content src="ch3.xhtml"/>
                </navPoint>
              </navMap>
            </ncx>
            """);

        // 5. Chapters
        WriteEntry(archive, "OEBPS/ch1.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>第一章：快速上手與防抓包秘訣</title></head>
            <body>
              <h1>第一章：快速上手與防抓包秘訣</h1>
              <p>// 歡迎使用 StealthEpubReader —— 專為辦公室隱蔽閱讀打造的極致輕量 EPUB 閱讀器。</p>
              <p>【1. 老闆鍵瞬間隱身】無論您目前正在點擊 Excel、VS Code 還是瀏覽器，只要按下全域快捷鍵「Alt + Q」，閱讀器會在 1 毫秒內直接消失且不產生任何動畫；再按一次「Alt + Q」即可瞬間還原至原本看到的那一行。</p>
              <p>【2. 滑鼠移開自動隱形】點擊上方微型工具列的「自動隱形 (Auto)」開關後，只要您的滑鼠游標移開閱讀器視窗，視窗就會自動變成 0% 完全透明；滑鼠移回來時才顯示文字，連按鍵都省了！</p>
              <p>【3. 自由匯入書籍】您可以直接將電腦裡的 .epub 小說檔案「拖曳 (Drag &amp; Drop)」丟進這個視窗，或點擊上方「書庫 (Ctrl+B)」按鈕匯入多本書籍。</p>
              <p>【4. 翻頁與滾動操作】支援「滑鼠滾輪 / 上下方向鍵」平滑捲動，也支援「左右方向鍵 / PageUp / PageDown」快速翻頁與跨章節切換。</p>
              <p>【5. 進度自動記憶】每本書的章節與精確滾動位置都會在背景自動存檔，切換書籍或重新開機後都能無縫接續閱讀。</p>
            </body>
            </html>
            """);

        WriteEntry(archive, "OEBPS/ch2.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>第二章：外觀偽裝與雙層透明度</title></head>
            <body>
              <h1>第二章：外觀偽裝與雙層透明度</h1>
              <p>// 點擊上方「外觀 (Ctrl+,)」即可打開字型、顏色與透明度控制面板。</p>
              <p>【系統字型自由切換】支援讀取 Windows 系統內安裝的所有字型！若您平常使用 VS Code 或 Visual Studio，推薦選擇「Consolas」或「Cascadia Mono」等寬字型，搭配「IDE 註解綠」主題，遠看百分之百像程式碼註解。</p>
              <p>【雙層獨立透明度】傳統閱讀器調低透明度時，文字也會跟著變淡導致眼睛疲勞。本閱讀器將「背景透明度 (0%~100%)」與「文字透明度 (10%~100%)」完全獨立分開！</p>
              <p>您可以把「背景透明度」拉到 0%（完全透明），讓文字直接浮在您的工作視窗或報表上方，同時保持文字清晰好讀。</p>
              <p>【快捷微調手勢】在閱讀區按住「Ctrl + 滾輪」可即時放大縮小字體；按住「Alt + 滾輪」可即時調整文字透明度；拖曳視窗右下角可將視窗縮成「單行字幕條」貼在狀態列上！</p>
            </body>
            </html>
            """);

        WriteEntry(archive, "OEBPS/ch3.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.1//EN" "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>第三章：極簡圖書館與測試長文</title></head>
            <body>
              <h1>第三章：極簡圖書館與測試長文</h1>
              <p>以下為捲動與進度記憶測試段落。您可以試著往下捲動到任意段落，然後切換章節或關閉程式，測試進度是否精準保留。</p>
              <p>[001] 午後三點的辦公室總是特別安靜，只有鍵盤敲擊聲與空調低沉的運轉聲交織在一起。</p>
              <p>[002] 螢幕上看似是一段正在除錯的系統日誌，實則是一段精彩絕倫的奇幻冒險章節。</p>
              <p>[003] 當腳步聲從座位後方緩緩靠近時，手指輕巧地掠過滑鼠邊緣，整段文字瞬間化為透明無形的空氣。</p>
              <p>[004] 「這支 API 的回應時間我們再觀察一下。」神色自若地指著底層的程式碼視窗說道。</p>
              <p>[005] 待腳步聲遠去，游標輕輕移回角落，熟悉的故事字句再次浮現眼前，彷彿從未中斷。</p>
              <p>[006] 支援預載快取引擎：當您閱讀本章時，上一章與下一章早已在背景靜默解析完畢，切換章節零等待。</p>
              <p>[007] 預設過濾所有 EPUB 內的插圖與封面圖片，徹底杜絕翻頁時突然跳出大張彩色插圖的尷尬風險。</p>
              <p>[008] 祝您閱讀愉快，工作順心！</p>
            </body>
            </html>
            """);

        return epubPath;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}
