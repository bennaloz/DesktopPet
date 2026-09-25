import sys
from PIL import Image, ImageDraw
d=sys.argv[1]
spec={'side':('y',600,1,'z',970,-1),'top':('x',600,1,'y',600,-1),'front':('x',600,1,'z',970,-1)}
for name,(ha,h0,hs,va,v0,vs) in spec.items():
    im=Image.open(f'{d}/{name}.png').convert('RGB'); dr=ImageDraw.Draw(im)
    for k in range(-60,61,5):
        v=k/100; x=h0+hs*1000*v; y=v0+vs*1000*v
        col=(255,0,0) if k%10==0 else (255,170,170)
        if 0<=x<1200: dr.line([(x,0),(x,1199)],fill=col,width=1); 
        if 0<=y<1200: dr.line([(0,y),(1199,y)],fill=col,width=1)
        if k%10==0:
            if 0<=x<1200: dr.text((x+2,2),f'{ha}{v:+.1f}',fill=(0,0,160))
            if 0<=y<1200: dr.text((2,y+2),f'{va}{v:+.1f}',fill=(0,0,160))
    im.save(f'{d}/{name}_grid.png')
