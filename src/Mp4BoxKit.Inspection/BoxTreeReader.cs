using System.Buffers.Binary;
using System.Globalization;

namespace Mp4BoxKit.Inspection;

internal static class BoxTreeReader
{
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "moov",
        "trak",
        "mdia",
        "minf",
        "stbl",
        "edts",
        "dinf",
        "mvex",
        "moof",
        "traf",
        "mfra",
        "udta"
    };

    private static readonly HashSet<string> FullBoxes = new(StringComparer.Ordinal)
    {
        "mvhd",
        "tkhd",
        "mdhd",
        "hdlr",
        "vmhd",
        "smhd",
        "nmhd",
        "dref",
        "stsd",
        "stts",
        "ctts",
        "stsc",
        "stsz",
        "stco",
        "co64",
        "stss",
        "elst",
        "trex",
        "mfhd",
        "tfhd",
        "tfdt",
        "trun",
        "sidx",
        "tfra",
        "mfro",
        "meta",
        "esds"
    };

    private static readonly HashSet<string> VideoSampleEntries = new(StringComparer.Ordinal)
    {
        "avc1",
        "avc3",
        "hvc1",
        "hev1",
        "av01",
        "vp08",
        "vp09",
        "mp4v",
        "mp2v",
        "jpeg",
        "png ",
        "mjp2",
        "vvc1",
        "vc-1",
        "dvh1",
        "dvhe",
        "drac"
    };

    private static readonly HashSet<string> AudioSampleEntries = new(StringComparer.Ordinal)
    {
        "mp4a",
        "Opus",
        "ac-3",
        "ec-3",
        "dtsc",
        "ipcm",
        "alaw",
        "ulaw"
    };

    public static List<BoxNode> Read(
        ReadOnlySpan<byte> data,
        InspectOptions options,
        List<string> warnings)
    {
        var maxBoxes = Math.Max(1, options.MaxBoxes);
        var maxDepth = Math.Clamp(options.MaxDepth, 1, 64);
        var count = 0;
        var boxes = new List<BoxNode>();
        ParseRange(
            data,
            0,
            data.Length,
            0,
            maxDepth,
            maxBoxes,
            boxes,
            warnings,
            ref count);
        return boxes;
    }

    private static void ParseRange(
        ReadOnlySpan<byte> data,
        int start,
        int end,
        int depth,
        int maxDepth,
        int maxBoxes,
        List<BoxNode> target,
        List<string> warnings,
        ref int count)
    {
        var offset = start;
        while (offset <= end - 8)
        {
            if (count >= maxBoxes)
            {
                warnings.Add($"Box limit ({maxBoxes}) reached; remaining boxes were skipped.");
                return;
            }

            if (!TryReadHeader(data, offset, end,
                    out var size,
                    out var headerSize,
                    out var type,
                    out var truncated))
            {
                warnings.Add($"Invalid box header at offset {offset}.");
                return;
            }

            if (size > end - offset)
            {
                truncated = true;
                size = end - offset;
            }

            var node = new BoxNode
            {
                Type = type,
                Offset = offset,
                Size = size,
                HeaderSize = headerSize,
                Depth = depth,
                Truncated = truncated
            };

            if (headerSize <= size)
            {
                var payload = data.Slice(offset + headerSize, size - headerSize);
                PopulateFields(node, payload);
                if (depth < maxDepth)
                {
                    ParseChildren(data, offset, size, headerSize, depth, maxDepth,
                        maxBoxes, node, warnings, ref count);
                }
            }

            target.Add(node);
            count++;

            if (truncated)
            {
                warnings.Add($"Truncated box {type} at offset {offset}.");
                return;
            }

            offset += size;
        }
    }

    private static void ParseChildren(
        ReadOnlySpan<byte> data,
        int boxOffset,
        int boxSize,
        int headerSize,
        int depth,
        int maxDepth,
        int maxBoxes,
        BoxNode parent,
        List<string> warnings,
        ref int count)
    {
        var childOffset = GetChildrenOffset(parent.Type,
            data.Slice(boxOffset + headerSize, boxSize - headerSize));
        if (childOffset < 0)
        {
            return;
        }

        var start = boxOffset + headerSize + childOffset;
        var end = boxOffset + boxSize;
        if (start > end)
        {
            return;
        }

        ParseRange(data, start, end, depth + 1, maxDepth, maxBoxes,
            parent.Children, warnings, ref count);
    }

    private static bool TryReadHeader(
        ReadOnlySpan<byte> data,
        int offset,
        int end,
        out int size,
        out int headerSize,
        out string type,
        out bool truncated)
    {
        size = 0;
        headerSize = 8;
        type = "";
        truncated = false;

        if (offset > end - 8)
        {
            return false;
        }

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
        var fourCc = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
        type = new FourCc(fourCc).ToString();

        if (size32 == 1)
        {
            if (offset > end - 16)
            {
                return false;
            }

            headerSize = 16;
            var size64 = BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
            if (size64 > int.MaxValue)
            {
                size = end - offset;
                truncated = true;
                return true;
            }

            size = (int)size64;
        }
        else if (size32 == 0)
        {
            size = end - offset;
        }
        else
        {
            size = size32 > int.MaxValue ? end - offset : (int)size32;
            truncated = size32 > int.MaxValue;
        }

        if (size < headerSize)
        {
            return false;
        }

        return true;
    }

    private static int GetChildrenOffset(string type, ReadOnlySpan<byte> payload)
    {
        if (Containers.Contains(type))
        {
            return 0;
        }

        if (type == "meta")
        {
            return 4;
        }

        if (type is "dref" or "stsd")
        {
            return 8;
        }

        if (VideoSampleEntries.Contains(type))
        {
            return 78;
        }

        if (AudioSampleEntries.Contains(type))
        {
            if (payload.Length < 10)
            {
                return -1;
            }

            var version = BinaryPrimitives.ReadUInt16BigEndian(payload[8..]);
            return version switch
            {
                1 => 44,
                2 => 64,
                _ => 28
            };
        }

        if (type == "wvtt")
        {
            return 8;
        }

        return -1;
    }

    private static void PopulateFields(BoxNode node, ReadOnlySpan<byte> payload)
    {
        if (FullBoxes.Contains(node.Type))
        {
            if (payload.Length < 4)
            {
                return;
            }

            var version = payload[0];
            var flags = (uint)(payload[1] << 16 | payload[2] << 8 | payload[3]);
            SetFullBox(node, version, flags);
        }

        switch (node.Type)
        {
            case "ftyp":
            case "styp":
                PopulateFileType(node, payload);
                break;
            case "mvhd":
                PopulateMovieHeader(node, payload);
                break;
            case "tkhd":
                PopulateTrackHeader(node, payload);
                break;
            case "mdhd":
                PopulateMediaHeader(node, payload);
                break;
            case "hdlr":
                PopulateHandler(node, payload);
                break;
            case "stsd":
                AddU32Field(node, payload, 4, "entryCount");
                break;
            case "stts":
                PopulateTimeToSample(node, payload);
                break;
            case "stsz":
                AddU32Field(node, payload, 4, "sampleSize");
                AddU32Field(node, payload, 8, "sampleCount");
                break;
            case "stco":
            case "co64":
                AddU32Field(node, payload, 4, "entryCount");
                break;
            case "mfhd":
                AddU32Field(node, payload, 4, "sequenceNumber");
                break;
            case "tfhd":
                AddU32Field(node, payload, 4, "trackId");
                break;
            case "tfdt":
                PopulateDecodeTime(node, payload);
                break;
            case "trun":
                PopulateTrackRun(node, payload);
                break;
            case "sidx":
                PopulateSegmentIndex(node, payload);
                break;
            case "tfra":
                PopulateTrackFragmentRandomAccess(node, payload);
                break;
            case "trex":
                AddU32Field(node, payload, 4, "trackId");
                AddU32Field(node, payload, 8, "defaultSampleDuration");
                AddU32Field(node, payload, 12, "defaultSampleSize");
                AddU32Field(node, payload, 16, "defaultSampleFlags");
                break;
            case "avcC":
                AddByteField(node, payload, 0, "configurationVersion");
                AddByteField(node, payload, 1, "profile");
                AddByteField(node, payload, 3, "level");
                break;
            case "hvcC":
                AddByteField(node, payload, 0, "configurationVersion");
                break;
            case "elst":
                AddU32Field(node, payload, 4, "entryCount");
                break;
        }
    }

    private static void PopulateFileType(BoxNode node, ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 4)
        {
            node.Fields.Add(new BoxField
            {
                Name = "majorBrand",
                Value = new FourCc(BinaryPrimitives.ReadUInt32BigEndian(payload)).ToString()
            });
        }

        AddU32Field(node, payload, 4, "minorVersion");

        if (payload.Length > 8)
        {
            var brands = new List<string>();
            for (var offset = 8; offset <= payload.Length - 4; offset += 4)
            {
                brands.Add(new FourCc(
                    BinaryPrimitives.ReadUInt32BigEndian(payload[offset..])).ToString());
            }

            if (brands.Count > 0)
            {
                node.Fields.Add(new BoxField
                {
                    Name = "compatibleBrands",
                    Value = string.Join(",", brands)
                });
            }
        }
    }

    private static void PopulateMovieHeader(BoxNode node, ReadOnlySpan<byte> payload)
    {
        var version = node.Version;
        var timescaleOffset = version == 1 ? 20 : 12;
        var durationOffset = version == 1 ? 24 : 16;
        AddU32Field(node, payload, timescaleOffset, "timescale");
        if (version == 1)
        {
            AddU64Field(node, payload, durationOffset, "duration");
        }
        else
        {
            AddU32Field(node, payload, durationOffset, "duration");
        }
    }

    private static void PopulateTrackHeader(BoxNode node, ReadOnlySpan<byte> payload)
    {
        var version = node.Version;
        var trackIdOffset = version == 1 ? 20 : 12;
        var durationOffset = version == 1 ? 28 : 20;
        AddU32Field(node, payload, trackIdOffset, "trackId");
        if (version == 1)
        {
            AddU64Field(node, payload, durationOffset, "duration");
        }
        else
        {
            AddU32Field(node, payload, durationOffset, "duration");
        }
    }

    private static void PopulateMediaHeader(BoxNode node, ReadOnlySpan<byte> payload)
    {
        var version = node.Version;
        var timescaleOffset = version == 1 ? 20 : 12;
        var durationOffset = version == 1 ? 24 : 16;
        var languageOffset = version == 1 ? 32 : 20;
        AddU32Field(node, payload, timescaleOffset, "timescale");
        if (version == 1)
        {
            AddU64Field(node, payload, durationOffset, "duration");
        }
        else
        {
            AddU32Field(node, payload, durationOffset, "duration");
        }

        if (payload.Length >= languageOffset + 2)
        {
            var packed = BinaryPrimitives.ReadUInt16BigEndian(payload[languageOffset..]);
            node.Fields.Add(new BoxField
            {
                Name = "language",
                Value = UnpackLanguage(packed)
            });
        }
    }

    private static void PopulateHandler(BoxNode node, ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 12)
        {
            node.Fields.Add(new BoxField
            {
                Name = "handlerType",
                Value = new FourCc(
                    BinaryPrimitives.ReadUInt32BigEndian(payload[8..])).ToString()
            });
        }
    }

    private static void PopulateTimeToSample(BoxNode node, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        node.Fields.Add(Field("entryCount", entryCount));
        ulong sampleCount = 0;
        var offset = 8;
        for (var index = 0; index < entryCount && offset <= payload.Length - 8; index++)
        {
            sampleCount += BinaryPrimitives.ReadUInt32BigEndian(payload[offset..]);
            offset += 8;
        }

        node.Fields.Add(Field("sampleCount", sampleCount));
    }

    private static void PopulateDecodeTime(BoxNode node, ReadOnlySpan<byte> payload)
    {
        if (node.Version == 1)
        {
            AddU64Field(node, payload, 4, "baseMediaDecodeTime");
        }
        else
        {
            AddU32Field(node, payload, 4, "baseMediaDecodeTime");
        }
    }

    private static void PopulateTrackRun(BoxNode node, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        var sampleCount = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]);
        node.Fields.Add(Field("sampleCount", sampleCount));

        var offset = 8;
        if ((node.Flags & 0x1) != 0)
        {
            if (payload.Length >= offset + 4)
            {
                var dataOffset = BinaryPrimitives.ReadInt32BigEndian(payload[offset..]);
                node.Fields.Add(Field("dataOffset", dataOffset));
            }

            offset += 4;
        }

        if ((node.Flags & 0x4) != 0)
        {
            if (payload.Length >= offset + 4)
            {
                var firstFlags = BinaryPrimitives.ReadUInt32BigEndian(payload[offset..]);
                node.Fields.Add(Field("firstSampleFlags", firstFlags));
            }

            offset += 4;
        }

        ulong duration = 0;
        var hasDuration = (node.Flags & 0x100) != 0;
        var hasSize = (node.Flags & 0x200) != 0;
        var hasFlags = (node.Flags & 0x400) != 0;
        var hasCompositionOffset = (node.Flags & 0x800) != 0;
        var entrySize = 0;
        if (hasDuration)
        {
            entrySize += 4;
        }

        if (hasSize)
        {
            entrySize += 4;
        }

        if (hasFlags)
        {
            entrySize += 4;
        }

        if (hasCompositionOffset)
        {
            entrySize += 4;
        }

        if (entrySize == 0)
        {
            return;
        }

        for (var index = 0; index < sampleCount && offset <= payload.Length - entrySize; index++)
        {
            if (hasDuration)
            {
                duration += BinaryPrimitives.ReadUInt32BigEndian(payload[offset..]);
            }

            offset += entrySize;
        }

        node.Fields.Add(Field("duration", duration));
    }

    private static void PopulateSegmentIndex(BoxNode node, ReadOnlySpan<byte> payload)
    {
        AddU32Field(node, payload, 4, "referenceId");
        AddU32Field(node, payload, 8, "timescale");
        if (node.Version == 1)
        {
            AddU64Field(node, payload, 12, "earliestPresentationTime");
            AddU64Field(node, payload, 20, "firstOffset");
            AddU16Field(node, payload, 30, "referenceCount");
        }
        else
        {
            AddU32Field(node, payload, 12, "earliestPresentationTime");
            AddU32Field(node, payload, 16, "firstOffset");
            AddU16Field(node, payload, 22, "referenceCount");
        }
    }

    private static void PopulateTrackFragmentRandomAccess(BoxNode node, ReadOnlySpan<byte> payload)
    {
        AddU32Field(node, payload, 4, "trackId");
        if (payload.Length >= 12)
        {
            var lengths = BinaryPrimitives.ReadUInt32BigEndian(payload[8..]);
            node.Fields.Add(new BoxField
            {
                Name = "lengths",
                Value = $"0x{lengths:X8}"
            });
        }

        AddU32Field(node, payload, 12, "entryCount");
    }

    private static void SetFullBox(BoxNode node, byte version, uint flags)
    {
        node.Version = version;
        node.Flags = flags;
        node.IsFullBox = true;
    }

    private static void AddU32Field(
        BoxNode node,
        ReadOnlySpan<byte> payload,
        int offset,
        string name)
    {
        if (payload.Length < offset + 4)
        {
            return;
        }

        node.Fields.Add(Field(name,
            BinaryPrimitives.ReadUInt32BigEndian(payload[offset..])));
    }

    private static void AddU64Field(
        BoxNode node,
        ReadOnlySpan<byte> payload,
        int offset,
        string name)
    {
        if (payload.Length < offset + 8)
        {
            return;
        }

        node.Fields.Add(Field(name,
            BinaryPrimitives.ReadUInt64BigEndian(payload[offset..])));
    }

    private static void AddU16Field(
        BoxNode node,
        ReadOnlySpan<byte> payload,
        int offset,
        string name)
    {
        if (payload.Length < offset + 2)
        {
            return;
        }

        node.Fields.Add(Field(name,
            BinaryPrimitives.ReadUInt16BigEndian(payload[offset..])));
    }

    private static void AddByteField(
        BoxNode node,
        ReadOnlySpan<byte> payload,
        int offset,
        string name)
    {
        if (payload.Length <= offset)
        {
            return;
        }

        node.Fields.Add(Field(name, payload[offset]));
    }

    private static BoxField Field(string name, object value)
        => new()
        {
            Name = name,
            Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };

    private static string UnpackLanguage(ushort value)
        => new(new[]
        {
            (char)(((value >> 10) & 31) + 0x60),
            (char)(((value >> 5) & 31) + 0x60),
            (char)((value & 31) + 0x60)
        });
}
