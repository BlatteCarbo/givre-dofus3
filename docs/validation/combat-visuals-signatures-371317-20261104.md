# Combat visuals static signature audit: 3.7.13.17 (2026-11-04)

## Result and boundary

The installed binary does not satisfy the existing 3.7.12.12 native snapshot. Of **762 signature entries, 10 match and 752 mismatch** at their pinned RVA, comparing all 16 bytes exactly. There are **761 distinct RVAs: 10 match and 751 mismatch**. The table duplicates RVA 0x55E08E0; both copies mismatch. All 762 entries map to 16 file-backed PE bytes; none is unmapped or short.

This is not evidence that the classes were renamed. Actual v39 metadata retains the sampled obfuscated classes, their relevant method names/argument counts, and bridge fields. Address/byte guards need a new snapshot, but correct managed identity, native ABI, object layouts, call-site semantics, and ownership still need proof. This audit neither proposes enabling hooks nor promotes a candidate address.

Only this report was created. No source pins, installed files, binaries, runtime state, or configuration were changed; no installs, git commands, injection, client/Givre launches, or desktop operations were used. Binary input was read as data in a persistent Node.js kernel; source text was inspected with read. Existing local Il2CppInspectorRedux metadata layout sources were reused as reference, without building or installing them.

## Inputs and gate

Worktree: `%USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024`

Client: `%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3`

| Input | Bytes | Observed SHA256 |
|---|---:|---|
| GameAssembly.dll | 125827584 | ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591 |
| Dofus_Data/il2cpp_data/Metadata/global-metadata.dat | 43768684 | ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af |

Both observed hashes equal the task's expected installed inputs. The release label 3.7.13.17 is supplied by the task; this audit identifies content by hashes, not that label.

bridge.cpp:290 calls SupportedClient before api.Load, SelfTest, feature installation, and Compositor.Update hook creation. The error text names 3.7.12.12; the actual predicate is in vendor/dofus-enhanced/src/core/common.cpp, using core/common.hpp constants: executable basename Dofus.exe, GameAssembly SHA256 70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663 and metadata SHA256 ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6. Both content pins differ. Changing the label or hashes alone cannot satisfy the address and byte guards.

All source paths below are relative to native/GivreCombatVisuals; vendored paths start at vendor/dofus-enhanced/src.

## Exact comparison and relocation candidates

PE mapping is section.PointerToRawData + RVA - section.VirtualAddress, requiring the complete 16-byte span inside SizeOfRawData. No LoadLibrary or exported function was called. Executable section raw spans used for supplementary pattern searches are .text (RVA 4096, raw 1024, size 6655488) and il2cpp (RVA 6660096, raw 6656512, size 86839808). For the samples in il2cpp the raw offset is RVA minus 0xE00; .text uses RVA minus 0xC00.

| Pinned RVA | File offset | Expected 16 bytes | Installed 16 bytes |
|---|---|---|---|
| 0x15E8650 | 0x15E7850 | 488b8140030000c3cccccccccccccccc | 4881c4c00000005fc3e8c235fbfecccc |
| 0x55E5710 | 0x55E4910 | 4d85c00f849000000048895c24084889 | 10070f1008660f73d808660f73d90866 |
| 0x20F9080 | 0x20F8280 | 40574883ec20803d5a81040500488bf9 | fecce8892b4afecccccccccccccccccc |
| 0x59D540 | 0x59C940 | 48895c24084889742420555741544156 | b9064c8d05e7c8e105488d1520cae105 |
| 0x59E63E | 0x59DA3E | 833d7392bc0600752348891d6292bc06 | 0841c1ee1d4180e601b201e882570400 |
| 0x1952710 | 0x1951910 | 48895c24084889742418574883ec2080 | 742e4533c08bd7e8c4c00100b001488b |

All matching RVAs: 0x68D2F0, 0x68D5C0, 0x68D2B0, 0x68D2C0, 0x682030, 0x68D2D0, 0x69A450, 0x681FE0, 0x681F30, 0x68A590. A match establishes only those 16 bytes at that address, not managed ownership or whole-function equivalence.

An exact 16-byte search across the two executable raw spans gives **629 distinct byte patterns** across 762 entries. Counted per original table entry, **320 have no hit, 154 have exactly one hit, 288 have multiple hits**. Among the 752 entries mismatching their old address, those counts are respectively **320, 152, 280**. These are byte-location candidates, not approved patches. For example old 0x15E8650 has one hit at 0x15E20F0, and old 0x55E5710 one at 0x55AAA50. Compositor.Update's old 0x20F9080 pattern has zero hits. EntityInfo.balc's old 0x1952710 pattern has 311 hits, so its common prologue cannot identify a replacement. Zero hits can result from changed relative displacements or generated code; they do not show method removal.

## Actual metadata corroboration

Metadata magic is 0xFAB11BAF, version **39**. Its section header uses offset/byte-size/count triples at byte 8. Observed tables:

| Section index | Table | Offset | Bytes | Count | Record bytes |
|---:|---|---:|---:|---:|---:|
| 2 | strings | 2111524 | 5085064 | 348238 | variable |
| 5 | methods | 8739072 | 12562016 | 392563 | 32 |
| 11 | fields | 30085240 | 2474640 | 206220 | 12 |
| 19 | type definitions | 35469148 | 3646130 | 44465 | 82 |
| 20 | images | 39115280 | 5436 | 151 | 36 |

The existing layout definitions are under %USERPROFILE%/Downloads/billbot-study-st01a19bff/DBDofusUnity/Il2CppInspectorRedux/Il2CppInspector.Common/Next/Metadata (Il2CppGlobalMetadataHeader.cs, Il2CppSectionMetadata.cs, Il2CppTypeDefinition.cs, Il2CppMethodDefinition.cs, Il2CppFieldDefinition.cs, Il2CppImageDefinition.cs, IIndexType.cs). This file's packed type record has field start at +26, method start at +30, method count at +58, field count at +62; method declaring type is a 16-bit index at +4, argument count at +30. All sampled methods' declaring indices were checked against their containing type. All 44465 type names resolved inside the string section. Image records place Core.dll at type range [0,9425) and Ankama.Dofus.Core.Characters.dll at [38878,39009).

| Actual type index | Image / type | Confirmed direct membership |
|---:|---|---|
| 9359 | Core.dll / Core.Engine.Compositor.Compositor | Update/0; fields eavv and isRunning |
| 7547 | Core.dll / exf | baza/1, bazg/2; fields drih and drii |
| 8269 | Core.dll / fsl | baza/1, bazg/2 |
| 8421 | Core.dll / fxc | type exists (27 direct fields) |
| 7363 | Core.dll / esg | zcx/0 |
| 7504 | Core.dll / evv | type exists (85 direct methods) |
| 8294 | Core.dll / fti | bfmh/0 |
| 305 | Core.dll / lg | oab/0; field ddku |
| 7501 | Core.dll / Core.Services.Entities.EntityInformationServices.EntityInfo | bajh/0, baks/2, balc/2 |
| 997 | Core.dll / Core.Rendering.Entity.CoreAnimatedEntity.AnimatedEntity | rez/0, rfa/0, ric/0; field dhhm |
| 38936 | Ankama.Dofus.Core.Characters.dll / CoreAnimated.Animated | field fhtc |
| 38959 | Ankama.Dofus.Core.Characters.dll / Core.Rendering.Look.EntityLook | dbfj/0, dbev/1, dbex/1 |

These are metadata identity observations, not native entrypoint recovery. Same names and arity do not prove return/parameter types, interface implementation, registry ownership, flag meanings, field offsets, or behavior. No code-registration-to-method mapping was reconstructed. No class rename is claimed.

## Version dependencies beyond the 16-byte table

- **Method addresses and ABI:** core/runtime.cpp Native requires the first pointer in MethodInfo to equal gameBase + expectedRva; FindVerified additionally matches name/arity and that address. NativeAt then checks the table entry. A new byte pattern alone does not satisfy those contracts. bridge.cpp:301 separately pins Compositor.Update at 0x20F9080. Direct function typedefs in animations/movement.cpp preserve an explicit eight-byte EntityMovementParam union and native float/argument placement; signatures do not validate that ABI. core/runtime.hpp assumes a MethodInfo entrypoint at offset zero, pointer-sized GC handles, native boxing, and x64 layout.
- **Initialization data RVAs:** core/runtime.cpp:190-202 WaitForInitialization guards code at 0x59D540 and 0x59E63E, then polls LONGs at gameBase+0x71678C0 and +0x7167870. Those data addresses are not signature entries and cannot be recovered just by changing a code prologue. The audited bridge's DNM_Load does not call WaitForInitialization; the helper is nevertheless snapshot-dependent.
- **Return-address/call-site provenance:** animations/combat-cleanup.cpp uses 0x26CFAA9 (call at 0x26CFAA4); instant-waits.cpp uses 0x1E59C24 (call at 0x1E59C1F). movement-waits.cpp uses 0x26B9E07, 0x26B9E1F, 0x205FD43; movement.cpp uses 0x263F03C and 0x263FB17. script-waits.cpp uses 0x2740093, 0x27401A0, 0x274164F. These need the intended caller and exact instruction boundary, not a matching callee. appearance/features.cpp:235 accepts exact caller locations 0x2617618 and 0x2619FDB, also present as guarded instruction-site signatures.
- **Raw instance fields:** bridge.cpp:244 reads fight+0xB0, and :251 source+0x48. appearance/appearance-refresh.cpp:26-42 traverses fight+0x18, context+0x58/+0x68, rendered+0x150/+0x20, creature+0x10 and entity+0x1A8/+0x398/+0x1C8/+0x170/+0xA8/+0x1E0. A successful prologue comparison says nothing about these layouts.
- **Checked fields are still version contracts:** appearance/features.cpp:125-132 explicitly requires lg.ddku=0x20, AnimatedEntity.dhhm=0x310 and CoreAnimated.Animated.fhtc=0x1C8. Metadata above confirms names, not these numerical offsets. core/runtime.cpp FieldAddress uses field_get_offset for name-resolved fields, whereas core/runtime.hpp Read<T> directly dereferences the supplied byte offset.
- **Other raw layouts:** animations/animations.cpp reads step+0x78/+0x1D and writes +0x71; instant-waits.cpp reads waiter+0x40 and writes +0x48. script-waits.cpp relies on +0x30/+0x38/+0x40 and fight state+0xE0. Movement implementations use behavior dictionaries and inline states at different offsets: movement.cpp behavior+0x38/+0x48, state+0x20/+0x30/+0x38, entity+0xB8/+0x1C8; push-movement.cpp state+0x28/+0x34/+0x38/+0x40/+0x48 and entity+0x348; jump-movement.cpp state+0x20/+0x28/+0x38/+0x40/+0x48; throw-movement.cpp state+0x10/+0x18/+0x30/+0x38/+0x40/+0x48. appearance/size-measurements.cpp uses look+0x1C/+0x30, array data+0x20 and animation height+0x34. Numeric behavior kinds, property keys, and flags must retain their meanings as well as offsets.
- **Managed names and services:** bridge discovery requires fsl to resolve a service whose concrete class is exf, whose drih field has concrete class fxc; it traverses eavv/drii and calls get_dofusOptionsContainer. runtime SelfTest explicitly expects Core.dll/exf and reflection name exf. Present metadata names support further static mapping, but do not establish assignability or live service graph validity. api.Load resolves IL2CPP exports by name rather than pinned RVA; export ABI compatibility is a separate question not exercised here.

The address-sized constants and stored 16-byte arrays are mechanically replaceable once each target is identified. This audit provides 152 moved unique-pattern candidates, not 152 verified method mappings. Remaining identity proof must connect current metadata owner/name/full signature and native registration to the right body, check call sites/control flow and runtime field offset tables, and preserve the feature's ownership/flag semantics. No unresolved identity is converted into a patch recommendation.

## Reproduction

The following read-only JavaScript is the full calculation used in the audit (the session used equivalent bounded read calls for source text). It reads installed files as data, prints hashes, mapped comparisons, relocation counts and metadata memberships, and writes nothing. Paste into Node's module input or execute as an equivalent ephemeral evaluation; do not save it into the worktree as an additional deliverable.

```js
// Run with Node.js 24 from the worktree root: node --input-type=module
import fs from 'node:fs';
import { createHash } from 'node:crypto';
const client = '%USERPROFILE%/AppData/Local/Ankama/Dofus-dofus3';
const src = fs.readFileSync('native/GivreCombatVisuals/vendor/dofus-enhanced/src/core/signatures.hpp', 'utf8');
const pe = fs.readFileSync(client + '/GameAssembly.dll');
const md = fs.readFileSync(client + '/Dofus_Data/il2cpp_data/Metadata/global-metadata.dat');
const hash = b => createHash('sha256').update(b).digest('hex');
console.log('SHA256', hash(pe), hash(md));
const signatures = [...src.matchAll(/\{(0x[\dA-Fa-f]+),\s*\{([^}]+)\}\}/g)].map(m => ({
  rva: Number(m[1]), bytes: Buffer.from(m[2].split(',').map(Number))
}));
const nt = pe.readUInt32LE(0x3c);
if (pe.readUInt32LE(nt) !== 0x4550 || pe.readUInt16LE(nt + 24) !== 0x20b) throw Error('Not PE32+');
const st = nt + 24 + pe.readUInt16LE(nt + 20);
const sections = Array.from({length: pe.readUInt16LE(nt + 6)}, (_, i) => {
  const o = st + 40 * i;
  return { rva: pe.readUInt32LE(o+12), size: pe.readUInt32LE(o+16),
    raw: pe.readUInt32LE(o+20), flags: pe.readUInt32LE(o+36) };
});
const map = r => {
  const s = sections.find(s => r >= s.rva && r + 16 <= s.rva + s.size);
  if (!s) throw Error('RVA outside file-backed section: ' + r.toString(16));
  return s.raw + r - s.rva;
};
let match = 0, zero = 0, one = 0, multiple = 0;
const matchRvas = [], mismatches = new Set(), cache = new Map();
for (const [i, s] of signatures.entries()) {
  if (s.bytes.length !== 16) throw Error('Wrong signature length');
  const offset = map(s.rva), actual = pe.subarray(offset, offset + 16);
  const equal = actual.equals(s.bytes);
  if (equal) { match++; matchRvas.push(s.rva.toString(16)); } else mismatches.add(s.rva);
  const key = s.bytes.toString('hex');
  if (!cache.has(key)) {
    const hits = [];
    for (const section of sections.filter(s => s.flags & 0x20000000)) {
      const b = pe.subarray(section.raw, section.raw + section.size);
      for (let p = b.indexOf(s.bytes); p >= 0; p = b.indexOf(s.bytes, p + 1)) hits.push(section.rva + p);
    }
    cache.set(key, hits);
  }
  const hits = cache.get(key);
  if (!hits.length) zero++; else if (hits.length === 1) one++; else multiple++;
  if ([0x15e8650, 0x55e5710, 0x20f9080, 0x59d540, 0x59e63e, 0x1952710].includes(s.rva))
    console.log({entry: i+1, rva: s.rva.toString(16), fileOffset: offset.toString(16),
      expected: key, actual: actual.toString('hex'), hitCount: hits.length,
      soleHit: hits.length === 1 ? hits[0].toString(16) : undefined});
}
console.log({entries: signatures.length, uniqueRvas: new Set(signatures.map(s=>s.rva)).size,
  match, mismatch: signatures.length-match, uniqueMismatch: mismatches.size,
  distinctPatterns: cache.size, zero, one, multiple, matchRvas});
// Actual v39 metadata: section triples are offset, byte size, count.
if (md.readUInt32LE(0) !== 0xfab11baf || md.readUInt32LE(4) !== 39) throw Error('Not metadata v39');
const section = i => ({offset: md.readUInt32LE(8+i*12), size: md.readUInt32LE(12+i*12), count: md.readUInt32LE(16+i*12)});
const strings = section(2), methods = section(5), fields = section(11), types = section(19), images = section(20);
if (types.size !== types.count*82 || methods.size !== methods.count*32 || fields.size !== fields.count*12 || images.size !== images.count*36) throw Error('Unexpected packed table layout');
const str = i => { if (i >= strings.size) throw Error('Invalid string index'); const p = strings.offset+i; return md.toString('utf8',p,md.indexOf(0,p)); };
const wanted = new Set(['lg','AnimatedEntity','Animated','esg','EntityInfo','evv','exf','fsl','fti','fxc','Compositor','EntityLook']);
for (let i=0; i<types.count; i++) {
  const o=types.offset+i*82, name=str(md.readUInt32LE(o)), ns=str(md.readUInt32LE(o+4));
  if (!wanted.has(name)) continue;
  const start=md.readUInt32LE(o+30), count=md.readUInt16LE(o+58);
  const ownedMethods=Array.from({length:count},(_,j)=>{
    const m=methods.offset+(start+j)*32;
    if(md.readUInt16LE(m+4)!==i) throw Error('Method owner mismatch');
    return str(md.readUInt32LE(m))+'/'+md.readUInt16LE(m+30);
  });
  const fstart=md.readUInt32LE(o+26), fcount=md.readUInt16LE(o+62);
  const ownedFields=Array.from({length:fcount},(_,j)=>str(md.readUInt32LE(fields.offset+(fstart+j)*12)));
  console.log({typeIndex:i,name,ns,ownedMethods,ownedFields});
}
```

## Verification performed

Observed hashes matched both supplied hashes. Exactly 762 parsed entries each had 16 expected and 16 mapped actual bytes; duplicate accounting explains the 761-RVA count. Full executable-span pattern searches were completed, not sampled. Metadata table-size/count equations and sampled method-owner indices were checked. This documentation-only audit did not build or run application tests and does not claim runtime compatibility. No runtime adaptation, pin change, or promotion was performed.

