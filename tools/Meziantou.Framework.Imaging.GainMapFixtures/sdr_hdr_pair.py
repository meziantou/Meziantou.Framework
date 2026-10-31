# The two renditions of the scene that avifgainmaputil combines: an SDR base image (a simple per-channel tone map, sRGB)
# and the HDR scene encoded with PQ (203 cd/m2 for the white), in the BT.709 and BT.2020 primaries.
import numpy as np, sys
from PIL import Image
w,h=int(sys.argv[1]),int(sys.argv[2])
lin=np.fromfile('hdr.rgbahalf',dtype=np.float16).reshape(h,w,4)[...,:3].astype(np.float64)
def srgb_oetf(x):
    x=np.clip(x,0,1); return np.where(x<=0.0031308,12.92*x,1.055*x**(1/2.4)-0.055)
def pq_oetf(nits):
    y=np.clip(nits/10000.0,0,1); m1=2610/16384; m2=2523/4096*128; c1=3424/4096; c2=2413/4096*32; c3=2392/4096*32
    ym=y**m1; return ((c1+c2*ym)/(1+c3*ym))**m2
sdr=lin/(1+lin)*1.6
Image.fromarray((srgb_oetf(sdr)*255+0.5).astype(np.uint8)).save('sdr.png')
def ppm(name,arr):
    with open(name,'wb') as f:
        f.write(f"P6 {w} {h} 65535\n".encode()); f.write((pq_oetf(arr*203.0)*65535+0.5).astype('>u2').tobytes())
ppm('hdr709.ppm',lin)
M=np.array([[0.6274,0.3293,0.0433],[0.0691,0.9195,0.0114],[0.0164,0.0880,0.8956]])
ppm('hdr2020.ppm',lin@M.T)
