"""Generate the approved Heritage cover ornaments for WPF and Companion.

Run from any directory with Python. Cabinet ornaments are intentionally separate.
Coordinates use the approved 220px cover: 42px corners and a 56px clasp.
"""
from pathlib import Path
import json
import math

ROOT = Path(__file__).resolve().parents[1]
PALETTES = {
    'gold': ['#765323', '#c99e53', '#fae8b1', '#a77b35', '#49341c', '#ad8341', '#ecd191', '#6c4b24'],
    'emerald': ['#295840', '#7ab58b', '#eff3c8', '#599f78', '#153b2b', '#6cad8a', '#d4e7b5', '#35664c'],
    'diamond': ['#4c6a7c', '#aac5d6', '#f7fcff', '#87a8bd', '#304a5c', '#95b5ce', '#e2f5ff', '#536f86'],
    'gold-face': ['#6b512b', '#272014'],
    'emerald-face': ['#16775c', '#041e1a'],
    'diamond-face': ['#48657f', '#111f30'],
}
OFFSETS = [0, .15, .24, .38, .49, .62, .81, 1]
LIGHT = {'gold': '#f9e1a4', 'emerald': '#e0f3ce', 'diamond': '#effaff'}
DARK = {'gold': '#654820', 'emerald': '#235b45', 'diamond': '#486981'}
assets = {}

def build(name, size):
    ops = []
    assets[name] = (size, ops)
    def path(d, fill='none', stroke=None, width=1):
        ops.append((d, fill, stroke, width))
    def circle(x, y, r, fill, stroke=None, width=1):
        path(f'M {x-r} {y} a {r} {r} 0 1 0 {2*r} 0 a {r} {r} 0 1 0 {-2*r} 0', fill, stroke, width)
    return path, circle

for tone in LIGHT:
    p, c = build(tone+'-corner', 42)
    p('M 0 42 L 0 9.24 L 2.1 5.04 L 5.04 2.1 L 9.24 0 L 42 0 L 36.96 3.78 L 18.48 3.78 L 15.54 7.56 L 9.24 7.56 L 7.56 9.24 L 7.56 15.54 L 3.78 18.48 L 3.78 36.96 Z', tone)
    p('M 1 33 L 1 10 Q 1 1 10 1 L 33 1', 'none', LIGHT[tone], .65)
    p('M 5 29 L 5 17 L 9 13 L 9 9 L 13 9 L 17 5 L 29 5', 'none', LIGHT[tone], .6)
    p('M 3 33 L 3 17 L 7 13 L 7 7 L 13 7 L 17 3 L 33 3', 'none', DARK[tone], .7)

    p, c = build(tone+'-accent', 42)
    if tone == 'gold':
        # Two small leaves follow the edge; no wreath spills onto the cover.
        p('M 5 21 C 0 18 -1 5 1 -1 C 8 4 10 16 5 21 Z', tone, LIGHT[tone], .45)
        p('M 5 19 L 2 3', 'none', DARK[tone], .65)
        p('M 13 10 C 9 8 10 0 13 -3 C 17 1 17 7 13 10 Z', tone, LIGHT[tone], .4)
    else:
        p('M 7 -3 L 21 6 L 17 26 L 1 15 Z', tone, LIGHT[tone], .55)
        colors = ['#9beace', '#36a887', '#063e32', '#38b990'] if tone == 'emerald' else ['#effaff', '#719bb8', '#304b65', '#b9dbf1']
        for d, color in zip(['M 7 -1 L 11 11 L 3 14 Z', 'M 7 -1 L 19 7 L 11 11 Z', 'M 3 14 L 11 11 L 16 23 Z', 'M 11 11 L 19 7 L 16 23 Z'], colors):
            p(d, color, LIGHT[tone], .25)
        if tone == 'diamond':
            p('M 22 -3 L 27 3 L 24 12 L 20 6 Z', tone, LIGHT[tone], .35)

    p, c = build(tone+'-badge', 56)
    if tone == 'gold':
        c(28, 29, 24.8, '#241c10')
        c(28, 28, 24.6, tone, LIGHT[tone], .55)
        for n in range(64):
            a = n*math.tau/64
            p(f'M {28+23.2*math.cos(a):.3f} {28+23.2*math.sin(a):.3f} L {28+24*math.cos(a):.3f} {28+24*math.sin(a):.3f}', 'none', DARK[tone], .35)
        c(28, 28, 21.2, tone+'-face', '#241c10', .7)
        c(28, 28, 19.5, 'none', '#c7a568', .4)
        p('M 10 19 A 20 20 0 0 1 36 10', 'none', '#fff1bf', .6)
    elif tone == 'emerald':
        p('M 28 1 Q 29 1 30 2 L 54 26 Q 56 28 54 30 L 30 54 Q 28 56 26 54 L 2 30 Q 0 28 2 26 L 26 2 Q 27 1 28 1 Z', tone, LIGHT[tone], .55)
        p('M 28 5 L 51 28 L 28 51 L 5 28 Z', tone+'-face', '#082e25', 1)
        for d,color in [('M 28 7 L 49 28 L 28 28 Z','#207b5c'),('M 49 28 L 28 49 L 28 28 Z','#07372e'),('M 28 49 L 7 28 L 28 28 Z','#0c503d'),('M 7 28 L 28 7 L 28 28 Z','#165c45')]:
            p(d,color)
        p('M 28 9 L 47 28 L 28 47 L 9 28 Z','none','#86c5a6',.4)
        for x,y in [(28,3),(53,28),(28,53),(3,28)]:
            p(f'M {x} {y-1.6} L {x+1.6} {y} L {x} {y+1.6} L {x-1.6} {y} Z',tone,LIGHT[tone],.3)
    else:
        p('M 18 3 L 38 3 L 53 18 L 53 38 L 38 53 L 18 53 L 3 38 L 3 18 Z',tone,LIGHT[tone],.6)
        outer=[(18,4),(38,4),(52,18),(52,38),(38,52),(18,52),(4,38),(4,18)]
        inner=[(20,8),(36,8),(48,20),(48,36),(36,48),(20,48),(8,36),(8,20)]
        for i,color in enumerate(['#e5f5ff','#8bacc2','#c5e1f2','#587890','#b5d4e9','#6f93ad','#d4ecfa','#8facbf']):
            points=[outer[i],outer[(i+1)%8],inner[(i+1)%8],inner[i]]
            p('M '+' L '.join(f'{x} {y}' for x,y in points)+' Z',color)
        p('M 20 8 L 36 8 L 48 20 L 48 36 L 36 48 L 20 48 L 8 36 L 8 20 Z',tone+'-face','#d1eaff',.35)
        p('M 21 11 L 35 11 L 45 21 L 45 35 L 35 45 L 21 45 L 11 35 L 11 21 Z','none','#92b5cb',.35)
        p('M 13 7 L 14 12 L 19 13 L 14 14 L 13 19 L 12 14 L 7 13 L 12 12 Z','#effaff')

def stops(colors):
    return list(zip(OFFSETS if len(colors)==8 else [0,1], colors))

defs=''.join(f'<linearGradient id="{key}" x1="0" y1="0" x2="1" y2="1">'+''.join(f'<stop offset="{o}" stop-color="{c}"/>' for o,c in stops(colors))+'</linearGradient>' for key,colors in PALETTES.items())
def paint(value):
    return 'none' if value is None else f'url(#{value})' if value in PALETTES else value

folder=ROOT/'src/PixelVault.Companion/wwwroot/completion-frames'
folder.mkdir(parents=True, exist_ok=True)
for name,(size,ops) in assets.items():
    svg=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}"><defs>{defs}</defs>'
    for d,fill,stroke,width in ops:
        svg+=f'<path d="{d}" fill="{paint(fill)}" stroke="{paint(stroke)}" stroke-width="{width}" stroke-linejoin="round" stroke-linecap="round"/>'
    (folder/('heritage-'+name+'.svg')).write_text(svg+'</svg>\n',encoding='utf-8')

lines=['// Generated by scripts/Build-HeritageCompletionAssets.py. Edit the shared generator.',
       'using System.Collections.Generic;','using System.Windows;','using System.Windows.Media;',
       'namespace PixelVaultNative {','internal static class HeritageCompletionOrnaments {',
       'static readonly Dictionary<string,Brush> Brushes=BuildBrushes();',
       'static readonly Dictionary<string,DrawingGroup> Art=BuildArt();',
       'static Dictionary<string,Brush> BuildBrushes() { var result=new Dictionary<string,Brush>();']
for key,colors in PALETTES.items():
    lines+=['{ var b=new LinearGradientBrush { StartPoint=new Point(0,0), EndPoint=new Point(1,1) };']
    lines += [f'b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("{c}"),{o}));' for o,c in stops(colors)]
    lines += [f'b.Freeze();result["{key}"]=b;}}']
lines+=['return result;}',
        'internal static Brush Paint(string key) { if(key==null||key=="none")return null;if(Brushes.TryGetValue(key,out var b))return b;var solid=new SolidColorBrush((Color)ColorConverter.ConvertFromString(key));solid.Freeze();return solid;}',
        'static Dictionary<string,DrawingGroup> BuildArt() { var result=new Dictionary<string,DrawingGroup>();']
for name,(size,ops) in assets.items():
    lines+=['{ var g=new DrawingGroup();']
    for d,fill,stroke,width in ops:
        pen='null' if stroke is None else f'new Pen(Paint({json.dumps(stroke)}),{width}) {{ LineJoin=PenLineJoin.Round, StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round }}'
        lines += [f'g.Children.Add(new GeometryDrawing(Paint({json.dumps(fill)}),{pen},Geometry.Parse({json.dumps(d)})));']
    lines += [f'g.Freeze();result["{name}"]=g;}}']
lines+=['return result;}',
        'internal static void Add(DrawingGroup target,string key,double size,double scale,double x,double y,bool flipX=false,bool flipY=false) {',
        'var t=new TransformGroup();t.Children.Add(new ScaleTransform(flipX?-1:1,flipY?-1:1,size/2,size/2));t.Children.Add(new ScaleTransform(scale,scale));t.Children.Add(new TranslateTransform(x,y));',
        'var g=new DrawingGroup { Transform=t };g.Children.Add(Art[key]);target.Children.Add(g);}', '}', '}']
(ROOT/'src/PixelVault.Native/UI/Library/HeritageCompletionOrnaments.g.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(f'Generated {len(assets)} matching Heritage SVG/WPF ornaments.')
