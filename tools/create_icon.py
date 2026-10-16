from pathlib import Path
from PIL import Image, ImageDraw
import math

script_dir=Path(__file__).resolve().parent
assets=next(path for path in [script_dir/'LyricBar/Assets', script_dir.parent/'Assets'] if path.is_dir())
size=1024; scale=size/256
image=Image.new('RGBA',(size,size)); pixels=image.load()
start=(18,185,199); end=(102,96,232)
for y in range(size):
    for x in range(size):
        t=(x+y)/(2*(size-1)); pixels[x,y]=tuple(round(a+(b-a)*t) for a,b in zip(start,end))+(255,)
mask=Image.new('L',(size,size)); ImageDraw.Draw(mask).rounded_rectangle([32,32,992,992],radius=208,fill=255); image.putalpha(mask)
draw=ImageDraw.Draw(image)
draw.polygon([(x*scale,y*scale) for x,y in [(90,76),(176,54),(176,145),(160,149),(160,83),(106,97),(106,165),(90,168)]],fill='white')
for cx,cy in [(79,165),(149,146)]:
    points=[]
    for i in range(128):
        a=i*math.tau/128; x=28*math.cos(a); y=19*math.sin(a); angle=math.radians(-15)
        points.append(((cx+x*math.cos(angle)-y*math.sin(angle))*scale,(cy+x*math.sin(angle)+y*math.cos(angle))*scale))
    draw.polygon(points,fill='white')
for box,alpha in [((137,193,196,202),242),((137,210,177,219),179)]:
    overlay=Image.new('RGBA',image.size); ImageDraw.Draw(overlay).rounded_rectangle(tuple(round(v*scale) for v in box),radius=18,fill=(255,255,255,alpha)); image=Image.alpha_composite(image,overlay)
preview=image.resize((256,256),Image.Resampling.LANCZOS); preview.save(assets/'AppIcon.png')
preview.save(assets/'App.ico',format='ICO',sizes=[(n,n) for n in [16,24,32,48,64,128,256]])
print('Created original SVG, PNG and seven-size ICO assets.')
