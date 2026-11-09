// 把 docs/manual/README.md 转成 Word：node tools/docgen/build-manual.js
// Excel 截图在 Word 里改为真实表格（横向页），其余截图按原图插入。
const fs = require("fs");
const path = require("path");
const {
  Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType,
  HeadingLevel, AlignmentType, LevelFormat, ShadingType, BorderStyle, PageOrientation, PageNumber,
  Footer, Header, TableLayoutType, TabStopType, Tab,
} = require("docx");

const root = path.resolve(__dirname, "..", "..");
const mdPath = path.join(root, "docs", "manual", "README.md");
const imgDir = path.join(root, "docs", "manual", "images");
const outPath = path.join(root, "docs", "实验数据追溯系统-用户操作说明.docx");
const FONT = "Microsoft YaHei";
// 版本号以 csproj 的 <Version> 为准，与软件窗口标题一致
const VERSION = (fs.readFileSync(path.join(root, "src", "PlcTestLogger.App", "PlcTestLogger.App.csproj"), "utf8").match(/<Version>([^<]+)<\/Version>/) || [, "0.0.0"])[1];
const DOC_TITLE = "实验数据追溯系统 用户操作说明";
const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1134; // A4, 2cm
const CONTENT_W = PAGE_W - MARGIN * 2;
const LAND_CONTENT_W = PAGE_H - MARGIN * 2;

const excelTables = {
  "excel-dev01.png": { json: "dev01.json", title: "表 1  设备 DEV01 的 logger\\DEV01\\2026-11.xlsx" },
  "excel-dev02.png": { json: "dev02.json", title: "表 2  设备 DEV02 的 logger\\DEV02\\2026-11.xlsx" },
};
const imageWidthsIn = { "main.png": 6.3, "add-device.png": 3.2, "edit-device.png": 3.2, "edit-device-blocked.png": 3.2, "search.png": 6.3, "logo.png": 0.9 };

// ---------- inline markdown (**bold**, `code`) ----------
function inline(text, extra = {}) {
  const runs = [];
  const re = /(\*\*[^*]+\*\*|`[^`]+`)/g;
  let last = 0, m;
  while ((m = re.exec(text)) !== null) {
    if (m.index > last) runs.push(new TextRun({ text: text.slice(last, m.index), font: FONT, ...extra }));
    const tok = m[0];
    if (tok.startsWith("**")) runs.push(new TextRun({ text: tok.slice(2, -2), bold: true, font: FONT, ...extra }));
    else runs.push(new TextRun({ text: tok.slice(1, -1), font: "Consolas", ...extra }));
    last = m.index + tok.length;
  }
  if (last < text.length) runs.push(new TextRun({ text: text.slice(last), font: FONT, ...extra }));
  return runs;
}

function para(text, opts = {}) {
  return new Paragraph({ children: inline(text), spacing: { after: 120, line: 320 }, ...opts });
}

function imageParagraph(file) {
  const buf = fs.readFileSync(path.join(imgDir, file));
  const { width, height } = pngSize(buf);
  const w = Math.round((imageWidthsIn[file] || 6.3) * 96);
  const h = Math.round(height * (w / width));
  return new Paragraph({
    alignment: AlignmentType.CENTER, spacing: { before: 60, after: 160 },
    children: [new ImageRun({ type: "png", data: buf, transformation: { width: w, height: h } })],
  });
}

function pngSize(buf) { return { width: buf.readUInt32BE(16), height: buf.readUInt32BE(20) }; }

function cell(text, { header = false, width, size = 20 } = {}) {
  return new TableCell({
    width: { size: width, type: WidthType.DXA },
    shading: header ? { type: ShadingType.CLEAR, fill: "DDEBF7", color: "auto" } : undefined,
    margins: { top: 40, bottom: 40, left: 80, right: 80 },
    children: [new Paragraph({ children: inline(text, { size, bold: header }), spacing: { after: 0 } })],
  });
}

function mdTable(lines, totalWidth) {
  const rows = lines.filter(l => !/^\|\s*-{3,}/.test(l)).map(l => l.trim().replace(/^\||\|$/g, "").split("|").map(c => c.trim()));
  const cols = rows[0].length;
  // 列宽按内容长度比例分配
  const lens = Array(cols).fill(0);
  rows.forEach(r => r.forEach((c, i) => { lens[i] = Math.max(lens[i], Math.min(c.length, 40), 4); }));
  const sum = lens.reduce((a, b) => a + b, 0);
  const widths = lens.map(l => Math.round(totalWidth * l / sum));
  widths[cols - 1] += totalWidth - widths.reduce((a, b) => a + b, 0);
  return new Table({
    width: { size: totalWidth, type: WidthType.DXA }, columnWidths: widths, layout: TableLayoutType.FIXED,
    rows: rows.map((r, ri) => new TableRow({
      tableHeader: ri === 0,
      children: r.map((c, i) => cell(c, { header: ri === 0, width: widths[i] })),
    })),
  });
}

function excelTable(rows, totalWidth) {
  const cols = rows[0].length;
  const lens = Array(cols).fill(0);
  rows.forEach(r => r.forEach((c, i) => { lens[i] = Math.max(lens[i], c.length, 3); }));
  const sum = lens.reduce((a, b) => a + b, 0);
  const widths = lens.map(l => Math.round(totalWidth * l / sum));
  widths[cols - 1] += totalWidth - widths.reduce((a, b) => a + b, 0);
  return new Table({
    width: { size: totalWidth, type: WidthType.DXA }, columnWidths: widths, layout: TableLayoutType.FIXED,
    rows: rows.map((r, ri) => new TableRow({
      tableHeader: ri === 0,
      children: r.map((c, i) => cell(c, { header: ri === 0, width: widths[i], size: 14 })),
    })),
  });
}

function codeBlock(lines) {
  return lines.map(l => new Paragraph({
    shading: { type: ShadingType.CLEAR, fill: "F3F3F3", color: "auto" },
    spacing: { after: 0 }, indent: { left: 200 },
    children: [new TextRun({ text: l.length ? l : " ", font: "Consolas", size: 18 })],
  }));
}

// ---------- parse markdown into sections (portrait / landscape) ----------
const md = fs.readFileSync(mdPath, "utf8").replace(/\r\n/g, "\n").split("\n");
const sections = [];
let cur = { landscape: false, children: [] };
function flushSection(landscape) { if (cur.children.length) sections.push(cur); cur = { landscape, children: [] }; }

let i = 0;
let titleSeen = false;
// 编号列表实例全局递增：同一 instance 在不同节里会共用 numId 而继续编号
let listInstance = 0, lastWasNotList = true;
while (i < md.length) {
  const line = md[i];
  if (line.startsWith("# ")) {
    if (!titleSeen) {
      titleSeen = true;
      cur.children.push(imageParagraph("logo.png"));
      cur.children.push(new Paragraph({ heading: HeadingLevel.TITLE, alignment: AlignmentType.CENTER, children: [new TextRun({ text: line.slice(2), font: FONT })] }));
      cur.children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 60 }, children: [new TextRun({ text: "v" + VERSION, font: FONT, size: 28, bold: true, color: "1F3864" })] }));
    }
    i++; continue;
  }
  if (line.startsWith("## ")) { lastWasNotList = true; cur.children.push(new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun({ text: line.slice(3), font: FONT })] })); i++; continue; }
  if (line.startsWith("```")) {
    const buf = []; i++;
    while (i < md.length && !md[i].startsWith("```")) buf.push(md[i++]);
    i++;
    lastWasNotList = true;
    cur.children.push(...codeBlock(buf));
    cur.children.push(new Paragraph({ spacing: { after: 60 } }));
    continue;
  }
  if (line.startsWith("|")) {
    const buf = [];
    while (i < md.length && md[i].startsWith("|")) buf.push(md[i++]);
    lastWasNotList = true;
    cur.children.push(mdTable(buf, cur.landscape ? LAND_CONTENT_W : CONTENT_W));
    cur.children.push(new Paragraph({ spacing: { after: 60 } }));
    continue;
  }
  const img = line.match(/^!\[([^\]]*)\]\(images\/([^)]+)\)/);
  if (img) {
    const file = img[2];
    if (file === "logo.png" && titleSeen) { i++; continue; }
    if (excelTables[file]) {
      const t = excelTables[file];
      if (!cur.landscape) flushSection(true);
      const rows = JSON.parse(fs.readFileSync(path.join(__dirname, t.json), "utf8"));
      cur.children.push(new Paragraph({ children: [new TextRun({ text: t.title, font: FONT, bold: true, size: 20 })], spacing: { before: 120, after: 80 } }));
      cur.children.push(excelTable(rows, LAND_CONTENT_W));
      cur.children.push(new Paragraph({ spacing: { after: 60 } }));
      // 两张表都放完后回到纵向
      if (file === "excel-dev02.png") flushSection(false);
    } else {
      cur.children.push(imageParagraph(file));
      cur.children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 160 }, children: [new TextRun({ text: "图  " + img[1], font: FONT, size: 18, color: "555555" })] }));
    }
    i++; continue;
  }
  if (/^- /.test(line)) { cur.children.push(new Paragraph({ children: inline(line.slice(2)), numbering: { reference: "bullets", level: 0 }, spacing: { after: 60 } })); i++; continue; }
  const num = line.match(/^(\d+)\. (.*)/);
  if (num) {
    // 每个编号列表用独立实例，从 1 重新计数
    if (lastWasNotList) { listInstance++; lastWasNotList = false; }
    cur.children.push(new Paragraph({ children: inline(num[2]), numbering: { reference: "numbers", level: 0, instance: listInstance }, spacing: { after: 60 } }));
    i++; continue;
  }
  // 空行和图片不打断编号列表（列表项之间可以插图）；正文段落、表格、代码块、标题才结束列表
  if (line.trim() === "") { i++; continue; }
  lastWasNotList = true;
  if (/^版本：/.test(line)) { cur.children.push(new Paragraph({ alignment: AlignmentType.CENTER, children: inline(line.replace(/v[\d.]+/, "v" + VERSION), { color: "555555" }), spacing: { after: 240 } })); i++; continue; }
  cur.children.push(para(line));
  i++;
}
flushSection(false);

function sectionProps(landscape) {
  return {
    page: {
      size: landscape ? { width: PAGE_H, height: PAGE_W, orientation: PageOrientation.LANDSCAPE } : { width: PAGE_W, height: PAGE_H },
      margin: { top: MARGIN, bottom: MARGIN, left: MARGIN, right: MARGIN },
    },
  };
}
// 页眉（标题栏）：左侧文档名，右侧版本号
function header(landscape) {
  const w = landscape ? LAND_CONTENT_W : CONTENT_W;
  return new Header({ children: [new Paragraph({
    tabStops: [{ type: TabStopType.RIGHT, position: w }],
    border: { bottom: { style: BorderStyle.SINGLE, size: 4, color: "BBBBBB", space: 2 } },
    spacing: { after: 120 },
    children: [
      new TextRun({ text: DOC_TITLE, font: FONT, size: 18, color: "777777" }),
      new TextRun({ children: [new Tab(), "v" + VERSION], font: FONT, size: 18, color: "777777" }),
    ],
  })] });
}
const footer = new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [new TextRun({ children: ["第 ", PageNumber.CURRENT, " 页"], font: FONT, size: 18, color: "777777" })] })] });

const doc = new Document({
  creator: "PlcLogger",
  title: DOC_TITLE + " v" + VERSION,
  styles: {
    default: { document: { run: { font: FONT, size: 21 } } },
    paragraphStyles: [
      { id: "Title", name: "Title", basedOn: "Normal", run: { font: FONT, size: 44, bold: true, color: "1F3864" }, paragraph: { spacing: { before: 120, after: 120 }, alignment: AlignmentType.CENTER } },
      { id: "Heading1", name: "Heading 1", basedOn: "Normal", next: "Normal", quickFormat: true, run: { font: FONT, size: 30, bold: true, color: "1F3864" }, paragraph: { spacing: { before: 360, after: 160 }, keepNext: true, outlineLevel: 0 } },
    ],
  },
  numbering: {
    config: [
      { reference: "bullets", levels: [{ level: 0, format: LevelFormat.BULLET, text: "•", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 560, hanging: 280 } } } }] },
      { reference: "numbers", levels: [{ level: 0, format: LevelFormat.DECIMAL, text: "%1.", alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 560, hanging: 280 } } } }] },
    ],
  },
  sections: sections.map(s => ({ properties: sectionProps(s.landscape), headers: { default: header(s.landscape) }, footers: { default: footer }, children: s.children })),
});

Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(outPath, buf);
  console.log("written", outPath, buf.length, "bytes;", sections.length, "sections");
});
