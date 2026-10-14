# -*- coding: utf-8 -*-
"""#149 可丢弃方案原型：原素材排版，不修改游戏或源素材。运行：python build/issue149-prototype/make_prototype.py"""
from pathlib import Path
from PIL import Image
import base64
import io
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
BUILDINGS = [
    ('wheat', '磨坊', '小麦', '面粉'), ('corn', '玉米加工坊', '玉米', '玉米粉'),
    ('rice', '碾米坊', '水稻', '大米'), ('potato', '淀粉坊', '马铃薯', '淀粉'),
    ('sunflower', '榨油坊', '向日葵', '葵花籽油'), ('sugarcane', '制糖坊', '甘蔗', '蔗糖'),
    ('radish', '腌制坊', '萝卜', '腌萝卜')]
ROADS = [('dirt', '土路'), ('gravel', '碎石道路'), ('cobble', '卵石道路'), ('brick', '红砖道路'), ('slab', '石板道路')]

def data_url(path):
    suffix = 'svg+xml' if path.suffix == '.svg' else 'png'
    return 'data:image/' + suffix + ';base64,' + base64.b64encode(path.read_bytes()).decode()

def png_url(image):
    stream = io.BytesIO()
    image.save(stream, format='PNG')
    return 'data:image/png;base64,' + base64.b64encode(stream.getvalue()).decode()

regions = json.loads((ROOT / 'assets/gameplay/terrain/roads/atlas_regions.json').read_text(encoding='utf-8'))
def road(material, masks):
    atlas = Image.open(ROOT / f'assets/gameplay/terrain/roads/road_{material}_atlas.png').convert('RGBA')
    image = Image.new('RGBA', (64 + (len(masks)-1)*32, 32 + (len(masks)-1)*16))
    for index, mask in enumerate(masks):
        x, y, w, h = next(r['region'] for r in regions if r['material'] == material and r['mask'] == mask and r['variant'] == 0)
        image.alpha_composite(atlas.crop((x, y, x+w, y+h)), (index*32, index*16))
    return image

def cropped(image):
    return image.crop(image.getchannel('A').getbbox())

items = [dict(name='农田', category='farm', detail='工人自动播种、浇水', fee='建造费 10.00 金币', size='3×3',
              A=data_url(ROOT/'assets/ui/facilities/field_wheat.svg'), B=data_url(ROOT/'assets/ui/facilities/field_wheat.svg'), C=data_url(ROOT/'assets/ui/facilities/field_wheat.svg'))]
metrics = []
for crop, name, raw, product in BUILDINGS:
    source = ROOT/f'assets/gameplay/buildings/{crop}_workshop_q0.png'
    image = Image.open(source).convert('RGBA')
    bbox = image.getchannel('A').getbbox()
    url = png_url(cropped(image))
    items.append(dict(name=name, category='processor', detail=f'{raw} → {product}', fee='建造费 10.00 金币', size='3×3', A=url, B=url,
                      C=data_url(ROOT/f"assets/ui/facilities/{'mill' if crop=='wheat' else 'workshop'}.svg")))
    metrics.append(dict(source=str(source.relative_to(ROOT)), canvas=list(image.size), alpha_bbox=list(bbox)))
for material, name in ROADS:
    items.append(dict(name=name, category='road', detail='短按起点与终点铺设', fee='建造费 1.00 金币/格', size='1×1',
                      A=png_url(cropped(road(material, [2,10,8]))), B=png_url(cropped(road(material,[0]))), C=data_url(ROOT/'assets/ui/facilities/road.svg')))
items.append(dict(name='拆除道路', category='road', detail='短按起点与终点拆路', fee='只拆道路 · 不退款', size='直线范围', removal=True,
                  A=png_url(cropped(road('slab',[2,10,8]))), B=png_url(cropped(road('slab',[0]))), C=data_url(ROOT/'assets/ui/facilities/road.svg')))

html = r'''<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>#149 建造目录缩略图方案原型</title>
<style>
*{box-sizing:border-box}body{margin:0;background:#d8ddc8;color:#53452f;font-family:'Microsoft YaHei',sans-serif}.page{width:1920px;height:1080px;position:relative;padding:40px 44px;background:linear-gradient(150deg,#dde1cf,#cbd4b8)}
h1{font-size:29px;margin:0 0 10px;font-weight:600}.subtitle{font-size:16px;color:#6b7259}.badge{float:right;padding:10px 18px;color:#fff8e6;background:#657342;font-size:15px}.main{display:grid;grid-template-columns:790px 1fr;gap:40px;margin-top:34px}.section-title{font-size:18px;font-weight:600;margin-bottom:12px}.muted{font-size:13px;color:#817257;font-weight:normal}.window{height:620px;width:790px;border:5px solid #846848;box-shadow:inset 0 0 0 2px #c8ad76,5px 5px 0 #554b3428;background:#f6e8c6;padding:2px;display:flex;flex-direction:column}.header{height:64px;flex:none;background:#e6d3a4;padding:9px 17px;position:relative}.header small{font-size:9px;color:#817257}.header h2{margin:3px 0 0;font-size:19px;font-weight:normal}.close{position:absolute;right:17px;top:18px;width:28px;height:28px;border:2px solid #b9a477;background:#f6e8c6;font-size:22px;color:#53452f}.body{padding:17px;display:flex;flex-direction:column;min-height:0;flex:1;gap:10px}.tabs{display:flex;gap:8px;height:34px;flex:none}.tabs button,.tabs input{font-family:inherit;font-size:11px;height:34px;background:#f6e8c6;color:#53452f;border:2px solid #b9a477}.tabs button:nth-child(1){width:78px}.tabs button:nth-child(2){width:85px}.tabs button:nth-child(3){width:116px}.tabs button:nth-child(4){width:85px}.tabs button.active{background:#dad5a5}.tabs input{min-width:180px;flex:1;background:#fff8e6;padding-left:10px}.scroll{overflow-y:auto;flex:1;padding-right:9px}.grid{display:grid;grid-template-columns:repeat(3,minmax(220px,1fr));gap:12px}.card{height:200px;background:#e5d5a7;border:2px solid #b9a477;border-bottom-width:3px;display:flex;align-items:center;flex-direction:column;padding:8px 9px 7px;color:#53452f;cursor:pointer;font-family:inherit}.card:hover{background:#ecdfba}.art{width:145px;height:108px;display:flex;align-items:flex-end;justify-content:center;position:relative;flex:none;padding-bottom:4px}.art img{width:145px;height:104px;object-fit:contain;image-rendering:pixelated}.art.svg img{width:145px;height:91px;image-rendering:auto;object-fit:contain}.caption{font-size:12px;line-height:21px;text-align:center;margin-top:8px}.fee{color:#817257}.hint{font-size:10px;line-height:18px;flex:none;color:#817257;margin:0}.collection{display:grid;grid-template-columns:repeat(4,1fr);gap:12px}.sample{height:172px;border:1px solid #c1b182;background:#f6e8c6;display:flex;flex-direction:column;align-items:center;padding:8px 4px}.sample .caption{margin-top:5px}.sample:nth-child(8){justify-content:center;align-items:flex-start;padding:18px;color:#817257;font-size:13px;line-height:23px}.road-collection{display:grid;grid-template-columns:repeat(6,1fr);gap:10px}.road-collection .sample{height:172px}.road-collection .caption{font-size:12px;line-height:20px}.notes{padding:18px 22px;margin-top:20px;background:#f6e8c6;border:2px solid #b9a477;line-height:25px;font-size:14px}.notes b{font-weight:600}.label{color:#657342;font-size:13px}.road-section{margin-top:24px}.removal-mark{position:absolute;right:3px;bottom:10px;width:32px;height:32px;border:2px solid #fff8e6;background:#9c5542;display:grid;place-items:center;box-shadow:0 0 0 1px #9c5542}.removal-mark svg{width:23px;height:23px;stroke:#fff8e6;fill:none;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}.switcher{position:fixed;left:50%;bottom:27px;transform:translateX(-50%);background:#35432f;color:#fff8e6;display:flex;align-items:center;gap:16px;padding:10px 14px;box-shadow:3px 3px 0 #53452f38}.switcher button{background:#657342;color:#fff8e6;font-size:19px;border:0;width:40px;height:36px}.switcher span{min-width:265px;text-align:center;font-size:15px}.status{font-size:12px;margin-top:15px;color:#6b7259}.selection{font-size:12px;line-height:20px;margin-top:10px;color:#657342;min-height:20px}::-webkit-scrollbar{width:9px}::-webkit-scrollbar-track{background:#ded0ac}::-webkit-scrollbar-thumb{background:#846848}
</style>
<div class="page"><div class="badge">可丢弃原型 · 未接入游戏</div><h1>#149 · 用地图中的建筑和道路辨认建造项</h1><div class="subtitle" id="subtitle"></div>
<div class="main"><section><div class="section-title">建造目录 <span class="muted">790 × 620 · 原窗口大小 · 三列 · 可切分类并滚动</span></div><div class="window"><header class="header"><small>FARM EXCHANGE</small><h2>建造目录</h2><button class="close" title="原型：关闭按钮不关闭窗口">×</button></header><div class="body"><div class="tabs"><button data-cat="all">全部</button><button data-cat="farm">农田</button><button data-cat="processor">加工场地</button><button data-cat="road">道路</button><input placeholder="搜索设施名称" id="search"></div><div class="scroll"><div class="grid" id="catalog"></div></div><p class="hint">农田与加工场地 10.00 / 座 · 道路 1.00 / 格<br>设施点击建造；道路短按起点、移动、短按终点；拆路选择直线范围。</p></div></div><div class="selection" id="selection">仅预览：点击卡片查看名称，不执行建造。</div><div class="notes"><b>建议展示区：145 × 108 px</b><br>沿用原卡片 220 × 200 px 的最低尺寸，为图和三行文字各保留空间。<br>加工图按非透明内容取图、等比缩放、居中底对齐，最近邻采样。<br>原 PNG、透明画布及地图 pivot 均保留；只改变 UI 如何取图。<br><b id="road-note"></b></div><div class="status">农田示意与选种图保持现状；详情窗是否同步使用正式图，待确认。</div></section>
<section><div class="section-title">七种加工建筑 <span class="muted" id="building-label"></span></div><div class="collection" id="buildings"></div><div class="road-section"><div class="section-title">五种道路与拆路工具 <span class="muted">相同方向与尺寸，直接比较材质</span></div><div class="road-collection" id="roads"></div></div><div class="notes"><span class="label" id="variant-label"></span><br><span id="variant-description"></span><br>拆路使用道路＋红色拆除角标，名称和“不退款”文字保留。<br>目录完整 14 项可通过左侧分类、滚动与搜索查看；右侧集中展示所有新图。</div></section></div></div>
<div class="switcher"><button id="prev" title="上一方案">←</button><span id="current"></span><button id="next" title="下一方案">→</button></div>
<script>
const items=__ITEMS__;
const variants={A:{name:'正式建筑＋三格短直道路（建议）',description:'道路端点与中间连接来自正式图集，五材质用完全相同的三格右下短直路段。更容易识别“铺路”，也能看清材质。',road:'A：三格短直道路仅用于示意，每基础格费用仍为 1.00 金币。'},B:{name:'正式建筑＋单格道路',description:'道路使用正式孤立 tile（mask = 0），最直接对应 1×1 占地；在目录中更像材质样片。建筑与 A 完全一致。',road:'B：五材质各展示一格，与卡片 1×1 标注直接对应。'},C:{name:'当前 SVG 示意对照',description:'现有图源：磨坊一张 SVG，其他六种加工场地共用 workshop.svg，五道路与拆路共用 road.svg。保留同一展示区便于比较辨识度。',road:'C：当前图源对照；现有游戏尚未使用 A 或 B 的正式图。'}};
const params=new URLSearchParams(location.search);let variant=variants[params.get('variant')]?params.get('variant'):'A';let category=params.get('category')||'processor';
function art(item){return `<div class="art ${variant==='C'||item.category==='farm'?'svg':''}"><img src="${item[variant]}" alt="${item.name}">${item.removal&&variant!=='C'?'<span class="removal-mark" title="拆除"><svg viewBox="0 0 24 24"><path d="m3 21 9-9m-3-3 6 6M11 3l10 10-4 4L7 7z"/></svg></span>':''}</div>`}
function caption(item){return `<div class="caption">${item.name} · ${item.size}<br>${item.detail}<br><span class="fee">${item.fee}</span></div>`}
function renderCatalog(){const query=document.querySelector('#search').value;document.querySelector('#catalog').innerHTML=items.filter(i=>(category==='all'||i.category===category)&&i.name.includes(query)).map(i=>`<button class="card" data-name="${i.name}">${art(i)}${caption(i)}</button>`).join('');document.querySelectorAll('.tabs button').forEach(b=>b.classList.toggle('active',b.dataset.cat===category));document.querySelectorAll('.card').forEach(b=>b.onclick=()=>document.querySelector('#selection').textContent='已预览：'+b.dataset.name+'。原型未提交游戏指令。');}
function render(){let v=variants[variant];document.querySelector('#subtitle').textContent='1920 × 1080 / UI 1×　·　'+v.name;document.querySelector('#building-label').textContent=variant==='C'?'当前 UI SVG 图源':'正式 q0 PNG · 每张均使用自己的建筑图';document.querySelector('#buildings').innerHTML=items.filter(i=>i.category==='processor').map(i=>`<div class="sample">${art(i)}<div class="caption">${i.name}<br><span class="fee">${i.detail}</span></div></div>`).join('')+'<div class="sample">原图 256 × 240<br>非透明内容取图<br>统一 145 × 108 展示区<br>保持比例，底部对齐</div>';document.querySelector('#roads').innerHTML=items.filter(i=>i.category==='road').map(i=>`<div class="sample">${art(i)}<div class="caption">${i.name}<br><span class="fee">${i.removal?'不退款':'1.00 金币 / 格'}</span></div></div>`).join('');document.querySelector('#current').textContent=variant+' — '+v.name;document.querySelector('#variant-label').textContent='方案 '+variant;document.querySelector('#variant-description').textContent=v.description;document.querySelector('#road-note').textContent=v.road;if(variant==='C'){document.querySelector('.sample:nth-child(8)').innerHTML='当前加工图源<br>磨坊独立一图<br>其余六种共用<br>workshop.svg';}renderCatalog();}
function cycle(delta){if(params.get('compare')==='AB')return;const keys=Object.keys(variants);variant=keys[(keys.indexOf(variant)+delta+keys.length)%keys.length];params.set('variant',variant);history.replaceState({},'',location.pathname+'?'+params);render();}
document.querySelector('#prev').onclick=()=>cycle(-1);document.querySelector('#next').onclick=()=>cycle(1);document.addEventListener('keydown',e=>{if(e.target.matches('input,textarea,[contenteditable]'))return;if(e.key==='ArrowLeft')cycle(-1);if(e.key==='ArrowRight')cycle(1)});document.querySelectorAll('[data-cat]').forEach(b=>b.onclick=()=>{category=b.dataset.cat;params.set('category',category);history.replaceState({},'',location.pathname+'?'+params);document.querySelector('.scroll').scrollTop=0;renderCatalog()});document.querySelector('#search').oninput=renderCatalog;render();
if(params.get('compare')==='AB'){const original=variant;variant='B';const row=items.filter(i=>i.category==='road').map(i=>`<div class="sample">${art(i)}<div class="caption">${i.name}</div></div>`).join('');variant=original;document.querySelector('.switcher').innerHTML='<span>A / B 同屏比较 · 原型未接入游戏</span>';document.querySelector('.main>section:nth-child(2)>.notes').outerHTML=`<div class="road-section"><div class="section-title">B 对照：单格道路 <span class="muted">上方为 A 三格短路段，下方为 B 单格；建筑相同</span></div><div class="road-collection">${row}</div></div>`;}
</script></html>'''
(OUT/'thumbnail-prototype.html').write_text(html.replace('__ITEMS__',json.dumps(items,ensure_ascii=False)),encoding='utf-8')
(OUT/'source-metrics.json').write_text(json.dumps(metrics,ensure_ascii=False,indent=2),encoding='utf-8')
(OUT/'README.md').write_text('''# #149 建造目录缩略图可丢弃原型

状态：仅预览，未修改生产代码；待用户确认 A / B、详情窗是否同步后再实施。

运行：直接打开 `thumbnail-prototype.html`；重建：`python build/issue149-prototype/make_prototype.py`。

同页以 `?variant=A`、`?variant=B`、`?variant=C` 切换；底部箭头及键盘左右键也可切换。分类支持 `category=all/farm/processor/road`。全部 14 项通过分类、滚动和搜索查看。卡片点击仅回显名称。

- A：正式七加工 q0 PNG＋三格右下短直道路，推荐。
- B：同一加工图＋单格孤立道路。
- C：当前 SVG 图源对照，不代表当前 Godot 排版精确截图。
- 790×620 原目录大小，最低 220×200 卡片，三列；建议展示区 145×108，PNG 非透明内容等比居中底对齐、最近邻，源 PNG 不改动。
- 七建筑源 `assets/gameplay/buildings/{crop}_workshop_q0.png`，原画布均 256×240，裁区见 `source-metrics.json`。实际磨坊、制糖坊动画使用拆分层，但静态 q0 完整图仍用于地图候选，适合 UI 缩略图。
- 五道路源 `assets/gameplay/terrain/roads/road_{material}_atlas.png`，按同目录 `atlas_regions.json` 的清单定位区域，variant 0。单格 mask 0；短路段 mask 2 / 10 / 8，依次偏移 (0,0)/(32,16)/(64,32)，完整组合为 128×64，再按非透明内容取图。
- 当前对照来自 `assets/ui/facilities/mill.svg`、`workshop.svg`、`road.svg`；农田沿用 `field_wheat.svg`。拆路角标为原型内简单工具图示，需用户确认后在现有 UI 图示体系中实现。
- 图源和主题来自实际项目资源、`BuildCatalogWindow.cs` 与 `UiElements.cs`。费用：设施每座 10.00，道路每格 1.00；拆除不退款。

确认事项：选择短路段或单格道路、认可建筑裁区及缩略比例、是否将加工/道路详情同步到正式图。农田与选种图不在本次替换范围。
''',encoding='utf-8')
print(OUT/'thumbnail-prototype.html')
