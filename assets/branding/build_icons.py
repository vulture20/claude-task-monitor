# Erzeugt png/*.png und app.ico aus den SVGs. Aufruf: python build_icons.py .  (benötigt cairosvg, Pillow)
import cairosvg, io, sys, os
from PIL import Image
d = sys.argv[1]
os.makedirs(f"{d}/png", exist_ok=True)
def src(s): return "icon-16.svg" if s < 24 else ("icon-32.svg" if s <= 32 else "logo.svg")
imgs = {}
for s in [16,20,24,32,40,48,64,128,256,512]:
    data = cairosvg.svg2png(url=f"{d}/{src(s)}", output_width=s, output_height=s)
    open(f"{d}/png/icon-{s}.png","wb").write(data)
    imgs[s] = Image.open(io.BytesIO(data)).convert("RGBA")
ico = [16,20,24,32,40,48,64,128,256]
imgs[256].save(f"{d}/app.ico", format="ICO", sizes=[(s,s) for s in ico], append_images=[imgs[s] for s in ico if s!=256])
cairosvg.svg2png(url=f"{d}/logo-horizontal.svg", write_to=f"{d}/png/logo-horizontal.png", output_width=760)
