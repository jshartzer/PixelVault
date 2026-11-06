"""Generate matching SVG and WPF vector ornaments from one set of paths."""
from pathlib import Path
import json, math

ROOT = Path(__file__).resolve().parents[1]
palettes = {
    'gold': ['#4b2b10', '#d39c45', '#fff4cc', '#b97926', '#6b3f15', '#edc77f', '#fff8df'],
    'emerald': ['#063f36', '#15977d', '#d8fff1', '#39deb6', '#064d41', '#b7e8d5', '#fff9da'],
    'gem': ['#002f32', '#00a68e', '#baffea', '#05d7b4', '#005746', '#68ffe0', '#e1fff2'],
    'gold-inlay': ['#090d0d', '#393222', '#74603d', '#171a17', '#141b1c', '#4f3e22', '#151916'],
    'emerald-inlay': ['#061a1a', '#095e50', '#10836a', '#053b34', '#021f23', '#0d594b', '#07372f'],
}
assets = {}

def build(name, size, color):
    ops = []
    assets[name] = {'size': size, 'ops': ops}
    def path(d, fill='none', stroke=None, sw=1):
        ops.append(dict(d=d, fill=fill, stroke=stroke, sw=sw))
    def circle(x,y,r,fill='none',stroke=None,sw=1):
        path(f'M {x-r} {y} a {r} {r} 0 1 0 {r*2} 0 a {r} {r} 0 1 0 {-r*2} 0',fill,stroke,sw)
    def leaf(x,y,length,angle,breadth=6):
        a=math.radians(angle)
        def p(px,py): return f'{x+px*math.cos(a)-py*math.sin(a):.3f},{y+px*math.sin(a)+py*math.cos(a):.3f}'
        path(f'M {p(0,0)} C {p(-breadth,-length*.35)} {p(-breadth,-length*.77)} {p(0,-length)} C {p(breadth*.8,-length*.64)} {p(breadth*.8,-length*.16)} {p(0,0)} Z',color,'#fff1bc' if color=='gold' else '#b4ffdf',.65)
        path(f'M {p(0,0)} Q {p(-.7,-length*.55)} {p(0,-length*.9)}','none','#624112' if color=='gold' else '#00664d',.8)
    def jewel(x,y,rx,ry):
        path(f'M {x} {y-ry-5} L {x+rx+5} {y} L {x} {y+ry+5} L {x-rx-5} {y} Z',color,'#efe7ba',1.5)
        path(f'M {x} {y-ry} L {x+rx} {y} L {x} {y+ry} L {x-rx} {y} Z','gem','#003e37',2)
        path(f'M {x} {y-ry} L {x} {y} L {x-rx} {y} Z','#82ffe3','#c8ffeb',.4)
        path(f'M {x} {y-ry} L {x+rx} {y} L {x} {y} Z','#00c7a4','#a1ffe2',.4)
        path(f'M {x-rx} {y} L {x} {y} L {x} {y+ry} Z','#005b54','#1cdfb4',.4)
        path(f'M {x} {y} L {x+rx} {y} L {x} {y+ry} Z','#009b7b','#49f1c9',.4)
        path(f'M {x} {y-ry+7} L {x+rx-7} {y} L {x} {y+ry-7} L {x-rx+7} {y} Z','none','#bdffe8',1)
        path(f'M {x} {y-ry} L {x} {y+ry}','none','#dffff0',.7)
    return path,circle,leaf,jewel

p,c,l,j=build('gold-corner',128,'gold')
p('M 9 70 L 9 26 Q 9 9 26 9 L 71 9 L 62 15 L 27 15 Q 16 15 16 27 L 16 60 Z','gold','#fff0c1',.8)
p('M 20 53 Q 23 30 52 20 Q 38 33 20 53 Z','gold','#fff4c9',.7)
p('M 23 45 L 47 22','none','#6c431a',1.2)
c(24,24,6,'gold','#fff4c9',.7);c(22,22,1.9,'#fff6da')

p,c,l,j=build('gold-scroll',128,'gold')
p('M 5 121 C 3 95 11 75 29 73 C 52 68 64 86 57 105 C 50 122 29 120 25 106 C 20 90 40 82 46 94 C 51 103 37 110 35 101','none','#4b2b10',7)
p('M 5 121 C 3 95 11 75 29 73 C 52 68 64 86 57 105 C 50 122 29 120 25 106 C 20 90 40 82 46 94 C 51 103 37 110 35 101','none','gold',4)
p('M 8 117 C 20 107 38 108 57 118 C 77 129 100 119 119 119','none','gold',3)
for args in [(68,120,36,-65,5),(83,119,31,-75,5),(100,120,24,-80,4),(17,92,30,-12,4),(22,78,27,-23,4),(32,77,23,-38,4)]: l(*args)
p('M 10 116 Q 23 88 19 65','none','#fff4cf',1)

p,c,l,j=build('emerald-corner',128,'emerald')
p('M 5 117 L 5 67 L 19 48 L 19 25 L 45 25 L 62 8 L 118 8 L 104 21 L 66 21 L 50 38 L 31 38 L 31 54 L 18 73 L 18 104 Z','emerald','#d8ffe9',1.6)
p('M 11 104 L 11 70 L 25 52 L 25 32 L 48 32 L 65 15 L 108 15','none','#003f37',3)
p('M 7 115 L 17 102 L 17 73','none','#42ffd5',1.4)
for args in [(29,45,24,42,4),(43,30,22,61,4)]: l(*args)
c(25,25,4,'gold','#edffdc',.7)

p,c,l,j=build('emerald-jewel',128,'emerald')
p('M 7 11 L 38 11 L 55 28 L 93 28 L 117 49 L 117 118 L 107 105 L 107 56 L 88 40 L 48 40 L 31 23 L 18 23 Z','emerald','#e9f5cd',1.3)
p('M 23 16 L 36 16 L 52 33 L 87 33 L 111 53 L 111 108','none','#003e35',2)
for args in [(77,110,35,33,7),(85,93,32,38,6),(36,43,30,-48,6),(42,56,27,-53,5),(85,46,26,45,5)]: l(*args)
j(76,57,29,39)
p('M 76 14 L 76 22 M 70 19 L 82 19','none','#e9fff3',1)

p,c,l,j=build('gold-badge',160,'gold')
for args in [(135,65,45,2,7),(140,49,35,14,6),(132,82,35,-11,6),(63,140,40,-76,7),(47,142,34,-68,6),(33,145,27,-58,6)]: l(*args)
p('M 23 149 Q 56 132 88 141 M 139 92 Q 150 55 146 16','none','gold',2.8)
c(91,91,58,'#1a1d17','#412b13',4)
c(91,91,55,'gold','#fff0c4',1.2)
c(91,91,50,'gold-inlay','#4b2c12',2)
c(91,91,46,'none','#e6bd74',1.5)
p('M 51 64 A 47 47 0 0 1 113 49','none','#fff6db',2)
p('M 130 115 A 47 47 0 0 1 69 133','none','#fff0bd',1.7)

p,c,l,j=build('emerald-badge',160,'emerald')
for args in [(52,143,44,-68,8),(36,145,35,-58,7),(24,146,28,-49,6),(143,61,44,12,7),(142,44,35,23,6),(145,80,35,0,6)]: l(*args)
p('M 8 150 Q 41 137 72 146 M 146 92 Q 155 54 150 17','none','emerald',2.5)
p('M 88 12 L 162 86 L 88 160 L 14 86 Z','emerald','#fff5d6',1.5)
p('M 88 18 L 156 86 L 88 154 L 20 86 Z','none','#003f38',4)
p('M 88 24 L 150 86 L 88 148 L 26 86 Z','emerald','#f1f5d0',1.3)
p('M 88 30 L 144 86 L 88 142 L 32 86 Z','emerald-inlay','#004d43',2)
p('M 88 35 L 139 86 L 88 137 L 37 86 Z','none','#d2ffe3',.8)
for x,y in [(88,29),(145,86),(88,143),(31,86)]: j(x,y,5,7)

for color in ['gold','emerald']:
    p,c,l,j=build(color+'-case-header',(520,106),color)
    p('M 91 24 L 429 24 L 452 53 L 429 82 L 91 82 L 68 53 Z',color,'#fff1c6',1.2)
    p('M 95 28 L 425 28 L 445 53 L 425 78 L 95 78 L 75 53 Z',color+'-inlay','#362c1c' if color=='gold' else '#003e34',2)
    p('M 96 31 L 424 31 L 440 53 L 424 75 L 96 75 L 80 53 Z','none','#e8d39d' if color=='gold' else '#9ce7c8',.8)
    if color=='emerald':
        for x,y,a in [(82,76,-48),(63,71,-56),(47,65,-63),(87,48,-24),(74,39,-30),(438,76,48),(457,71,56),(473,65,63),(433,48,24),(446,39,30)]: l(x,y,42,a,7)
        j(260,14,10,10)
    else:
        p('M 49 51 L 78 69 L 89 82 L 64 72 Z',color,'#fff0bd',.8)
        p('M 471 51 L 442 69 L 431 82 L 456 72 Z',color,'#fff0bd',.8)

def gradient_svg():
    return ''.join(f'<linearGradient id="{key}" x1="0" y1="0" x2="1" y2="1" gradientUnits="objectBoundingBox">'+''.join(f'<stop offset="{i/(len(colors)-1):.4f}" stop-color="{color}"/>' for i,color in enumerate(colors))+'</linearGradient>' for key,colors in palettes.items())

def paint(value): return 'none' if not value or value=='none' else f'url(#{value})' if value in palettes else value

svg_dir=ROOT/'src/PixelVault.Companion/wwwroot/completion-frames'
svg_dir.mkdir(exist_ok=True)
for name,asset in assets.items():
    dimensions=asset['size'] if isinstance(asset['size'],tuple) else (asset['size'],asset['size'])
    content=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {dimensions[0]} {dimensions[1]}"><defs>{gradient_svg()}</defs>'
    for op in asset['ops']:
        content+=f'<path d="{op["d"]}" fill="{paint(op["fill"])}" stroke="{paint(op["stroke"])}" stroke-width="{op["sw"]}" stroke-linejoin="round" stroke-linecap="round"/>'
    content+='</svg>'
    (svg_dir/(name+'.svg')).write_text(content,encoding='utf-8')

lines=['// Generated by scripts/Build-CompletionFrameAssets.py. Edit the shared generator.','using System;','using System.Collections.Generic;','using System.Windows;','using System.Windows.Media;','namespace PixelVaultNative {','internal static class CompletionOrnaments {','static readonly Dictionary<string,DrawingGroup> Art=Build();','static Dictionary<string,DrawingGroup> Build() {','var brushes=new Dictionary<string,Brush>();']
for key,colors in palettes.items():
    lines+=['{ var gradient=new LinearGradientBrush { StartPoint=new Point(0,0), EndPoint=new Point(1,1) };']
    for i,color in enumerate(colors): lines+=[f'gradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("{color}"),{i/(len(colors)-1):.8f}));']
    lines += [f'gradient.Freeze();brushes["{key}"]=gradient;}}']
lines+=['Brush Paint(string value) { if(value==null||value=="none")return null;if(brushes.TryGetValue(value,out var b))return b;var color=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));color.Freeze();return color;}','var art=new Dictionary<string,DrawingGroup>();']
for name,asset in assets.items():
    lines+=['{var g=new DrawingGroup();']
    for op in asset['ops']:
        stroke='null' if not op['stroke'] else 'new Pen(Paint('+json.dumps(op['stroke'])+'),'+str(op['sw'])+') { LineJoin=PenLineJoin.Round, StartLineCap=PenLineCap.Round, EndLineCap=PenLineCap.Round }'
        lines+=[f'g.Children.Add(new GeometryDrawing(Paint({json.dumps(op["fill"])}),{stroke},Geometry.Parse({json.dumps(op["d"])})));']
    lines += [f'g.Freeze();art["{name}"]=g;}}']
lines+=['return art;}','internal static void Add(DrawingGroup target,string key,double scale,double x,double y,bool rotate=false,bool mirror=false) {','var transform=new TransformGroup();if(mirror)transform.Children.Add(new ScaleTransform(-1,1,64,64));if(rotate)transform.Children.Add(new RotateTransform(180,64,64));transform.Children.Add(new ScaleTransform(scale,scale));transform.Children.Add(new TranslateTransform(x,y));var g=new DrawingGroup { Transform=transform };g.Children.Add(Art[key]);target.Children.Add(g);','}','}','}']
(ROOT/'src/PixelVault.Native/UI/Library/CompletionOrnaments.g.cs').write_text('\n'.join(lines)+'\n',encoding='utf-8')
print(f'Generated {len(assets)} matching SVG/WPF ornaments.')
