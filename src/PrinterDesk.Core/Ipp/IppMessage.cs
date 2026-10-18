using System.Buffers.Binary;
using System.Text;

namespace PrinterDesk.Core.Ipp;

/// <summary>Delimiter and value tags (RFC 8010 §3.5).</summary>
public enum IppTag : byte
{
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    End = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,
    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,
    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BeginCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A,
}

public readonly record struct IppResolution(int X, int Y, bool PerInch);

public readonly record struct IppRange(int Lower, int Upper);

/// <summary>
/// One attribute with all its values. Values are <see cref="int"/>, <see cref="bool"/>, <see cref="string"/>,
/// <see cref="IppResolution"/>, <see cref="IppRange"/>, <c>byte[]</c> (octet strings, dates), or null
/// (out-of-band values and collections, whose members are not kept).
/// </summary>
public sealed record IppAttribute(IppTag Group, string Name, IppTag ValueTag, IReadOnlyList<object?> Values);

/// <summary>An IPP request or response (RFC 8010): version, operation or status code, request id, attributes.</summary>
public sealed class IppMessage
{
    /// <summary>Limits applied when decoding, so a misbehaving device cannot exhaust memory.</summary>
    private const int MaxAttributes = 10_000;
    private const int MaxCollectionDepth = 16;

    public ushort Code { get; init; }

    public int RequestId { get; init; }

    public List<IppAttribute> Attributes { get; } = [];

    /// <summary>Standard request header: charset, language, printer URI and user name.</summary>
    public static IppMessage Request(IppOperation operation, Uri printerUri, int requestId)
    {
        var message = new IppMessage { Code = (ushort)operation, RequestId = requestId };
        message.Add(IppTag.OperationAttributes, "attributes-charset", IppTag.Charset, "utf-8");
        message.Add(IppTag.OperationAttributes, "attributes-natural-language", IppTag.NaturalLanguage, "en");
        message.Add(IppTag.OperationAttributes, "printer-uri", IppTag.Uri, printerUri.AbsoluteUri);
        message.Add(IppTag.OperationAttributes, "requesting-user-name", IppTag.NameWithoutLanguage, "PrinterDesk");
        return message;
    }

    public IppMessage Add(IppTag group, string name, IppTag valueTag, params object[] values)
    {
        Attributes.Add(new IppAttribute(group, name, valueTag, values));
        return this;
    }

    public IppAttribute? Find(string name, IppTag? group = null) =>
        Attributes.FirstOrDefault(a => a.Name == name && (group is null || a.Group == group));

    public IReadOnlyList<string> Strings(string name) => Find(name)?.Values.OfType<string>().ToList() ?? [];

    public IReadOnlyList<int> Ints(string name) => Find(name)?.Values.OfType<int>().ToList() ?? [];

    public string? String(string name) => Strings(name).FirstOrDefault();

    public int? Int(string name) => Find(name)?.Values.OfType<int>().Select(v => (int?)v).FirstOrDefault();

    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        Span<byte> header = stackalloc byte[8];
        header[0] = 2; // IPP 2.0
        header[1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], Code);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], RequestId);
        stream.Write(header);

        IppTag? group = null;
        foreach (var attribute in Attributes)
        {
            if (attribute.Group != group)
            {
                group = attribute.Group;
                stream.WriteByte((byte)attribute.Group);
            }
            for (var i = 0; i < attribute.Values.Count; i++)
            {
                stream.WriteByte((byte)attribute.ValueTag);
                WriteBytes(stream, i == 0 ? Encoding.UTF8.GetBytes(attribute.Name) : []);
                WriteBytes(stream, EncodeValue(attribute.ValueTag, attribute.Values[i]));
            }
        }
        stream.WriteByte((byte)IppTag.End);
        return stream.ToArray();
    }

    private static byte[] EncodeValue(IppTag tag, object? value) => value switch
    {
        int number when tag is IppTag.Integer or IppTag.Enum => BigEndian(number),
        bool flag => [flag ? (byte)1 : (byte)0],
        string text => Encoding.UTF8.GetBytes(text),
        byte[] raw => raw,
        null => [],
        _ => throw new ArgumentException($"Cannot encode {value.GetType().Name} as {tag}."),
    };

    private static byte[] BigEndian(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static void WriteBytes(Stream stream, byte[] bytes)
    {
        if (bytes.Length > ushort.MaxValue)
            throw new ArgumentException("IPP value too long.");
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    /// <summary>Parses a message; throws <see cref="IppException"/> on malformed or truncated data.</summary>
    public static IppMessage Decode(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        reader.Skip(2); // version
        var message = new IppMessage { Code = reader.UInt16(), RequestId = reader.Int32() };

        var group = IppTag.OperationAttributes;
        string? name = null;
        IppTag valueTag = default;
        List<object?>? values = null;

        void Flush()
        {
            if (name is not null && values is not null)
                message.Attributes.Add(new IppAttribute(group, name, valueTag, values));
            name = null;
            values = null;
        }

        while (true)
        {
            var tag = reader.Byte();
            if (tag == (byte)IppTag.End)
                break;
            if (tag < 0x10)
            {
                Flush();
                group = (IppTag)tag;
                continue;
            }

            var attributeName = Encoding.UTF8.GetString(reader.Bytes(reader.UInt16()));
            var raw = reader.Bytes(reader.UInt16());
            object? value = tag == (byte)IppTag.BeginCollection ? SkipCollection(ref reader) : DecodeValue((IppTag)tag, raw);

            if (attributeName.Length > 0)
            {
                Flush();
                if (message.Attributes.Count >= MaxAttributes)
                    throw new IppException("The printer sent too many attributes.");
                name = attributeName;
                valueTag = (IppTag)tag;
                values = [];
            }
            else if (values is null)
            {
                throw new IppException("Malformed IPP data: value without an attribute name.");
            }
            values.Add(value);
        }
        Flush();
        return message;
    }

    /// <summary>Consumes a collection's members up to its matching end tag. Returns null (members are not kept).</summary>
    private static object? SkipCollection(ref Reader reader)
    {
        var depth = 1;
        while (depth > 0)
        {
            var tag = reader.Byte();
            reader.Skip(reader.UInt16());
            reader.Skip(reader.UInt16());
            if (tag == (byte)IppTag.BeginCollection && ++depth > MaxCollectionDepth)
                throw new IppException("Malformed IPP data: collections nested too deep.");
            if (tag == (byte)IppTag.EndCollection)
                depth--;
            else if (tag < 0x10)
                throw new IppException("Malformed IPP data: unterminated collection.");
        }
        return null;
    }

    private static object? DecodeValue(IppTag tag, ReadOnlySpan<byte> raw)
    {
        switch (tag)
        {
            case IppTag.Integer or IppTag.Enum:
                return raw.Length == 4 ? BinaryPrimitives.ReadInt32BigEndian(raw) : throw Malformed(tag);
            case IppTag.Boolean:
                return raw.Length == 1 ? raw[0] != 0 : throw Malformed(tag);
            case IppTag.RangeOfInteger:
                return raw.Length == 8
                    ? new IppRange(BinaryPrimitives.ReadInt32BigEndian(raw), BinaryPrimitives.ReadInt32BigEndian(raw[4..]))
                    : throw Malformed(tag);
            case IppTag.Resolution:
                return raw.Length == 9
                    ? new IppResolution(BinaryPrimitives.ReadInt32BigEndian(raw), BinaryPrimitives.ReadInt32BigEndian(raw[4..]), raw[8] == 3)
                    : throw Malformed(tag);
            case IppTag.TextWithLanguage or IppTag.NameWithLanguage:
                var inner = new Reader(raw);
                inner.Skip(inner.UInt16()); // language
                return Encoding.UTF8.GetString(inner.Bytes(inner.UInt16()));
            case >= (IppTag)0x10 and <= (IppTag)0x1F:
                return null; // out-of-band: unsupported, unknown, no-value
            case >= (IppTag)0x40 and <= (IppTag)0x5F:
                return Encoding.UTF8.GetString(raw);
            default:
                return raw.ToArray();
        }
    }

    private static IppException Malformed(IppTag tag) => new($"Malformed IPP data: bad {tag} value.");

    /// <summary>Bounds-checked big-endian reader.</summary>
    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || _position + count > _data.Length)
                throw new IppException("Malformed IPP data: message is truncated.");
            var slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }

        public void Skip(int count) => Bytes(count);

        public byte Byte() => Bytes(1)[0];

        public ushort UInt16() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2));

        public int Int32() => BinaryPrimitives.ReadInt32BigEndian(Bytes(4));
    }
}
