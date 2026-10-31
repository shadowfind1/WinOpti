"""Переводит строки в файле по списку пар.

python dev/loc.py <файл> <пары.txt>

Пары: две строки (старое, новое), между парами пустая строка.
  XAML: старое — русский текст атрибута, новое — английский. "RU" -> "{ui:Tr 'RU', En='EN'}".
  C#:   если новое начинается с "~", это перевод: "RU" -> L.T("RU", "EN"), $"RU" -> L.T($"RU", $"EN").
        иначе — обычная замена кода как есть.
Каждое старое должно найтись хотя бы раз, иначе файл не меняется.
"""
import sys

partial = '--partial' in sys.argv
path, pairs_path = [a for a in sys.argv[1:] if a != '--partial']
bom = open(path, 'rb').read(3) == bytes([0xEF, 0xBB, 0xBF])
src = open(path, encoding='utf-8-sig').read()
raw = open(pairs_path, encoding='utf-8').read().replace('\r\n', '\n')
blocks = [b for b in raw.split('\n\n') if b.strip()]
xaml = path.endswith('.xaml')
missing = []


def xesc(s):
    return s.replace('\\', '\\\\').replace("'", "\\'").replace('&', '&amp;').replace('"', '&quot;').replace('<', '&lt;')


for b in blocks:
    lines = b.strip('\n').split('\n')
    if len(lines) != 2:
        sys.exit('bad block:\n' + b)
    old, new = lines
    if xaml:
        # русский текст в xaml уже экранирован (&#xE73E; и т.п. не трогаем)
        key = '"' + old + '"'
        if key not in src:
            missing.append(old); continue
        en = new.replace('&', '&amp;') if '&#' not in new else new
        en = en.replace("'", "\\'")
        ru = old.replace("'", "\\'")
        src = src.replace(key, '"{ui:Tr \'' + ru + '\', En=\'' + en + '\'}"')
    elif new.startswith('~'):
        en = new[1:]
        done = False
        # редактор срезает пробел в конце строки, а в коде он часто есть: "…больше. " +
        if not any(p + old + '"' in src for p in ('$@"', '$"', '"')) and any(p + old + ' "' in src for p in ('$@"', '$"', '"')):
            old, en = old + ' ', en + ' '
        for pre in ('$@"', '$"', '"'):
            key = pre + old + '"'
            if key in src:
                rep = 'L.T(' + key + ', ' + pre + en + '")'
                # не переводить второй раз
                src = src.replace('L.T(' + key, '\0').replace(key, rep).replace('\0', 'L.T(' + key)
                done = True
                break
        if not done:
            missing.append(old)
    else:
        if old not in src:
            missing.append(old); continue
        src = src.replace(old, new)

if missing:
    print('NOT FOUND in', path)
    for m in missing:
        print('  ', m)
    if not partial:
        sys.exit(1)
if xaml and 'xmlns:ui=' not in src:
    i = src.index('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"') + len('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"')
    src = src[:i] + '\n             xmlns:ui="clr-namespace:WinOpti.UI"' + src[i:]
open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8').write(src)
print('ok', path, len(blocks))
