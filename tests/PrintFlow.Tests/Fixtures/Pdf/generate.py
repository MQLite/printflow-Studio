"""Synthetic PDF fixtures only. Regenerate with pypdf 6.10.0 (QA tooling, not a Product dependency).
Windows.Data.Pdf is the Product parser/renderer. These files exercise real structural forms.
"""
from pathlib import Path
import io, struct, zlib
from pypdf import PdfWriter, PdfReader
from pypdf.generic import DecodedStreamObject, NameObject, RectangleObject, ArrayObject, ByteStringObject
root=Path(__file__).parent
for name,n,rotation in [('single',1,0),('rotated',1,90),('two',2,0),('zero',0,0),('encrypted',1,0),('crop',1,0),('opaque',1,0),('fractional',1,0)]:
 w=PdfWriter()
 for i in range(n):
  p=w.add_blank_page(width=144.12 if name=='fractional' else 144,height=72)
  s=DecodedStreamObject();s.set_data(b'1 0 0 rg 0 0 144 72 re f\n' if name=='opaque' else b'1 0 0 rg 10 10 40 30 re f\n')
  p[NameObject('/Contents')]=w._add_object(s)
  if rotation:p.rotate(rotation)
  if name=='crop':p.cropbox=RectangleObject([18,9,126,63])
 if name=='encrypted':
  w._ID=ArrayObject([ByteStringObject(b'0123456789abcdef')]*2)
  w.encrypt('synthetic-fixture-only',algorithm='RC4-128')
 w.write(root/(name+'.pdf'))
(root/'corrupt.pdf').write_bytes(b'%PDF-1.7\nnot a document\n')
w=PdfWriter(root/'single.pdf',incremental=True);w.add_blank_page(width=144,height=72);w.write(root/'incremental-two.pdf')
# Compressed catalog and indirect nested page tree, with xref stream. Page dictionaries
# never occur as cleartext /Type /Page; content contains a decoy string a text scan would count.
objects=[b'<< /Type /Catalog /Pages 2 0 R >>',b'<< /Type /Pages /Kids [3 0 R] /Count 2 >>',
 b'<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 /MediaBox [0 0 144 72] >>',
 b'<< /Type /Page /Parent 3 0 R /Resources << >> /Contents 6 0 R >>',b'<< /Type /Page /Parent 3 0 R /Resources << >> /Contents 6 0 R >>']
body=b'';header=b''
for i,o in enumerate(objects,1):header+=f'{i} {len(body)} '.encode();body+=o+b'\n'
compressed=zlib.compress(header+body)
output=bytearray(b'%PDF-1.7\n%\xe2\xe3\xcf\xd3\n');offsets={}
def obj(n,data):
 offsets[n]=len(output);output.extend(f'{n} 0 obj\n'.encode()+data+b'\nendobj\n')
content=b'1 0 0 rg 10 10 40 30 re f\n% /Type /Page decoy\n'
obj(6,f'<< /Length {len(content)} >>\nstream\n'.encode()+content+b'endstream')
obj(7,f'<< /Type /ObjStm /N 5 /First {len(header)} /Length {len(compressed)} /Filter /FlateDecode >>\nstream\n'.encode()+compressed+b'\nendstream')
offsets[8]=len(output)
entries=[(0,0,65535)]+[(2,7,i) for i in range(5)]+[(1,offsets[i],0) for i in [6,7,8]]
xref=zlib.compress(b''.join(struct.pack('>BIH',*v) for v in entries))
obj(8,f'<< /Type /XRef /Size 9 /Root 1 0 R /W [1 4 2] /Length {len(xref)} /Filter /FlateDecode >>\nstream\n'.encode()+xref+b'\nendstream')
output.extend(f'startxref\n{offsets[8]}\n%%EOF\n'.encode());(root/'compressed-two.pdf').write_bytes(output)
for name in ['two','compressed-two','incremental-two']:assert len(PdfReader(root/(name+'.pdf')).pages)==2
