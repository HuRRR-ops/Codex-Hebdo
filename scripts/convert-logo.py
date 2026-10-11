import re, pathlib, xml.etree.ElementTree as ET
root=pathlib.Path(__file__).resolve().parents[1]
d=ET.parse(root/'Assets/lolo.svg').getroot()[0].attrib['d']
tokens=re.findall(r'[A-Za-z]|[-+]?(?:\d*\.)?\d+(?:[eE][-+]?\d+)?',d)
x=y=sx=sy=0.0
out=[]
i=0
cmd=None
sizes={'M':2,'L':2,'H':1,'V':1,'Q':4,'A':7}
def emit(c,values=()):
    out.append(c+' '+ ' '.join(format(v,'.8g') for v in values))
while i<len(tokens):
    if tokens[i].isalpha():
        cmd=tokens[i];i+=1
        if cmd.upper()=='Z':
            emit('Z');x,y=sx,sy;cmd=None;continue
    upper=cmd.upper();relative=cmd.islower()
    n=sizes[upper]
    p=list(map(float,tokens[i:i+n]));i+=n
    if upper in ('M','L'):
        nx,ny=p
        if relative:nx+=x;ny+=y
        emit(upper,(nx,ny));x,y=nx,ny
        if upper=='M':sx,sy=x,y;cmd='l' if relative else 'L'
    elif upper=='H':
        x=p[0]+(x if relative else 0);emit('L',(x,y))
    elif upper=='V':
        y=p[0]+(y if relative else 0);emit('L',(x,y))
    elif upper=='Q':
        cx,cy,nx,ny=p
        if relative:cx+=x;cy+=y;nx+=x;ny+=y
        emit('Q',(cx,cy,nx,ny));x,y=nx,ny
    elif upper=='A':
        if relative:p[5]+=x;p[6]+=y
        emit('A',p);x,y=p[5:7]
path='F1 '+' '.join(out)
(root/'Windhawk/logo.inc').write_text('// SVG commands expanded to absolute coordinates to preserve SVG subpath semantics in XAML.\nstatic constexpr wchar_t kLogoPath[] = LR"('+path+')";\n',encoding='utf-8')
print('Converted',len(out),'explicit absolute SVG commands.')
