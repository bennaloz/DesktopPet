import sys,glob,os
from PIL import Image
d,name,n,ms=sys.argv[1],sys.argv[2],int(sys.argv[3]),int(sys.argv[4])
frames=[]
for i in range(n):
    a=Image.open(f'{d}/_g_side_{i:03d}.png').convert('RGB'); b=Image.open(f'{d}/_g_tq_{i:03d}.png').convert('RGB')
    c=Image.new('RGB',(720,300)); c.paste(a,(0,0)); c.paste(b,(360,0)); frames.append(c)
frames[0].save(f'{d}/{name}',save_all=True,append_images=frames[1:],duration=ms,loop=0)
for f in glob.glob(f'{d}/_g_*.png'): os.remove(f)
print('gif',name,len(frames))
