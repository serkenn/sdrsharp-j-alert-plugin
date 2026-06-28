import struct, sys, os
path = sys.argv[1]; outdir = sys.argv[2]
data = open(path,'rb').read()
sig = bytes([0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32])
i = data.find(sig)
if i < 0: print("bundle signature not found"); sys.exit(1)
header_offset = struct.unpack_from('<q', data, i-8)[0]
p = header_offset
major, minor = struct.unpack_from('<II', data, p); p+=8
count = struct.unpack_from('<i', data, p)[0]; p+=4
# bundle id (7-bit length-prefixed string)
def read_str(buf, p):
    res=0; shift=0
    while True:
        b=buf[p]; p+=1
        res |= (b & 0x7f)<<shift
        if not (b & 0x80): break
        shift+=7
    s=buf[p:p+res].decode('utf-8'); p+=res
    return s,p
bundle_id, p = read_str(data, p)
# v2+ has extra header fields (deps.json/runtimeconfig offsets+sizes, flags)
if major >= 2:
    p += 8+8+8+8+8  # depsOffset,depsSize,runtimeOffset,runtimeSize,flags (each 8 bytes)
print(f"version={major}.{minor} count={count} id={bundle_id}")
os.makedirs(outdir, exist_ok=True)
for n in range(count):
    offset, size, compressedSize = struct.unpack_from('<qqq', data, p); p+=24
    ftype = data[p]; p+=1
    rel, p = read_str(data, p)
    raw = data[offset:offset+size] if compressedSize==0 else None
    if compressedSize!=0:
        import zlib
        comp = data[offset:offset+compressedSize]
        try: raw = zlib.decompress(comp, -15)
        except Exception as e: raw=None; print("  decomp fail",rel,e)
    if rel.endswith('.dll') and raw:
        name=os.path.basename(rel)
        open(os.path.join(outdir,name),'wb').write(raw)
print("extracted dlls:", len([f for f in os.listdir(outdir) if f.endswith('.dll')]))
