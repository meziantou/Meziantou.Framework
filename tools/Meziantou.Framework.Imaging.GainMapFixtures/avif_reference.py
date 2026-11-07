# Reference rendition of an AVIF file with a gain map: the formula of ISO 21496-1 evaluated with numpy on the planes that
# libavif decodes (the base image at headroom 0, and the gain map that extractgainmap writes losslessly), at full weight.
import numpy as np, subprocess, sys
def rd(path):
    out=subprocess.run(['identify','-format','%w %h',path],check=True,capture_output=True,text=True).stdout.split()
    w,h=int(out[0]),int(out[1])
    raw=subprocess.run(['convert',path,'-depth','16','-endian','MSB','rgb:-'],check=True,capture_output=True).stdout
    return np.frombuffer(raw,dtype='>u2').reshape(h,w,3).astype(np.float64)/65535
def srgb_eotf(v): return np.where(v<=0.04045,v/12.92,((v+0.055)/1.055)**2.4)
def to_xyz(xr,yr,xg,yg,xb,yb,xw,yw):
    P=np.array([[x/y,1,(1-x-y)/y] for x,y in [(xr,yr),(xg,yg),(xb,yb)]]).T
    W=np.array([xw/yw,1,(1-xw-yw)/yw]); return P*np.linalg.solve(P,W)
BT709=to_xyz(0.64,0.33,0.30,0.60,0.15,0.06,0.3127,0.3290); BT2020=to_xyz(0.708,0.292,0.170,0.797,0.131,0.046,0.3127,0.3290)
name=sys.argv[1]; alternate=sys.argv[2] if len(sys.argv)>2 else None
subprocess.run(['avifgainmaputil','tonemap',name,'/tmp/base.png','--headroom','0','-d','12'],check=True,capture_output=True)
subprocess.run(['avifgainmaputil','extractgainmap',name,'/tmp/gm.png','-q','100'],check=True,capture_output=True)
# The tone map item, parsed here: avifgainmaputil printmetadata prints the values of the red channel for all three
import struct
data=open(name,'rb').read()
def box(d,o,e):
    while o<e:
        sz,t=struct.unpack('>I4s',d[o:o+8]); yield t.decode('latin1'),o+8,o+sz; o+=sz
meta=next((s,e) for t,s,e in box(data,0,len(data)) if t=='meta')
kids={t:(s,e) for t,s,e in box(data,meta[0]+4,meta[1])}
s0,e0=kids['iinf']; v=data[s0]; types={}
for t,s,e in box(data,s0+(6 if v==0 else 8),e0):
    iv=data[s]; iid=struct.unpack('>H',data[s+4:s+6])[0]; types[data[s+8:s+12].decode()]=iid
s0,e0=kids['iloc']; v=data[s0]; sizes=data[s0+4]; bsz=data[s0+5]>>4
assert sizes==0x44 and v in (0,1)
n=struct.unpack('>H',data[s0+6:s0+8])[0]; q=s0+8; loc={}
for _ in range(n):
    iid=struct.unpack('>H',data[q:q+2])[0]; q+=2
    if v==1: q+=2
    q+=2; base_off=int.from_bytes(data[q:q+bsz],'big'); q+=bsz
    ec=struct.unpack('>H',data[q:q+2])[0]; q+=2
    off,ln=struct.unpack('>II',data[q:q+8]); q+=8*ec; loc[iid]=(base_off+off,ln)
o,ln=loc[types['tmap']]; t=data[o:o+ln]
assert t[0]==0 and struct.unpack('>H',t[1:3])[0]==0
flags=t[5]; nch=3 if flags&0x80 else 1; q=6+16
def rat(signed):
    global q
    n=struct.unpack('>i' if signed else '>I',t[q:q+4])[0]; d=struct.unpack('>I',t[q+4:q+8])[0]; q+=8; return n/d
vals=np.array([[rat(True),rat(True),rat(False),rat(True),rat(True)] for _ in range(nch)])
if nch==1: vals=np.repeat(vals,3,axis=0)
gmin,gmax,gamma,boff,aoff=(vals[:,i] for i in range(5))
base=srgb_eotf(rd('/tmp/base.png')); h,w,_=base.shape
if alternate=='bt2020': base=base@(np.linalg.inv(BT2020)@BT709).T
g=rd('/tmp/gm.png'); gh,gw,_=g.shape
# bilinear, pixel centers aligned, clamped at the edges
def coords(n,gn):
    p=np.clip((np.arange(n)+0.5)*gn/n-0.5,0,gn-1); i0=np.floor(p).astype(int); i1=np.minimum(i0+1,gn-1); return i0,i1,p-i0
y0,y1,fy=coords(h,gh); x0,x1,fx=coords(w,gw)
top=g[y0][:,x0]*(1-fx)[None,:,None]+g[y0][:,x1]*fx[None,:,None]
bot=g[y1][:,x0]*(1-fx)[None,:,None]+g[y1][:,x1]*fx[None,:,None]
gs=top*(1-fy)[:,None,None]+bot*fy[:,None,None]
gain=gmin+(gmax-gmin)*np.power(gs,1/gamma)
alt=(base+boff)*np.exp2(gain)-aoff
alt.astype(np.float16).tofile(name.replace('.avif','.reference.f16'))
print(name,w,h,'gain map',gw,gh,'max',alt.max())
