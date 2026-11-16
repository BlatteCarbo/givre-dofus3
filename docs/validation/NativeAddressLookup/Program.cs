using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: NativeAddressLookup <annotated-assembly.dll> <RVA-hex|type:exact-name|find:name-fragment>");
    return 2;
}

var selectedType = args[1].StartsWith("type:", StringComparison.Ordinal) ? args[1][5..] : null;
var nameFragment = args[1].StartsWith("find:", StringComparison.Ordinal) ? args[1][5..] : null;
var expectedRva = selectedType is null && nameFragment is null
    ? Convert.ToUInt64(args[1].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16) : 0;
using var input = File.OpenRead(args[0]);
using var pe = new PEReader(input);
var metadata = pe.GetMetadataReader();
var found = false;
foreach (var typeHandle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(typeHandle);
    if (nameFragment is not null)
    {
        if (metadata.GetString(type.Name).Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}");
            found = true;
        }
        continue;
    }
    if (selectedType is not null)
    {
        if (metadata.GetString(type.Name) != selectedType) continue;
        Console.WriteLine($"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}");
        foreach (var fieldHandle in type.GetFields())
        {
            var field = metadata.GetFieldDefinition(fieldHandle);
            var constantHandle = field.GetDefaultValue();
            string value = "";
            if (!constantHandle.IsNil)
            {
                var constant = metadata.GetConstant(constantHandle);
                if (constant.TypeCode == ConstantTypeCode.Int32)
                    value = $" = {metadata.GetBlobReader(constant.Value).ReadInt32()}";
                else if (constant.TypeCode == ConstantTypeCode.UInt32)
                    value = $" = {metadata.GetBlobReader(constant.Value).ReadUInt32()}";
                else if (constant.TypeCode == ConstantTypeCode.UInt16)
                    value = $" = {metadata.GetBlobReader(constant.Value).ReadUInt16()}";
                else if (constant.TypeCode == ConstantTypeCode.Int16)
                    value = $" = {metadata.GetBlobReader(constant.Value).ReadInt16()}";
                else
                    value = $" [constant {constant.TypeCode}]";
            }
            var signature = metadata.GetBlobReader(field.Signature);
            signature.ReadSignatureHeader();
            var fieldType = signature.ReadSignatureTypeCode();
            string typeName = fieldType.ToString();
            if (fieldType == SignatureTypeCode.TypeHandle)
            {
                var handle = signature.ReadTypeHandle();
                typeName = handle.Kind switch
                {
                    HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
                    HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)handle).Name),
                    _ => handle.Kind.ToString()
                };
            }
            Console.WriteLine($"  field {metadata.GetString(field.Name)} : {typeName}{value}");
        }
        foreach (var propertyHandle in type.GetProperties())
            Console.WriteLine($"  property {metadata.GetString(metadata.GetPropertyDefinition(propertyHandle).Name)}");
        found = true;
    }
    foreach (var methodHandle in type.GetMethods())
    {
        var method = metadata.GetMethodDefinition(methodHandle);
        foreach (var attributeHandle in method.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(attributeHandle);
            EntityHandle attributeType;
            if (attribute.Constructor.Kind == HandleKind.MemberReference)
                attributeType = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            else if (attribute.Constructor.Kind == HandleKind.MethodDefinition)
                attributeType = metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            else
                continue;
            var name = attributeType.Kind switch
            {
                HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)attributeType).Name),
                HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)attributeType).Name),
                _ => ""
            };
            if (name != "AddressAttribute") continue;
            var blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid custom attribute prolog");
            var count = blob.ReadUInt16();
            for (var index = 0; index < count; index++)
            {
                var kind = blob.ReadByte();
                var elementType = blob.ReadByte();
                if ((kind != 0x53 && kind != 0x54) || elementType != 0x0e)
                    throw new BadImageFormatException("Expected a named string in AddressAttribute");
                var key = blob.ReadSerializedString();
                var value = blob.ReadSerializedString();
                if (key != "RVA" || value is null || selectedType is null
                    && Convert.ToUInt64(value.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16) != expectedRva)
                    continue;
                Console.WriteLine($"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}::{metadata.GetString(method.Name)} RVA={value}");
                foreach (var parameterHandle in method.GetParameters())
                {
                    var parameter = metadata.GetParameter(parameterHandle);
                    Console.WriteLine($"  parameter {parameter.SequenceNumber}: {metadata.GetString(parameter.Name)}");
                }
                found = true;
            }
        }
    }
}
return found ? 0 : 1;
