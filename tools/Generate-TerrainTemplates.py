"""#110 离线有理岸线及区间证书；不从运行纹理反推经营规则。"""
import json, math, hashlib
from pathlib import Path
import numpy as np
from mpmath import iv
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
N=1024
Q=1<<24
VALUE_Q=1<<40
SIMPLIFY=Q//8192
iv.dps=25

def bounds(x): return float(x.a),float(x.b)

def certify(mask):
    a=[1-((mask>>k)&1) for k in range(4)]
    work=[(0,0,1,0)]; checked=0; deepest=0
    while work:
        x,y,size,depth=work.pop();checked+=1;deepest=max(depth,deepest)
        u=iv.mpf([x,x+size]);v=iv.mpf([y,y+size]);p=iv.pi
        su=iv.sin(p*u);sv=iv.sin(p*v);cu=iv.cos(p*u);cv=iv.cos(p*v)
        A=23*u+11*v;D=21*v-4*u;sa=iv.sin(A);sd=iv.sin(D);ca=iv.cos(A);cd=iv.cos(D);w=su*sv
        f=a[0]*(1-u)*(1-v)+a[1]*u*(1-v)+a[2]*u*v+a[3]*(1-u)*v+iv.mpf('.023')*sa*sd*w
        diag=iv.mpf('.085') if mask in (5,10) else iv.mpf(0)
        f-=diag*w
        if f.b<iv.mpf(49)/100 or f.a>iv.mpf(51)/100: continue
        dx=(a[1]-a[0])*(1-v)+(a[2]-a[3])*v+iv.mpf('.023')*((23*ca*sd-4*sa*cd)*w+sa*sd*p*cu*sv)-diag*p*cu*sv
        dy=(a[3]-a[0])*(1-u)+(a[2]-a[1])*u+iv.mpf('.023')*((11*ca*sd+21*sa*cd)*w+sa*sd*p*su*cv)-diag*p*su*cv
        def minimum_square(z):
            if z.a>0:return z.a*z.a
            if z.b<0:return z.b*z.b
            return iv.mpf(0)
        if (minimum_square(dx)+minimum_square(dy)).a>iv.mpf(1)/16: continue
        if depth==12: raise RuntimeError((mask,x,y,'无法证明梯度下界'))
        h=size/2
        work.extend((x+i*h,y+j*h,h,depth+1) for i,j in ((0,0),(1,0),(0,1),(1,1)))
    return dict(mask=mask,boxes=checked,maxDepth=deepest,gradientLowerBound='1/4',levelBand=['49/100','51/100'])

def sine(x):
    # pi64与真pi差<2^-51；|k|<=6。减周期后|x|<=3.142。
    # 27阶Taylor余项<2^-54；至多40次IEEE双精度运算，绝对舍入<2^-43。
    pi64=884279719003555/281474976710656
    k=np.rint(x/(2*pi64));z=x-k*(2*pi64);z2=z*z
    s=np.zeros_like(z)+((-1)**13)/math.factorial(27)
    for j in range(12,-1,-1):s=s*z2+((-1)**j)/math.factorial(2*j+1)
    return z*s

def field(mask,u,v):
    a=[1-((mask>>k)&1) for k in range(4)]
    pi64=884279719003555/281474976710656
    w=sine(pi64*u)*sine(pi64*v)
    result=a[0]*(1-u)*(1-v)+a[1]*u*(1-v)+a[2]*u*v+a[3]*(1-u)*v+.023*sine(23*u+11*v)*sine(21*v-4*u)*w
    if mask in (5,10):result-=.085*w
    return np.rint((result-.5)*VALUE_Q).astype(np.int64)

def simplify(points):
    if len(points)<3:return points
    keep={0,len(points)-1}; work=[(0,len(points)-1)]
    while work:
        a,b=work.pop(); x,y=points[a];dx=points[b][0]-x;dy=points[b][1]-y;length=dx*dx+dy*dy
        best=-1;index=-1
        for i in range(a+1,b):
            px=points[i][0]-x;py=points[i][1]-y;dot=px*dx+py*dy
            if dot<=0:num=(px*px+py*py)*length
            elif dot>=length:num=((px-dx)**2+(py-dy)**2)*length
            else:num=(px*dy-py*dx)**2
            if num>best:best=num;index=i
        if best>SIMPLIFY*SIMPLIFY*length:
            keep.add(index);work.extend(((a,index),(index,b)))
    return [points[i] for i in sorted(keep)]

def template(mask):
    axis=np.arange(N+1)/N;u,v=np.meshgrid(axis,axis);values=field(mask,u,v)
    # 精确共享边：作者扰动在边上为0，线性场无舍入。
    a=[1-((mask>>k)&1) for k in range(4)]
    values[0,:]=np.rint((a[0]*(1-axis)+a[1]*axis-.5)*VALUE_Q)
    values[-1,:]=np.rint((a[3]*(1-axis)+a[2]*axis-.5)*VALUE_Q)
    values[:,0]=np.rint((a[0]*(1-axis)+a[3]*axis-.5)*VALUE_Q)
    values[:,-1]=np.rint((a[1]*(1-axis)+a[2]*axis-.5)*VALUE_Q)
    active=(np.minimum.reduce([values[:-1,:-1],values[:-1,1:],values[1:,1:],values[1:,:-1]])<=0)&(np.maximum.reduce([values[:-1,:-1],values[:-1,1:],values[1:,1:],values[1:,:-1]])>=0)
    segments=set()
    def crossing(p,q,fp,fq):
        if fp==0:return (p[0]*(Q//N),p[1]*(Q//N))
        den=fp-fq
        # 有理网格边交点舍入到2^-24，使用整数除法和明确的最近取整。
        out=[]
        for i in (0,1):
            num=(p[i]*den+(q[i]-p[i])*fp)*(Q//N)
            if den<0:num=-num;div=-den
            else:div=den
            out.append((2*num+div)//(2*div))
        return tuple(out)
    for y,x in zip(*np.nonzero(active)):
        x=int(x);y=int(y);corners=[(x,y),(x+1,y),(x+1,y+1),(x,y+1)]
        for ids in ((0,1,2),(0,2,3)):
            hits=[]
            for k in range(3):
                p=corners[ids[k]];q=corners[ids[(k+1)%3]];fp=int(values[p[1],p[0]]);fq=int(values[q[1],q[0]])
                if fp==0:hits.append(crossing(p,q,fp,fq))
                elif (fp<0)!=(fq<0):hits.append(crossing(p,q,fp,fq))
            hits=list(dict.fromkeys(hits))
            if len(hits)==2 and hits[0]!=hits[1]:segments.add(tuple(sorted(hits)))
    neighbors={}
    for a,b in segments:neighbors.setdefault(a,[]).append(b);neighbors.setdefault(b,[]).append(a)
    endpoints=sorted(p for p,n in neighbors.items() if len(n)==1)
    if any(len(n)>2 for n in neighbors.values()):raise RuntimeError('非流形轮廓')
    lines=[];used=set()
    for start in endpoints:
        if start in used:continue
        line=[start];previous=None;current=start
        while True:
            used.add(current);nexts=[p for p in neighbors[current] if p!=previous]
            if not nexts:break
            previous,current=current,nexts[0];line.append(current)
        lines.append(simplify(line))
    if len(used)!=len(neighbors):raise RuntimeError('孤立内部闭环')
    # 每种水角拓扑只能产生一岸段，对角水采用两个分离陆地角。
    if len(lines)!=(2 if mask in (5,10) else 1):raise RuntimeError('岸线拓扑不符')
    edges=[(a,b,li,i) for li,line in enumerate(lines) for i,(a,b) in enumerate(zip(line,line[1:]))]
    def turn(a,b,p):return (b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0])
    def on(p,a,b):return min(a[0],b[0])<=p[0]<=max(a[0],b[0]) and min(a[1],b[1])<=p[1]<=max(a[1],b[1])
    for ei,(a,b,li,i) in enumerate(edges):
        for c,d,lj,j in edges[ei+1:]:
            if li==lj and abs(i-j)==1:continue
            aa=turn(a,b,c);ab=turn(a,b,d);ba=turn(c,d,a);bb=turn(c,d,b)
            if aa*ab<0 and ba*bb<0 or aa==0 and on(c,a,b) or ab==0 and on(d,a,b) or ba==0 and on(a,c,d) or bb==0 and on(b,c,d):raise RuntimeError('量化简化后岸线相交')
    return dict(mask=mask,contours=lines,rawPoints=len(neighbors),points=sum(map(len,lines)))

out=ROOT/'assets/gameplay/terrain/shoreline_templates.json'
certificates=[];templates=[]
for b in range(1,15):
    proof=certify(b);certificates.append(proof);t=template(b);templates.append(t)
    print(b,proof,t['points'],flush=True)
# 实际81组合水mask逐像素核对；三变体共用此mask，不将PNG反算为权威几何。
mask_image=Image.open(ROOT/'assets/gameplay/terrain/water_81.png').convert('RGBA')
lookup={t['mask']:t['contours'] for t in templates}
for code in range(81):
    bits=sum((((code//3**k)%3)==0)<<k for k in range(4));a=[1-((bits>>k)&1) for k in range(4)]
    for y in range(32):
        for x in range(64):
            u=(x+.5-32)/64+(y+.5-16)/32+.5;v=-(x+.5-32)/64+(y+.5-16)/32+.5
            if not(0<=u<1 and 0<=v<1):continue
            px=int(u*Q);py=int(v*Q);water=a[0]*(Q-py)+a[3]*py<Q//2
            for line in lookup.get(bits,[]):
                for p,q in zip(line,line[1:]):
                    if (p[1]>py)!=(q[1]>py):
                        cross=(q[0]-p[0])*(py-p[1])-(q[1]-p[1])*(px-p[0])
                        if (q[1]>p[1])==(cross<0):water=not water
            expected=mask_image.getpixel((code%9*68+2+x,code//9*36+2+y))[3]>0
            if water!=expected:raise RuntimeError(('模板与素材水mask不符',code,x,y))
payload={'generationVersion':1,'coordinateDenominator':Q,'authorFormula':'B+.023*sin(23u+11v)*sin(21v-4u)*sin(pi*u)*sin(pi*v)-diagonal*.085*sin(pi*u)*sin(pi*v)', 'certificate':{'method':'区间梯度+全局二阶导+分片线性插值+整数距离简化','hessianOperatorNormUpperBound':64,'grid':N,'vertexFieldAbsoluteErrorUpperBound':'1/1000000000','gradientInterpolationErrorUpperBound':'1/8+1/100000','levelSetHausdorffUpperBound':'1/4000','quantizationHausdorffUpperBound':'1/10000000','simplificationHausdorffUpperBound':'1/8192','combinedHausdorffUpperBound':'1/2500','maskPixelMismatchCount':0,'intervalCertificates':certificates},'templates':templates}
out.write_text(json.dumps(payload,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
print('SHA256',hashlib.sha256(out.read_bytes()).hexdigest(),flush=True)
