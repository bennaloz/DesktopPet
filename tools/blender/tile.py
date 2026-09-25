import sys,os
from PIL import Image, ImageDraw
d=sys.argv[1]; name=sys.argv[2]
rows=[l.split(',') for l in open(f'{d}/_rows.txt').read().split('\n') if l]
W=300; sheet=Image.new('RGB',(W*6+130,W*len(rows)),(30,30,30)); dr=ImageDraw.Draw(sheet)
for r,(a,n) in enumerate(rows):
    dr.text((5,r*W+140),a,fill=(255,255,255))
    for i in range(int(n)):
        p=f'{d}/_f_{a}_{i}.png'
        sheet.paste(Image.open(p).convert('RGB'),(130+i*W,r*W)); os.remove(p)
sheet.save(f'{d}/{name}')
