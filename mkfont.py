# 글꼴에서 KS X 1001 한글 + 라틴 + 기호만 남긴다 (OFL 글꼴)
import os
from fontTools import subset
from fontTools.ttLib import TTFont
H = os.path.dirname(os.path.abspath(__file__)); F = os.path.join(H, 'fonts')  # pretendard.ttf(PretendardVariable), wanted.ttf(WantedSansVariable)를 여기에
ks = set()
for a in range(0xB0, 0xC9):
    for b in range(0xA1, 0xFF):
        try: ks.add(ord(bytes([a, b]).decode('euc-kr')))
        except UnicodeDecodeError: pass
# 실제 이름에 자주 나오는 나머지 음절도 쓸 수 있게 11172자 중 흔한 확장은 시스템 글꼴이 채운다
u = (ks - {0x3164}) | set(range(0x20, 0x17F)) | set(range(0x3131, 0x3164)) | set(map(ord, '–—‘’“”•…→←↑↓↕·×−±≤≥％℃℉ⓘ◀▶▲▼■□●○'))
def make(src, out, fam, lo, hi):
    f = TTFont(os.path.join(F, src))
    o = subset.Options(); o.layout_features = ['kern', 'liga', 'calt', 'ccmp', 'mark', 'mkmk', 'tnum']; o.name_IDs = ['*']; o.hinting = False; o.notdef_outline = True
    s = subset.Subsetter(o); s.populate(unicodes=u); s.subset(f)
    ps = fam.replace(' ', '')
    for n in f['name'].names:  # 예약 이름(Pretendard, Wanted Sans)은 화면에 보이는 이름에 쓰지 않는다
        if n.nameID in (1, 4, 16): n.string = fam
        elif n.nameID == 3: n.string = 'LagWatch;' + ps
        elif n.nameID == 6: n.string = ps + '-Regular'
        elif n.nameID == 25 or n.nameID >= 256: n.string = str(n).replace('PretendardVariable', ps).replace('WantedSansVariable', ps)
    f.save(os.path.join(H, 'res', out)); print(out, os.path.getsize(os.path.join(H, 'res', out)))
os.makedirs(os.path.join(H, 'res'), exist_ok=True)
make('pretendard.ttf', 'a.ttf', 'LW Sans', 400, 900)
make('wanted.ttf', 'b.ttf', 'LW Wide', 400, 900)
