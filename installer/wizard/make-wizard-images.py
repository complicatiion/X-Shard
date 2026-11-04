# Regenerates the Inno Setup wizard images (run from the repository root): python installer/wizard/make-wizard-images.py
# Needs Pillow and the Poppins font (Bold/Regular); adjust the font paths below on Windows.

import sys,glob
from PIL import Image,ImageDraw,ImageFont,ImageFilter
logo=Image.open('src/XShard/Assets/app-logo-color.png').convert('RGBA')
fonts=['/usr/share/fonts/truetype/google-fonts/Poppins-Bold.ttf']
fontpath=sorted(fonts)[0] if fonts else None
def lerp(a,b,t):return tuple(round(a[i]+(b[i]-a[i])*t) for i in range(3))
TOP=(0x0F,0x15,0x11);MID=(0x16,0x2C,0x1A);BOT=(0x2A,0x7F,0x36)
def gradient(w,h):
    im=Image.new('RGB',(w,h));px=im.load()
    for y in range(h):
        for x in range(w):
            t=min(1,max(0,(y/h)*0.85+(x/w)*0.15))
            c=lerp(TOP,MID,t/0.5) if t<0.5 else lerp(MID,BOT,(t-0.5)/0.5)
            px[x,y]=c
    # soft radial glow behind the logo
    glow=Image.new('L',(w,h),0);d=ImageDraw.Draw(glow);r=int(w*0.55);cx,cy=w//2,int(h*0.36) if h>w else h//2
    d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=38);glow=glow.filter(ImageFilter.GaussianBlur(r*0.45))
    im=Image.composite(Image.new('RGB',(w,h),(0x6A,0x8A,0x70)),im,glow)
    return im
def large(scale):
    w,h=round(164*scale),round(314*scale)
    im=gradient(w,h).convert('RGBA')
    s=int(w*0.74);lg=logo.resize((s,s),Image.LANCZOS)
    # subtle shadow
    sh=Image.new('RGBA',lg.size,(0,0,0,0));sh.putalpha(lg.getchannel('A').point(lambda a:int(a*0.45)));sh=sh.filter(ImageFilter.GaussianBlur(max(2,s//40)))
    x,y=(w-s)//2,int(h*0.36)-s//2
    im.alpha_composite(Image.merge('RGBA',(Image.new('L',sh.size,0),)*3+(sh.getchannel('A'),)),(x,y+max(2,s//50)))
    im.alpha_composite(lg,(x,y))
    d=ImageDraw.Draw(im)
    if fontpath:
        f=ImageFont.truetype(fontpath,int(w*0.16));t='X-Shard';tw=d.textlength(t,font=f)
        d.text(((w-tw)/2,int(h*0.62)),t,font=f,fill=(255,255,255,255))
        f2=ImageFont.truetype('/usr/share/fonts/truetype/google-fonts/Poppins-Regular.ttf',int(w*0.068))
        for i,line in enumerate(['Xbox 360 & Xbox Classic','ISO tool']):
            lw=d.textlength(line,font=f2);d.text(((w-lw)/2,int(h*0.62)+int(w*0.22)+i*int(w*0.09)),line,font=f2,fill=(0xB7,0xE4,0x9E,255))
    return im.convert('RGB')
def small(scale):
    n=round(55*scale);im=gradient(n,n).convert('RGBA')
    s=int(n*0.86);lg=logo.resize((s,s),Image.LANCZOS);im.alpha_composite(lg,((n-s)//2,(n-s)//2))
    return im.convert('RGB')
for pct,sc in [(100,1),(125,1.25),(150,1.5),(175,1.75),(200,2)]:
    large(sc).save(f'installer/wizard/wizard-large-{pct}.bmp');small(sc).save(f'installer/wizard/wizard-small-{pct}.bmp')
print(fontpath)
