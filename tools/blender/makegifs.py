import sys,json,glob,os
from PIL import Image
d=sys.argv[1]; idx=json.load(open(f'{d}/_index.json'))
BG=(246,243,238)
for name,(n,step) in idx.items():
    frames=[]
    for i in range(n):
        c=Image.new('RGB',(720,300),BG)
        for k,v in enumerate(('side','tq')):
            im=Image.open(f'{d}/_{name}_{v}_{i:03d}.png').convert('RGBA'); c.paste(im,(k*360,0),im)
        frames.append(c.convert('P',palette=Image.ADAPTIVE,colors=128))
    frames[0].save(f'{d}/{name}.gif',save_all=True,append_images=frames[1:],duration=int(1000*step/30),loop=0,optimize=True,disposal=2)
    print(name,n,os.path.getsize(f'{d}/{name}.gif')//1024,'KB')
for f in glob.glob(f'{d}/_*.png'): os.remove(f)
