# A synthetic high dynamic range scene: linear-light RGBA half floats (1 is the white of standard dynamic range).
import numpy as np, sys
w,h=int(sys.argv[1]),int(sys.argv[2]); out=sys.argv[3]
y,x=np.mgrid[0:h,0:w].astype(np.float32)
r=(x/(w-1))**2*6.0+0.02
g=(y/(h-1))*3.0+0.05
b=np.abs(np.sin(x/7.0+y/5.0))*1.5+0.01
# a bright spot
d=((x-w*0.7)**2+(y-h*0.3)**2)**0.5
spot=np.clip(1-d/(min(w,h)*0.25),0,1)*8
img=np.stack([r+spot,g+spot*0.8,b+spot*0.6,np.ones_like(r)],-1).astype(np.float16)
img.tofile(out)
