"""把 xlsx 第一个工作表的内容导出为 JSON 二维数组。用法: py -3.12 xlsx_rows.py <xlsx> <out.json>"""
import json
import re
import sys
import zipfile

z = zipfile.ZipFile(sys.argv[1])
shared = re.findall(r"<x:t[^>]*>(.*?)</x:t>", z.read("xl/sharedStrings.xml").decode())
rows = []
for row in re.findall(r"<x:row[^>]*>(.*?)</x:row>", z.read("xl/worksheets/sheet1.xml").decode()):
    cells = re.findall(r'<x:c ([^>]*)><x:v>(.*?)</x:v>', row)
    rows.append([shared[int(v)] if 't="s"' in a else v for a, v in cells])
with open(sys.argv[2], "w", encoding="utf-8") as f:
    json.dump(rows, f, ensure_ascii=False)
print(f"{len(rows)} rows -> {sys.argv[2]}")
