"""把 logger 下的 xlsx 内容渲染成图片（用于手册截图）。
用法: py -3.12 tools/render_excel.py <xlsx> <out.png> [title]
"""
import os
import re
import subprocess
import sys
import zipfile

xlsx, out = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
title = sys.argv[3] if len(sys.argv) > 3 else os.path.basename(xlsx)

z = zipfile.ZipFile(xlsx)
shared = re.findall(r"<x:t[^>]*>(.*?)</x:t>", z.read("xl/sharedStrings.xml").decode())
rows = []
for row in re.findall(r"<x:row[^>]*>(.*?)</x:row>", z.read("xl/worksheets/sheet1.xml").decode()):
    cells = re.findall(r'<x:c ([^>]*)><x:v>(.*?)</x:v>', row)
    rows.append([shared[int(v)] if 't="s"' in a else v for a, v in cells])

tsv = out + ".tsv"
with open(tsv, "w", encoding="utf-8-sig") as f:
    f.write("\n".join("\t".join(r) for r in rows))

PS = r'''
Add-Type -AssemblyName System.Drawing
$rows = @(Get-Content -Encoding UTF8 "{tsv}" | ForEach-Object {{ ,($_ -split "`t") }})
$font = New-Object System.Drawing.Font "Microsoft YaHei UI",10
$bold = New-Object System.Drawing.Font "Microsoft YaHei UI",10,([System.Drawing.FontStyle]::Bold)
$m = [System.Drawing.Graphics]::FromImage((New-Object System.Drawing.Bitmap 10,10))
$cols = $rows[0].Count
$w = @(); for ($c=0; $c -lt $cols; $c++) {{ $mx = 0; foreach ($r in $rows) {{ if ($c -lt $r.Count) {{ $sz = $m.MeasureString([string]$r[$c], $bold).Width; if ($sz -gt $mx) {{ $mx = $sz }} }} }}; $w += [int]($mx + 18) }}
$rowH = 26; $pad = 12; $titleH = 34
$imgW = [int](($w | Measure-Object -Sum).Sum + $pad*2 + 40); $imgH = [int]($rows.Count*$rowH + $pad*2 + $titleH)
$bmp = New-Object System.Drawing.Bitmap $imgW,$imgH; $g=[System.Drawing.Graphics]::FromImage($bmp); $g.TextRenderingHint='ClearTypeGridFit'; $g.Clear([System.Drawing.Color]::White)
$g.DrawString("{title}", (New-Object System.Drawing.Font "Microsoft YaHei UI",11,([System.Drawing.FontStyle]::Bold)), [System.Drawing.Brushes]::Black, $pad, 8)
$grid = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200,200,200))
$hdrBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(221,235,247))
$numBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240,240,240))
$y = $pad + $titleH; $x0 = $pad + 40
for ($ri=0; $ri -lt $rows.Count; $ri++) {{
  $x = $x0; $r = $rows[$ri]
  $g.FillRectangle($numBrush, [System.Drawing.Rectangle]::new([int]$pad, [int]$y, 40, [int]$rowH))
  $g.DrawRectangle($grid, [System.Drawing.Rectangle]::new([int]$pad, [int]$y, 40, [int]$rowH))
  $g.DrawString([string]($ri+1), $font, [System.Drawing.Brushes]::DimGray, $pad+12, $y+4)
  for ($c=0; $c -lt $cols; $c++) {{
    $rect = [System.Drawing.Rectangle]::new([int]$x, [int]$y, [int]$w[$c], [int]$rowH)
    if ($ri -eq 0) {{ $g.FillRectangle($hdrBrush, $rect) }}
    $g.DrawRectangle($grid, $rect)
    $s = if ($c -lt $r.Count) {{ [string]$r[$c] }} else {{ "" }}
    $g.DrawString($s, $(if ($ri -eq 0) {{ $bold }} else {{ $font }}), [System.Drawing.Brushes]::Black, $x+6, $y+4)
    $x += $w[$c]
  }}
  $y += $rowH
}}
$g.Dispose(); $bmp.Save("{out}", [System.Drawing.Imaging.ImageFormat]::Png); "saved {out}"
'''.format(tsv=tsv.replace("\\", "\\\\"), title=title, out=out.replace("\\", "\\\\"))

psf = out + ".ps1"
with open(psf, "w", encoding="utf-8-sig") as f:
    f.write(PS)
r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", psf],
                   capture_output=True, text=True, encoding="utf-8", errors="replace")
print(r.stdout.strip(), r.stderr[:800])
os.remove(tsv)
os.remove(psf)
