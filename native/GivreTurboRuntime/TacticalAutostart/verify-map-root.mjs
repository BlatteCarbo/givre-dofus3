// Read-only verification for the installed 3.7.12.12 metadata and PE image.
// bun verify-map-root.mjs <Dofus installation directory>
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
assert.equal(metadata.readUInt32LE(4), 39);

const pe = image.readUInt32LE(0x3c);
const optional = pe + 24;
const imageBase = Number(image.readBigUInt64LE(optional + 24));
const sectionTable = optional + image.readUInt16LE(pe + 20);
const sections = Array.from({ length: image.readUInt16LE(pe + 6) }, (_, index) => {
    const row = sectionTable + index * 40;
    return {
        rva: image.readUInt32LE(row + 12),
        size: Math.max(image.readUInt32LE(row + 8), image.readUInt32LE(row + 16)),
        raw: image.readUInt32LE(row + 20),
    };
});
const raw = address => {
    const rva = address - imageBase;
    const section = sections.find(section => rva >= section.rva && rva < section.rva + section.size);
    assert(section, `Unmapped PE address ${address.toString(16)}`);
    return section.raw + rva - section.rva;
};
const pointer = address => Number(image.readBigUInt64LE(raw(address)));
const text = index => {
    const offset = 0x203824 + index;
    return metadata.toString("utf8", offset, metadata.indexOf(0, offset));
};
const typeName = index => {
    const row = 0x21db13c + index * 82;
    const name = text(metadata.readUInt32LE(row));
    const namespace = text(metadata.readUInt32LE(row + 4));
    return namespace ? `${namespace}.${name}` : name;
};
const fieldOffsets = Number(image.readBigUInt64LE(0x601ab38));
const types = Number(image.readBigUInt64LE(0x601ab18));
assert.equal(types, 0x1860c6690);
assert.equal(image.readBigUInt64LE(0x601ab30), 44466n);

for (const [typeRow, expectedClass, expectedField, expectedStatic, expectedOffset] of [
    [0x21ee842, "Core.Rendering.Map.GameObjects.MapController", "dhcf", false, 32],
    [0x226cbd4, "epf", "<doqb>k__BackingField", false, 280],
    [0x226cbd4, "epf", "<doqc>k__BackingField", true, 0],
]) {
    const typeIndex = (typeRow - 0x21db13c) / 82;
    assert.equal(typeName(typeIndex), expectedClass);
    const first = metadata.readUInt32LE(typeRow + 26);
    const count = metadata.readUInt16LE(typeRow + 62);
    const offsets = raw(pointer(fieldOffsets + typeIndex * 8));
    const fields = Array.from({ length: count }, (_, index) => {
        const row = 0x1cb8ba0 + (first + index) * 12;
        const typeIndex = metadata.readUInt32LE(row + 4);
        const type = raw(pointer(types + typeIndex * 8));
        const attributes = image.readUInt16LE(type + 8);
        return {
            row, name: text(metadata.readUInt32LE(row)), typeIndex,
            type: typeName(Number(image.readBigUInt64LE(type))),
            attributes, isStatic: (attributes & 0x10) !== 0,
            offset: image.readInt32LE(offsets + index * 4),
        };
    });
    const field = fields.find(field => field.name === expectedField);
    assert(field);
    assert.equal(field.isStatic, expectedStatic);
    assert.equal(field.offset, expectedOffset);
    console.log(`${expectedClass}.${field.name}: metadata=0x${field.row.toString(16)}, type=${field.type}, attributes=0x${field.attributes.toString(16).padStart(4, "0")}, offset=${field.offset}, storage=${field.isStatic ? "static" : "instance"}`);
    if (expectedStatic) {
        const roots = fields.filter(field => field.type === "UnityEngine.GameObject");
        assert.equal(roots.filter(field => !field.isStatic).length, 0);
        assert.equal(roots.filter(field => field.isStatic).length, 1);
        console.log("epf GameObject matches: original instance query=0; corrected static query=1");
    }
}
console.log("PASS: installed hashes, field storage, offsets, and map-root query verified without a running client.");

// Prefab reflection binding: method tokens are scoped to AddressableUtility.
for (const [row, name, token, parameters] of [
    [0x1445cac, "LoadAssetAsync", 0x06000022, 2],
    [0x1445d4c, "Release", 0x06000027, 1],
]) {
    assert.equal(text(metadata.readUInt32LE(row)), name);
    assert.equal(metadata.readUInt16LE(row + 4), 43899);
    assert.equal(metadata.readUInt32LE(row + 20), token);
    assert.equal(metadata.readUInt16LE(row + 30), parameters);
    assert.notEqual(metadata.readInt16LE(row + 18), -1);
}
assert.equal(typeName(43899), "Ankama.AddressableUtilities.Runtime.AddressableUtility");
const specs = Number(image.readBigUInt64LE(0x601ab28));
const specCount = Number(image.readBigUInt64LE(0x601ab20));
const instantiations = Number(image.readBigUInt64LE(0x601aaf8));
const findSpec = (methodIndex, typeDefinition) => {
    for (let index = 0; index < specCount; index++) {
        const row = raw(specs) + index * 12;
        if (image.readInt32LE(row) !== methodIndex) continue;
        const instantiation = pointer(instantiations + image.readInt32LE(row + 8) * 8);
        if (image.readUInt32LE(raw(instantiation)) !== 1) continue;
        const type = raw(pointer(pointer(instantiation + 8)));
        if (image[type + 10] === 18 && Number(image.readBigUInt64LE(type)) === typeDefinition) return index;
    }
    return -1;
};
assert.equal(typeName(29144), "UnityEngine.GameObject");
assert.equal(findSpec(391211, 29144), 124067);
assert.equal(typeName(29178), "UnityEngine.Object");
assert.equal(findSpec(391216, 29178), 124091);
console.log("PASS: LoadAssetAsync<GameObject> and Release<UnityEngine.Object> AOT specifications already exist.");

// Both MakeGenericMethod implementations occupy virtual slot 51. The base
// implementation unconditionally constructs NotSupportedException and throws.
assert.equal(metadata.readUInt16LE(0xeb788c + 28), 51);
assert.equal(metadata.readUInt16LE(0xebcd4c + 28), 51);
assert.equal(metadata.readUInt16LE(0xeb788c + 24), 0x1c6); // public virtual newslot
assert.equal(metadata.readUInt16LE(0xebcd4c + 24), 0xc6);  // public virtual override
const mscorlibMethods = Number(image.readBigUInt64LE(0x62b20b0));
assert.equal(pointer(mscorlibMethods + (0x27fa - 1) * 8), 0x1841d1440);
assert.equal(pointer(mscorlibMethods + (0x2aa0 - 1) * 8), 0x1842188e0);
assert.equal(pointer(mscorlibMethods + (0x09a9 - 1) * 8), 0x1842d4ba0);
assert.equal(typeName(19642), "System.NotSupportedException");
assert.equal(metadata.readUInt16LE(0xe7ae6c + 4), 19642);
assert.equal(metadata.readUInt32LE(0xe7ae6c + 20), 0x060009a9);
const relativeCall = address => {
    assert.equal(image[raw(address)], 0xe8);
    return address + 5 + image.readInt32LE(raw(address) + 1);
};
assert.equal(relativeCall(0x1841d1472), 0x1842d4ba0);
assert.equal(relativeCall(0x1841d1489), 0x18059bee0);
const literal = (image.readUInt32LE(raw(0x186c22778)) >>> 1) & 0x0fffffff;
const literalTable = metadata.readUInt32LE(8);
const literalData = metadata.readUInt32LE(20);
const start = literalData + metadata.readUInt32LE(literalTable + literal * 4);
const end = literalData + metadata.readUInt32LE(literalTable + (literal + 1) * 4);
assert.equal(metadata.toString("utf8", start, end), "Derived classes must provide an implementation.");
console.log("PASS: base MakeGenericMethod throws NotSupportedException: Derived classes must provide an implementation.");
