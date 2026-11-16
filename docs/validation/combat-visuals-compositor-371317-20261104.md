# Current Compositor.Update mapping

Read-only follow-up to `combat-visuals-signatures-371317-20261104.md`.
Same installed GameAssembly SHA256 ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591
and metadata SHA256 ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af.
No hook, binary promotion or compatibility change performed.

The old Update prologue had zero exact matches because its RIP-relative
displacement changed. Method registration provides stronger identity evidence
than replacing a prologue search with a wildcard.

- Metadata v39 type9359 is Core.Engine.Compositor.Compositor in Core.dll.
- Packed type record gives methodStart82763 and methodCount19.
- Method row82780 names Update, has zero parameters and token0x0601435c.
- Token is at byte+20 in the32-byte method record. An initial exploratory
  read at+22 was incorrect and failed PE mapping; that result is discarded.
- Exact NUL-delimited Core.dll string VA0x185ba2cc8 has one aligned pointer
  reference in the searched .rdata/.data sections,at0x185ba2c40.
- Its first three registration words are name0x185ba2cc8,count0x1ba63,
  methodPointers0x1868ead30.
- Slot `(token & 0xffffff)-1` is82779,within count113251.
- The pointer in that slot is0x1820cc7f0,relative to image base0x180000000:
  **current candidate RVA0x20cc7f0**.
- First16bytes: `40574883ec20803d51d1030500488bf9`.
- Old pinned RVA0x20f9080/bytes
  `40574883ec20803d5a81040500488bf9` differ.

This connects metadata owner/name/token to a concrete module table entry.
It does not prove the complete native function semantics, service fields,
return-address guards, field layouts, all other hooks or live ABI. The candidate
is evidence for a future versioned port,not permission to change the global
fingerprint gate or the shipped Update address in isolation.

## Additional animation entry mappings

The same Core.dll registration and token-slot calculation gives one direct
name/parameter-count match per row below. The search is limited to the confirmed
Core.dll type range[0,9425). These observations still do not verify parameter
types, native calling convention or caller semantics.

| Type / method | Args | Type index | Method row | Token | Old RVA | Current RVA | Current first16bytes |
| --- | ---: | ---: | ---: | --- | --- | --- | --- |
| Core.Rendering.Entity.CoreAnimatedEntity.AnimatedEntity.rfw | 5 | 997 | 10105 | 0x0600277a | 0x2616140 | 0x25cd5e0 | 48895c24185556574883ec60803d523c |
| Core.Rendering.Entity.CoreAnimatedEntity.AnimatedEntity.rfv | 3 | 997 | 10104 | 0x06002779 | 0x2616050 | 0x25cd4f0 | 48895c24084889742410574883ec4080 |
| gpy..ctor | 10 | 9014 | 81025 | 0x06013c82 | 0x2081890 | 0x2056210 | 48896c241856415641574883ec20803d |
| gqg..ctor | 1 | 9028 | 81122 | 0x06013ce3 | 0x208a1a0 | 0x205e190 | 48895c2408574883ec208bda488bf933 |

Old addresses are read from movement-dispatch.cpp,movement.cpp,
combat-cleanup.cpp and instant-waits.cpp respectively. No uniform relocation
delta describes these entries; each needs its own identity and ABI validation.

## Current field-layout and return-type checks

The resident metadata-registration candidate has typesCount155112 and
typesPointer0x18608c8e0; fieldOffsetsCount44465 and
fieldOffsetsPointer0x186a3b570. Resolve a type's field table through
`fieldOffsetsPointer + typeIndex*8`,then use its declared field ordinal and
four-byte signed entries. Current observed offsets:

| Owner | Field | Offset | IL2CPP kind |
| --- | --- | --- | --- |
| Compositor(type9359) | eavv | 0x28 | not decoded here |
| Compositor(type9359) | eavy | 0x40 | not decoded here |
| Compositor(type9359) | isRunning | 0x48 | Boolean(0x02) |
| fxc(type8421) | dwan | 0xb0 | Boolean(0x02) |

The current Update body at0x20cc821 tests `[rdi+0x48]`,then loads the
update collection from+0x40. This independently corroborates the compositor
running offset. The fxc offset corroborates the bridge's raw read location,
but the field name/type alone does not prove its combat-state meaning.

Packed v39 ReturnType at method+6 occupies FOUR bytes,not two. Three low bytes
happened to produce the same values for these indices because the high byte is
zero; that observation was not a width proof. The inspected index reader uses
four bytes for these TypeIndex values. Reading only two initially produced
impossible generic return types; that interpretation is discarded. Full indices
through the registered Il2CppType table give:

- Update,row82780:index94986,Void(kind0x01).
- AnimatedEntity.rfw,row10105:index65955,Boolean(kind0x02).
- AnimatedEntity.rfv,row10104:index94986,Void.
- gpy..ctor,row81025 and gqg..ctor,row81122:index94986,Void.

These kinds are taken from bits16..23 of the Il2CppType flags word at+8.
The Boolean return of rfw must be preserved in any replacement hook; a generic
void Update-shaped detour is not interchangeable.

## Parameter and movement-union checks

Method ParameterStart is+14(four bytes); ParameterCount is+30(two bytes).
Header section10 has offset25821112,size4264128,count355344,12-byte parameter
records: name index+0,token+4,type index+8. Current signatures decoded through
registered types are:

- AnimatedEntity.rfw: `Boolean(enw,yf,EntityMovementParam,EntityMovementParam,EntityMovementParam)`.
- AnimatedEntity.rfv: `Void(Int32,Int32,Single)`.
- gpy..ctor: `Void(CoreAnimated.Animated,String,Boolean,Boolean,mhp,Int32,String,Boolean,frv,Boolean)`.
- gqg..ctor: `Void(Int32)`.

All listed parameters have the IL2CPP byref flag clear. EntityMovementParam is
type1008 in Core.Rendering.Entity.CoreAnimatedEntity.MovementBehaviors.
The registered size table at0x186a92300 gives four words(24,8,0,0): boxed
instance24 bytes and native value8 bytes. Its eleven primitive union fields
(byte/sbyte/bool/short/ushort/int/uint/long/ulong/float/double) all have boxed
field offset16. This supports the existing eight-byte union representation
used three times by movement-dispatch.cpp; it does not independently prove
every native argument location or movement completion order. No source hook
or fingerprint gate changed.

## Rendered-entity lookup and waiter entry

Two more direct Core.dll token mappings,using the same module table:

| Owner/method | Type | Row | Token | Old RVA | Current RVA | First16bytes |
| --- | ---: | ---: | --- | --- | --- | --- |
| ebm.vkq | 3209 | 17197 | 0x0600432e | 0x27695e0 | 0x271d7e0 | 48895c2418574883ec20803da5469f04 |
| gqf.uia | 9027 | 81107 | 0x06013cd4 | 0x208a020 | 0x205e010 | 40564883ec20803dfcb30a0500488bf1 |

Metadata signature of vkq is AnimatedEntity(Int64),matching the intended
rendered-entity identity lookup. uia is Void() on the waiter type. Both are
unique direct name/arity matches in their named types. The old cleanup and
visual-path caller addresses are separate unresolved contracts; these mappings
do not authorize shortening arbitrary waits.

## Animation-start and completion-path evidence

Type9014(gpy),row81023,uia/0,token0x06013c80 maps to RVA0x20580f0,
Void(). Current first16bytes:`48896c2420564883ec20803ddf120b05`.
This is the named animation-start candidate; the old hook pins0x20853e0.
The direct table entry is not proof of provenance classification correctness.

The same type's bmat/0,row81024,token0x06013c81 maps to0x20565a0.
Disassembly reaches this ordered sequence at the end of its normal body:

```text
0x2056aa5 mov byte ptr [rsi+0x71],1
0x2056aa9 xor edx,edx
0x2056aab mov rcx,rsi
0x2056aae call 0x2033030
```

The call target0x2033030 is a completion-path candidate corresponding to the
old completeAnimationStep pin0x205e360. Its own metadata owner,cleanup slot,
watchdog behavior and all callers remain unresolved. The marker write supports
the continued relevance of offset0x71,but not the safety of an arbitrary direct
completion. No production RVA,hook or runtime state changed.

### Completion ownership and body

Reverse lookup of0x2033030 in all113251 Core.dll method-pointer slots found
five aliases: gpa.nby(row80742),gpa.ncu(80746),gpa.itk(80749),
gpa.blyf(80776,token0x06013b89),and gpt.uia(80957). This is a shared native
body,not a unique owner. The gpy.bmat call above directly supports its use for
this step,while gpa.blyf provides a named metadata mapping.

The current body performs an indirect call via class+0x278 with MethodInfo
from+0x280; it then calls0x43045a0 on step+0x30 when nonnull,clears that field,
sets Boolean step+0x1d,and invokes the delegate from step+0x10 by tail call.
This is structural evidence of cleanup/mark/callback ordering. The exact
virtual-slot-to-method mapping and the0x43045a0 callee's watchdog meaning
are not established by these instructions alone.

Metadata shows gpa.blye at virtual slot14,gpa.blyl at15,uhz at19,uia at21.
gpy overrides all four. Its blyl body at0x20563b0 writes+0x71,then calls
gpy.bmay(0x2056f30) before tail-calling gpa.blyl(0x2033340). No assumption
that blyl is the indirect target at class+0x278 is made without a verified
Il2CppClass vtable layout.

The companion layout audit `combat-visuals-layouts-371317-20261104.md`
independently attributes the requested nine field targets and their ancestry;
no numerical mismatch was demonstrated in that sample. It does not certify
live service identities or render behavior.

## Script-wait methods

Core type9026,gqe has three unique direct matches with Void return:

| Method | Args | Row | Token | Old RVA | Current RVA | First16bytes |
| --- | ---: | ---: | --- | --- | --- | --- |
| .ctor | 1 | 81102 | 0x06013ccf | 0x2089970 | 0x205d850 | 48895c2408574883ec20488bda488bf9 |
| uia | 0 | 81103 | 0x06013cd0 | 0x2089a50 | 0x205d930 | 40534883ec20488bd933d2488b4940e8 |
| blye | 0 | 81104 | 0x06013cd1 | 0x20899a0 | 0x205d880 | 40534883ec2033d2488bd9e8305afdff |

The constructor parameter resolves to fws,matching the existing context class.
uia loads step+0x40,calls0x2032fb0,stores EAX at+0x2c,then tail-calls
0x2032b70. blye calls0x20332c0 then tail-calls the shared completion body
0x2033030. These are observed ordered operations,not a complete semantic
interpretation of the callees. The three visual-only constructor caller guards
from script-waits.cpp remain old-version pins and are not relocated here.

During inspection,disassembling past a tail jump entered neighbouring functions;
only the instruction ranges ending at0x205d951 and0x205d89a respectively are
used above. No adjacent-function instructions are attributed to these methods.

### Script-wait constructor caller mappings

The three named callers retained their direct identity in current Core metadata.
Disassembly from each registered entrypoint to the next higher registered pointer
found exactly one direct call to current gqe..ctor0x205d850 in each body:

| Owner/method | Type | Row/token | Entry RVA | Bounded span | Call RVA | Return RVA | Old return |
| --- | ---: | --- | --- | ---: | --- | --- | --- |
| Core.Features.Fight.Spells.Scripts.ScriptLogic.SpellScriptRunner.uwu | 3128 | 16409/0x0600401a | 0x26d5db0 | 576 | 0x26d5f66 | 0x26d5f6b | 0x2740093 |
| SpellScriptRunner.uwv | 3128 | 16410/0x0600401b | 0x26d5ff0 | 320 | 0x26d608b | 0x26d6090 | 0x27401a0 |
| dzh.uxc | 3134 | 16430/0x0600402f | 0x26edd90 | 368 | 0x26ede6a | 0x26ede6f | 0x274164f |

Each call has the local instruction sequence `xor r8d,r8d; mov rdx,rbx;
mov rcx,rax; mov rsi,rax; call gqe..ctor`,consistent with newly allocated step,
context and null MethodInfo arguments. Spans are static search bounds,not
runtime method-size claims. These observations attribute current constructor
call sites; proving that each branch remains exclusively a visual fallback
requires the surrounding branch and script-context semantics. The source
allowlist still contains the old values; none was automatically rewritten.

## Reproduction algorithm

Read PE sections as file-backed data,using image base0x180000000. Search .rdata
and .data for exact `Core.dll\0`,then aligned64-bit references to that VA.
Read the module's name/count/methodPointers triple and require the token slot
to be in bounds. Read metadata section19(type definitions,82-byte rows) and
section5(methods,32-byte rows) using v39 offset/size/count header triples.
Type methodStart is+30,methodCount+58;method name index+0,token+20,
parameterCount+30. Resolve strings through section2. Read the methodPointers
slot through PE RVA-to-raw conversion and compare its16bytes against the old
guard. Never load or execute the image to reproduce this observation.
