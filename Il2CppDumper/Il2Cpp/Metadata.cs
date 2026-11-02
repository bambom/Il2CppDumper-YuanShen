using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Il2CppDumper
{
    public sealed class Metadata : BinaryStream
    {
        public Il2CppGlobalMetadataHeader header;
        public Il2CppImageDefinition[] imageDefs;
        public Il2CppTypeDefinition[] typeDefs;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private Dictionary<int, Il2CppFieldDefaultValue> fieldDefaultValuesDic;
        private Dictionary<int, Il2CppParameterDefaultValue> parameterDefaultValuesDic;
        public Il2CppPropertyDefinition[] propertyDefs;
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
        private Dictionary<Il2CppImageDefinition, Dictionary<uint, int>> attributeTypeRangesDic;
        private Il2CppStringLiteral[] stringLiterals;
        private Il2CppMetadataUsageList[] metadataUsageLists;
        private Il2CppMetadataUsagePair[] metadataUsagePairs;
        public int[] attributeTypes;
        public int[] interfaceIndices;
        public Dictionary<uint, SortedDictionary<uint, uint>> metadataUsageDic;
        public long maxMetadataUsages;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;
        public uint[] vtableMethods;
        public Il2CppRGCTXDefinition[] rgctxEntries;

        private Dictionary<uint, string> stringCache = new Dictionary<uint, string>();
        public ulong Address;

        private byte[] stringDecryptionBlob = null;

        public Metadata(Stream stream) : base(stream)
        {
            /*var sanity = ReadUInt32();
            if (sanity != 0xFAB11BAF)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            var version = ReadInt32();
            if (version < 16 || version > 27)
            {
                throw new NotSupportedException($"ERROR: Metadata file supplied is not a supported version[{version}].");
            }
            Version = version;*/
            Version = 24;
            header = ReadClass<Il2CppGlobalMetadataHeader>(0);

            // decryption_blob.bin follows the executable (repo root / build output); no hardcoded machine path.
            stringDecryptionBlob = File.ReadAllBytes(ResolveBlobPath());

            /*if (version == 24)
            {
                if (header.stringLiteralOffset == 264)
                {
                    Version = 24.2f;
                    header = ReadClass<Il2CppGlobalMetadataHeader>(0);
                }
                else
                {
                    imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesCount);
                    if (imageDefs.Any(x => x.token != 1))
                    {
                        Version = 24.1f;
                    }
                }
            }*/
            // ---- Genshin PC CTest 1.4.50 (dev, 2021-03) build-specific header overrides ----
            // Derived 2026-11-02 by content cross-validation against this exact global-metadata.dat
            // (sha256 12ab21e6...): string table anchored via image nameIdx -> mscorlib.dll/System.dll,
            // tables tile contiguously; genericContainers 99730*12B, interfaces 14132 ints,
            // nestedTypes 10552 ints. See Projects/RecoveryData/Genshin/knowledge for evidence.
            header.genericContainersOffset = 0xD26898; header.genericContainersCount = 1196760;
            header.interfacesOffset = 0xE4AB70; header.interfacesCount = 56528;
            header.nestedTypesOffset = 0xE58840; header.nestedTypesCount = 42208;

            imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesCount);
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(header.typeDefinitionsOffset, header.typeDefinitionsCount);
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(header.methodsOffset, header.methodsCount);
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(header.parametersOffset, header.parametersCount);
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(header.fieldsOffset, header.fieldsCount);
            var fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValuesOffset, header.fieldDefaultValuesCount);
            var parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValuesOffset, header.parameterDefaultValuesCount);
            fieldDefaultValuesDic = fieldDefaultValues.ToDictionary(x => x.fieldIndex);
            parameterDefaultValuesDic = parameterDefaultValues.ToDictionary(x => x.parameterIndex);
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(header.propertiesOffset, header.propertiesCount);
            interfaceIndices = ReadClassArray<int>(header.interfacesOffset, header.interfacesCount / 4);
            nestedTypeIndices = ReadClassArray<int>(header.nestedTypesOffset, header.nestedTypesCount / 4);
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(header.eventsOffset, header.eventsCount);
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainersOffset, header.genericContainersCount);
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(header.genericParametersOffset, header.genericParametersCount);
            constraintIndices = ReadClassArray<int>(header.genericParameterConstraintsOffset, header.genericParameterConstraintsCount / 4);
            vtableMethods = ReadClassArray<uint>(header.vtableMethodsOffset, header.vtableMethodsCount / 4);
            if (Version > 16 && Version < 27) //TODO
            {
                // CTest 1.4.50: string literal entries/data region and this build's 0x5000 key blob are
                // NOT yet located (retail-1.4 blob does not decrypt this build). Literals are skipped;
                // identity data (types/methods/fields) is unaffected. PENDING: extract key from UserAssembly.
                stringLiterals = new Il2CppStringLiteral[0];
                metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(header.metadataUsageListsOffset, header.metadataUsageListsCount);
                metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(header.metadataUsagePairsOffset, header.metadataUsagePairsCount);

                ProcessingMetadataUsage();

                fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefsOffset, header.fieldRefsCount);
            }
            if (Version > 20)
            {
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(header.attributesInfoOffset, header.attributesInfoCount);
                attributeTypes = ReadClassArray<int>(header.attributeTypesOffset, header.attributeTypesCount / 4);
            }
            /*if (Version > 24)
            {
                attributeTypeRangesDic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>();
                foreach (var imageDef in imageDefs)
                {
                    var dic = new Dictionary<uint, int>();
                    attributeTypeRangesDic[imageDef] = dic;
                    var end = imageDef.customAttributeStart + imageDef.customAttributeCount;
                    for (int i = imageDef.customAttributeStart; i < end; i++)
                    {
                        dic.Add(attributeTypeRanges[i].token, i);
                    }
                }
            }*/
            if (Version <= 24.1f)
            {
                rgctxEntries = ReadMetadataClassArray<Il2CppRGCTXDefinition>(header.rgctxEntriesOffset, header.rgctxEntriesCount);
            }
        }

        private T[] ReadMetadataClassArray<T>(uint addr, int count) where T : new()
        {
            return ReadClassArray<T>(addr, count / SizeOf(typeof(T)));
        }

        public bool GetFieldDefaultValueFromIndex(int index, out Il2CppFieldDefaultValue value)
        {
            return fieldDefaultValuesDic.TryGetValue(index, out value);
        }

        public bool GetParameterDefaultValueFromIndex(int index, out Il2CppParameterDefaultValue value)
        {
            return parameterDefaultValuesDic.TryGetValue(index, out value);
        }

        public uint GetDefaultValueFromIndex(int index)
        {
            return (uint)(header.fieldAndParameterDefaultValueDataOffset + index);
        }

        private Dictionary<uint, bool> indexlist = new Dictionary<uint, bool>();
        public string GetStringFromIndex(uint index)
        {
            if (!stringCache.TryGetValue(index, out var result))
            {
                //result = ReadStringToNull(header.stringOffset + index);
                result = ReadStringToNull(0xE63A28 + index); // CTest 1.4.50 string table (anchored via image nameIdx)
                stringCache.Add(index, result);
            }
            return result;
        }

        private void writetofile(string filename, string data)
        {
            StreamWriter file = new StreamWriter(@"G:\" + filename, true);
            file.WriteLine(data);
            file.Close();
        }

        public int GetCustomAttributeIndex(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (Version > 24)
            {
                if (attributeTypeRangesDic[imageDef].TryGetValue(token, out var index))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return customAttributeIndex;
            }
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            Position = (ulong)(SizeOf(typeof(Il2CppGlobalMetadataHeader)) + stringLiteral.dataIndex);

            var buffer = ReadBytes((int)stringLiteral.length);
            for (var i = 0; i < stringLiteral.length; i++)
            {
                byte cl = (byte)(buffer[i] ^ stringDecryptionBlob[(0x1400 + i) % 0x5000]);
                byte al = (byte)(stringDecryptionBlob[i % 0x2800 + index % 0x2800] + i);
                buffer[i] = (byte)(cl ^ al);
            }

            return Encoding.UTF8.GetString(buffer);
        }

        private void ProcessingMetadataUsage()
        {
            metadataUsageDic = new Dictionary<uint, SortedDictionary<uint, uint>>();
            for (uint i = 1; i <= 6u; i++)
            {
                metadataUsageDic[i] = new SortedDictionary<uint, uint>();
            }
            foreach (var metadataUsageList in metadataUsageLists)
            {
                for (int i = 0; i < metadataUsageList.count; i++)
                {
                    var offset = metadataUsageList.start + i;
                    if (offset >= metadataUsagePairs.Length) break; // CTest 1.4.50: list ranges clamp to file end
                    var metadataUsagePair = metadataUsagePairs[offset];
                    var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                    if (usage == 0 || usage > 6) continue; // CTest 1.4.50: 5 malformed pairs (7) / null usages (0) sampled
                    var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                    metadataUsageDic[usage][metadataUsagePair.destinationIndex] = decodedIndex;
                }
            }
            maxMetadataUsages = metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
        }

        public uint GetEncodedIndexType(uint index)
        {
            return (index & 0xE0000000) >> 29;
        }

        public uint GetDecodedMethodIndex(uint index)
        {
            if (Version >= 27)
            {
                return (index & 0x1FFFFFFEU) >> 1;
            }
            return index & 0x1FFFFFFFU;
        }

        public int SizeOf(Type type)
        {
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute)Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (Version < attr.Min || Version > attr.Max)
                        continue;
                }
                var fieldType = i.FieldType;
                if (fieldType.IsPrimitive)
                {
                    size += GetPrimitiveTypeSize(fieldType.Name);
                }
                else if (fieldType.IsEnum)
                {
                    var e = fieldType.GetField("value__").FieldType;
                    size += GetPrimitiveTypeSize(e.Name);
                }
                else
                {
                    size += SizeOf(fieldType);
                }
            }
            return size;

            int GetPrimitiveTypeSize(string name)
            {
                switch (name)
                {
                    case "Int32":
                    case "UInt32":
                        return 4;
                    case "Int16":
                    case "UInt16":
                        return 2;
                    default:
                        return 0;
                }
            }
        }

        public string ReadString(int numChars)
        {
            var start = Position;
            // UTF8 takes up to 4 bytes per character
            var str = Encoding.UTF8.GetString(ReadBytes(numChars * 4)).Substring(0, numChars);
            // make our position what it would have been if we'd known the exact number of bytes needed.
            Position = start;
            ReadBytes(Encoding.UTF8.GetByteCount(str));
            return str;
        }

        private static string ResolveBlobPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "decryption_blob.bin"),
                "decryption_blob.bin",
                Path.Combine(Path.GetDirectoryName(typeof(Metadata).Assembly.Location) ?? ".", "decryption_blob.bin"),
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("decryption_blob.bin not found beside the executable or in the working directory");
        }
    }
}
