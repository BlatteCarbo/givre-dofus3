// Bounded, read-only inspection of the pinned client, not runtime evidence.
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { join } from "node:path";
import assert from "node:assert/strict";

const root = process.argv[2];
assert(root, "Provide the installed Dofus directory.");
const metadata = readFileSync(join(root, "Dofus_Data/il2cpp_data/Metadata/global-metadata.dat"));
const image = readFileSync(join(root, "GameAssembly.dll"));
const hash = bytes => createHash("sha256").update(bytes).digest("hex");
assert.equal(hash(metadata), "ba1efbad5a8ecca48775fc20293f17e9dece72cc6134c4e7ecdd8071e7c67db6");
assert.equal(hash(image), "70b3861ded2ffa4a963bb6eaac8ff3c470c2f062e641601628c95f60411e8663");
const text = index => {
    const start = 0x203824 + index;
    assert(start < metadata.length);
    return metadata.toString("utf8", start, metadata.indexOf(0, start));
};
const pe = image.readUInt32LE(0x3c), optional = pe + 24;
const base = Number(image.readBigUInt64LE(optional + 24));
const table = optional + image.readUInt16LE(pe + 20);
const sections = Array.from({ length: image.readUInt16LE(pe + 6) }, (_, i) => {
    const at = table + i * 40;
    return { rva: image.readUInt32LE(at + 12), size: image.readUInt32LE(at + 16), raw: image.readUInt32LE(at + 20), executable: (image.readUInt32LE(at + 36) & 0x20000000) !== 0 };
});
const raw = address => {
    const rva = address - base;
    const s = sections.find(s => rva >= s.rva && rva < s.rva + s.size);
    assert(s, `Unmapped address ${address.toString(16)}`);
    return s.raw + rva - s.rva;
};
const pointer = address => Number(image.readBigUInt64LE(raw(address)));
const sceneryInitializers = new Set();
const sceneryLiteralSlots = new Set();
// Locate the exact scenery name through the same literal encoding verified by
// verify-map-root.mjs. Only report candidates; a string reference is not ownership.
const literalTable = metadata.readUInt32LE(8);
const literalCount = metadata.readUInt32LE(16);
const literalData = metadata.readUInt32LE(20);
const knownEncoded = image.readUInt32LE(raw(0x186c22778));
const literalKind = knownEncoded & 0xe0000000;
assert(literalCount > 0, "Require a nonempty pinned literal index.");
const sceneryNames = new Set(["MapBackground", "Background", "MapForeground", "Foreground",
    "Map", "MapAnimatedElements", "MapParticles"]);
for (let i = 0; i < literalCount - 1; i++) {
    const start = literalData + metadata.readUInt32LE(literalTable + i * 4);
    const end = literalData + metadata.readUInt32LE(literalTable + (i + 1) * 4);
    if (end - start > 32) continue;
    const value = metadata.toString("utf8", start, end);
    if (!sceneryNames.has(value)) continue;
    console.log(JSON.stringify({ sceneryLiteralText: value, index: i }));
    const encoded = Buffer.alloc(4);
    encoded.writeUInt32LE((literalKind | (i << 1) | 1) >>> 0);
    let matches = 0;
    for (let at = image.indexOf(encoded); at !== -1 && matches < 8; at = image.indexOf(encoded, at + 1)) {
        const s = sections.find(s => at >= s.raw && at < s.raw + s.size);
        if (!s || at % 8 !== 0) continue;
        const slot = base + s.rva + at - s.raw;
        sceneryLiteralSlots.add(slot);
        console.log(JSON.stringify({ sceneryLiteral: i, value, slot: `0x${slot.toString(16)}` }));
        matches++;
    }
}
// Locate the metadata image owning epf rather than assuming Assembly-CSharp.
const imageNames = new Set();
let ownerTypes;
// v39 sections are (offset, byte size, count); TypeDefinitionIndex is UInt16.
const imagesHeader = 8 + 20 * 12;
const imagesStart = metadata.readUInt32LE(imagesHeader);
const imagesSize = metadata.readUInt32LE(imagesHeader + 4);
const imagesCount = metadata.readUInt32LE(imagesHeader + 8);
assert.equal(imagesSize / imagesCount, 36);
for (let i = 0; i < imagesCount; i++) {
    const at = imagesStart + i * 36;
    const first = metadata.readUInt16LE(at + 8), count = metadata.readUInt32LE(at + 10);
    if (first > 7276 || first + count <= 7276) continue;
    const name = text(metadata.readUInt32LE(at));
    assert(name.endsWith(".dll"));
    imageNames.add(name);
    ownerTypes = { first, end: first + count };
}
assert.equal(imageNames.size, 1, "Require unique metadata image owning epf.");
const moduleName = [...imageNames][0];
const modules = [];
for (let start = image.indexOf(Buffer.from(`${moduleName}\0`)); start !== -1;
    start = image.indexOf(Buffer.from(`${moduleName}\0`), start + 1)) {
    const section = sections.find(s => start >= s.raw && start < s.raw + s.size);
    if (!section) continue;
    const nameAddress = base + section.rva + start - section.raw;
    const encoded = Buffer.alloc(8);
    encoded.writeBigUInt64LE(BigInt(nameAddress));
    for (let at = image.indexOf(encoded); at !== -1; at = image.indexOf(encoded, at + 1)) {
        if (at + 24 > image.length) continue;
        const count = image.readUInt32LE(at + 8);
        const methods = Number(image.readBigUInt64LE(at + 16));
        if (count < 0x1209d || count > 392887
            || !sections.some(s => methods - base >= s.rva && methods - base + count * 8 <= s.rva + s.size)) continue;
        modules.push({ at, count, methods });
    }
}
assert.equal(modules.length, 1, `Require unique ${moduleName} code-generation module.`);
const nativeMethods = modules[0];
console.log(JSON.stringify({ moduleName, codeGenerationModule: nativeMethods }));
const callTargets = new Set([0x182e880f0, 0x18385b040, 0x182e86d00]);
for (const address of [0x182e8815f + 0x2ae6f91, 0x182e88167 + 0x2ae459d, 0x182e8816f + 0x2ae6f75])
    console.log(JSON.stringify({ conversionConstant: `0x${address.toString(16)}`, value: image.readFloatLE(raw(address)) }));
const targetTokens = new Map();
const resolvedCallees = [];
for (let rid = 1; rid <= nativeMethods.count; rid++) {
    const address = pointer(nativeMethods.methods + (rid - 1) * 8);
    if (callTargets.has(address)) targetTokens.set(0x06000000 + rid, address);
}
for (let i = 0; i < 392887; i++) {
    const at = 0x85574c + i * 32;
    const owner = metadata.readUInt16LE(at + 4), token = metadata.readUInt32LE(at + 20);
    if (owner < ownerTypes.first || owner >= ownerTypes.end || !targetTokens.has(token)) continue;
    resolvedCallees.push({ owner, name: text(metadata.readUInt32LE(at)),
        address: `0x${targetTokens.get(token).toString(16)}` });
}
const types = Number(image.readBigUInt64LE(0x601ab18));
const typeName = i => {
    assert(i >= 0 && i < 44466);
    const at = 0x21db13c + i * 82;
    const name = text(metadata.readUInt32LE(at));
    const ns = text(metadata.readUInt32LE(at + 4));
    return ns ? `${ns}.${name}` : name;
};
// ub.qih passes these three MethodInfo slots to the same component factory.
// Decode only their pinned MethodRef entries; names do not establish scene ownership.
const methodSpecs = Number(image.readBigUInt64LE(0x601ab28));
const methodSpecCount = Number(image.readBigUInt64LE(0x601ab20));
const genericInstantiations = Number(image.readBigUInt64LE(0x601aaf8));
for (const slot of [0x186be2990, 0x186be2778, 0x186be2360, 0x186bc19f8, 0x186bc17e0]) {
    const encoded = image.readUInt32LE(raw(slot));
    assert.equal(encoded >>> 29, 6, "Require an encoded MethodRef, not a runtime pointer.");
    assert.equal(encoded & 1, 1);
    const specIndex = (encoded & 0x1fffffff) >>> 1;
    assert(specIndex < methodSpecCount);
    const spec = raw(methodSpecs) + specIndex * 12;
    const methodIndex = image.readInt32LE(spec);
    assert(methodIndex >= 0 && methodIndex < 392887);
    const method = 0x85574c + methodIndex * 32;
    const instantiationIndex = image.readInt32LE(spec + 8);
    assert(instantiationIndex >= 0);
    const instantiation = pointer(genericInstantiations + instantiationIndex * 8);
    const count = image.readUInt32LE(raw(instantiation));
    assert(count > 0 && count <= 4);
    const argumentsAddress = pointer(instantiation + 8);
    const argumentsList = Array.from({ length: count }, (_, index) => {
        const type = raw(pointer(argumentsAddress + index * 8));
        const kind = image[type + 10];
        assert(kind === 18 || kind === 17, "Require a concrete component type.");
        return typeName(Number(image.readBigUInt64LE(type)));
    });
    console.log(JSON.stringify({ factoryComponentSlot: `0x${slot.toString(16)}`, specIndex,
        methodIndex, classInstantiationIndex: image.readInt32LE(spec + 4), instantiationIndex,
        declaringType: typeName(metadata.readUInt16LE(method + 4)),
        method: text(metadata.readUInt32LE(method)), arguments: argumentsList }));
}
const mergedLayerAddress = 0x18352a6b0;
const mergedLayerOffset = raw(mergedLayerAddress);
console.log(JSON.stringify({ sceneryLifecycle: "ub.qij<merged>",
    address: `0x${mergedLayerAddress.toString(16)}`,
    hex: image.subarray(mergedLayerOffset, mergedLayerOffset + 1536).toString("hex") }));
for (let i = ownerTypes.first; i < ownerTypes.end; i++) {
    const name = typeName(i);
    if (/Rendering\.Map|MergedMap|MapLayer|MapBackground|MapRenderer/.test(name))
        console.log(JSON.stringify({ mapTypeCandidate: name, index: i }));
    const definition = 0x21db13c + i * 82;
    const firstField = metadata.readUInt32LE(definition + 26);
    const count = metadata.readUInt16LE(definition + 62);
    for (let fieldIndex = 0; fieldIndex < count; fieldIndex++) {
        const field = 0x1cb8ba0 + (firstField + fieldIndex) * 12;
        const type = raw(pointer(types + metadata.readUInt32LE(field + 4) * 8));
        if (image[type + 10] !== 18) continue;
        const fieldType = typeName(Number(image.readBigUInt64LE(type)));
        if (fieldType !== "ub" && fieldType !== "uj" && fieldType !== "ue") continue;
        console.log(JSON.stringify({ sceneryOwnerReference: name,
            field: text(metadata.readUInt32LE(field)), fieldType }));
    }
}
for (const callee of resolvedCallees) {
    console.log(JSON.stringify({ conversionCallee: typeName(callee.owner), name: callee.name, address: callee.address }));
    const offset = raw(Number(callee.address));
    for (let i = 0; i < 384; i += 32)
        console.log(`CALLEE_BYTES ${callee.name} ${i} ${image.subarray(offset + i, offset + i + 32).toString("hex")}`);
}
const fieldOffsets = Number(image.readBigUInt64LE(0x601ab38));
for (const wanted of ["epf", "epk", "fyb", "eph", "epg", "exj", "fbe",
    "ub", "uj", "ue", "Core.Rendering.MapElementsPool", "Core.Rendering.MapParticlesPool",
    "Core.Rendering.GameObjects.MapElement", "Core.World.InteractiveCellManager"]) {
    const index = Array.from({ length: 44466 }, (_, i) => i).find(i => typeName(i) === wanted);
    assert(index !== undefined, `Missing ${wanted}`);
    const at = 0x21db13c + index * 82;
    const firstField = metadata.readUInt32LE(at + 26);
    const firstMethod = metadata.readUInt32LE(at + 30);
    const fieldCount = metadata.readUInt16LE(at + 62);
    const methodCount = metadata.readUInt16LE(at + 58);
    assert(fieldCount <= 128 && methodCount <= 256);
    const fields = Array.from({ length: fieldCount }, (_, i) => {
        const f = 0x1cb8ba0 + (firstField + i) * 12;
        const t = raw(pointer(types + metadata.readUInt32LE(f + 4) * 8));
        const kind = image[t + 10];
        const offsets = raw(pointer(fieldOffsets + index * 8));
        return { name: text(metadata.readUInt32LE(f)), kind, offset: image.readInt32LE(offsets + i * 4),
            class: kind === 18 || kind === 17 ? typeName(Number(image.readBigUInt64LE(t))) : null };
    });
    const methods = Array.from({ length: methodCount }, (_, i) => {
        const m = 0x85574c + (firstMethod + i) * 32;
        assert.equal(metadata.readUInt16LE(m + 4), index);
        const token = metadata.readUInt32LE(m + 20);
        const rid = token & 0xffffff;
        assert(rid > 0 && rid <= nativeMethods.count);
        return { name: text(metadata.readUInt32LE(m)), token: `0x${token.toString(16)}`,
            address: `0x${pointer(nativeMethods.methods + (rid - 1) * 8).toString(16)}`,
            parameterCount: metadata.readUInt16LE(m + 30) };
    });
    console.log(JSON.stringify({ class: wanted, index, fieldCount, methodCount }));
    for (const field of fields) console.log(JSON.stringify({ owner: wanted, field }));
    for (const method of methods) console.log(JSON.stringify({ owner: wanted, method }));
    if (wanted === "Core.Rendering.MapElementsPool" || wanted === "Core.Rendering.MapParticlesPool") {
        for (const method of methods.filter(m => m.name === ".ctor")) {
            const start = raw(Number(method.address));
            console.log(JSON.stringify({ poolConstructor: wanted, address: method.address }));
            for (let offset = 0; offset < 80; offset += 8)
                console.log(`POOL_BYTES ${wanted} +${offset} ${image.subarray(start + offset, start + offset + 8).toString("hex")}`);
        }
    }
    if (wanted === "ub" || wanted === "uj") {
        const constructor = methods.find(m => m.name === ".ctor");
        assert(constructor);
        const offset = raw(Number(constructor.address));
        const length = wanted === "ub" ? 160 : 128;
        console.log(JSON.stringify({ constructorBytes: wanted, address: constructor.address,
            hex: image.subarray(offset, offset + length).toString("hex") }));
        const initializer = methods.find(m => m.name === (wanted === "ub" ? "qih" : "qjy"));
        assert(initializer);
        sceneryInitializers.add(Number(initializer.address));
        const start = raw(Number(initializer.address));
        const size = wanted === "ub" ? 368 : 208;
        console.log(JSON.stringify({ sceneryInitializer: wanted, address: initializer.address,
            hex: image.subarray(start, start + size).toString("hex") }));
        for (let offset = 0; offset < size; offset += 8)
            console.log(`FACTORY_BYTES ${wanted} +${offset} ${image.subarray(start + offset, start + offset + 8).toString("hex")}`);
        if (wanted === "ub") {
            for (const [name, length] of [["hni", 368], ["qii", 784], ["gvh", 784], ["eiu", 784], ["qik", 160]]) {
                const lifecycle = methods.find(method => method.name === name);
                assert(lifecycle && Number(lifecycle.address) !== 0);
                const offset = raw(Number(lifecycle.address));
                console.log(JSON.stringify({ sceneryLifecycle: `ub.${name}`, address: lifecycle.address,
                    hex: image.subarray(offset, offset + length).toString("hex") }));
            }
        }
    }
    if (wanted === "epf") {
        const conversion = methods.find(m => m.name === "xkz");
        assert(conversion);
        const address = Number(conversion.address);
        const offset = raw(address);
        for (let i = 0; i < 384; i += 32)
            console.log(`METHOD_BYTES ${i} ${image.subarray(offset + i, offset + i + 32).toString("hex")}`);
    }
}
// Factories may be passed as delegates instead of called directly. Report
// aligned native-pointer references separately from instruction candidates.
for (const target of sceneryInitializers) {
    const encoded = Buffer.alloc(8);
    encoded.writeBigUInt64LE(BigInt(target));
    for (let at = image.indexOf(encoded); at !== -1; at = image.indexOf(encoded, at + 1)) {
        const section = sections.find(s => at >= s.raw && at + 8 <= s.raw + s.size);
        if (!section || at % 8 !== 0) continue;
        console.log(JSON.stringify({ sceneryPointerReference: `0x${(base + section.rva + at - section.raw).toString(16)}`,
            target: `0x${target.toString(16)}` }));
    }
}
// Direct-call candidates only: an E8 byte inside another instruction can match.
// Inspect the surrounding instruction stream before treating a hit as a caller.
for (const section of sections.filter(s => s.executable)) {
    // x64 LEA reg,[RIP+disp32] is the usual delegate-factory reference.
    for (let at = section.raw; at + 7 <= section.raw + section.size; at++) {
        if ((image[at] & 0xf8) !== 0x48 || image[at + 1] !== 0x8d
            || (image[at + 2] & 0xc7) !== 0x05) continue;
        const address = base + section.rva + at - section.raw;
        const target = address + 7 + image.readInt32LE(at + 3);
        if (sceneryLiteralSlots.has(target)) {
            const start = Math.max(section.raw, at - 16);
            console.log(JSON.stringify({ sceneryLiteralCodeCandidate: `0x${address.toString(16)}`,
                slot: `0x${target.toString(16)}`, windowAddress: `0x${(address - at + start).toString(16)}`,
                hex: image.subarray(start, Math.min(section.raw + section.size, at + 112)).toString("hex") }));
        }
        if (!sceneryInitializers.has(target)) continue;
        console.log(JSON.stringify({ sceneryDelegateCandidate: `0x${address.toString(16)}`,
            target: `0x${target.toString(16)}` }));
    }
    for (let at = section.raw; at + 5 <= section.raw + section.size; at++) {
        if (image[at] !== 0xe8 && image[at] !== 0xe9) continue;
        const address = base + section.rva + at - section.raw;
        const target = address + 5 + image.readInt32LE(at + 1);
        if (!sceneryInitializers.has(target)) continue;
        const start = Math.max(section.raw, at - 32);
        let enclosingAddress = 0;
        const enclosingTokens = new Set();
        for (let rid = 1; rid <= nativeMethods.count; rid++) {
            const candidate = pointer(nativeMethods.methods + (rid - 1) * 8);
            if (candidate > address || candidate < enclosingAddress) continue;
            if (candidate > enclosingAddress) {
                enclosingAddress = candidate;
                enclosingTokens.clear();
            }
            enclosingTokens.add(0x06000000 + rid);
        }
        const enclosingMethods = [];
        for (let i = 0; i < 392887; i++) {
            const method = 0x85574c + i * 32;
            const owner = metadata.readUInt16LE(method + 4);
            if (owner < ownerTypes.first || owner >= ownerTypes.end
                || !enclosingTokens.has(metadata.readUInt32LE(method + 20))) continue;
            enclosingMethods.push({ owner: typeName(owner), name: text(metadata.readUInt32LE(method)) });
        }
        console.log(JSON.stringify({ sceneryCallerCandidate: `0x${address.toString(16)}`,
            target: `0x${target.toString(16)}`, windowAddress: `0x${(address - at + start).toString(16)}`,
            opcode: image[at],
            nearestMethodAddress: `0x${enclosingAddress.toString(16)}`, enclosingMethods,
            hex: image.subarray(start, Math.min(section.raw + section.size, at + 69)).toString("hex") }, null, 2));
    }
}
