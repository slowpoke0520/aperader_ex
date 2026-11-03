using ApeRadar.History;
using Microsoft.Extensions.Logging.Abstractions;
using Nodsoft.WowsReplaysUnpack.Core.DataTypes;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using System.Xml;
using Xunit;

namespace ApeRadar.Tests;

public sealed class ReplayDefinitionStoreTests
{
    [Theory]
    [InlineData("UINT8")]
    [InlineData("SHIP_LEVEL")]
    public void NumericBytes_PreserveEveryValueInsteadOfBecomingBoolean(string name)
    {
        DataTypeBase type = CreateStore().GetDataType(new Version(15, 9, 0), Node($"<Type>{name}</Type>"));
        Assert.Equal(typeof(byte), type.ClrType);
        using BinaryReader reader = new(new MemoryStream(Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()));
        for (int i = 0; i < 256; i++) Assert.Equal((byte)i, Assert.IsType<byte>(type.GetValue(reader, null)));
        Assert.Equal(256, reader.BaseStream.Position);
        Assert.Throws<EndOfStreamException>(() => type.GetValue(reader, null));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void BoolAlias_RetainsBooleanSemantics(byte value, bool expected)
    {
        DataTypeBase type = CreateStore().GetDataType(new Version(15, 9, 0), Node("<Type>BOOL</Type>"));
        Assert.Equal(typeof(bool), type.ClrType);
        using BinaryReader reader = new(new MemoryStream(new[] { value }));
        Assert.Equal(expected, Assert.IsType<bool>(type.GetValue(reader, null)));
    }

    [Theory]
    [InlineData("255", 255)]
    [InlineData("0", 0)]
    [InlineData("true", 1)]
    [InlineData("false", 0)]
    public void NumericDefaults_AreBytesForPropertiesAndArrayElements(string value, byte expected)
    {
        DataTypeBase type = CreateStore().GetDataType(new Version(15, 9, 0), Node("<Type>UINT8</Type>"));
        Assert.Equal(expected, Assert.IsType<byte>(type.GetDefaultValue(Node($"<Property><Default>{value}</Default></Property>"))));
        Assert.Equal(expected, Assert.IsType<byte>(type.GetDefaultValue(Node($"<Value>{value}</Value>"), true)));
        Assert.Equal((byte)0, Assert.IsType<byte>(type.GetDefaultValue(null)));
    }

    [Theory]
    [InlineData("B0C7A9C6020100000000000080BF01", 3333015472u, 0f, -1f)]
    [InlineData("B0C719C70201C09A61480000F04101", 3340355504u, 231019f, 30f)]
    public void Recorded159Buffs_ConsumeExactlyOneRecordWithNumericSubtype(string hex, uint id, float start, float duration)
    {
        DataTypeBase type = CreateStore().GetDataType(new Version(15, 9, 0), Node("<Type>BUFF_STATE</Type>"));
        Assert.Equal(15, type.DataSize);
        using BinaryReader reader = new(new MemoryStream(Convert.FromHexString(hex)));
        FixedDictionary record = Assert.IsType<FixedDictionary>(type.GetValue(reader, null));
        Assert.Equal(id, record["paramsId"]);
        Assert.Equal((byte)2, record["entitySubtypeId"]);
        Assert.Equal(start, record["startTime"]);
        Assert.Equal(duration, record["duration"]);
        Assert.Equal((byte)1, record["source"]);
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
    }

    [Fact]
    public void Legacy158Buffs_KeepTheirOriginalElevenByteLayout()
    {
        ReplayDefinitionStore store = CreateStore();
        _ = store.GetDataType(new Version(15, 9, 0), Node("<Type>BUFF_STATE</Type>"));
        DataTypeBase type = store.GetDataType(new Version(15, 8, 0), Node("<Type>BUFF_STATE</Type>"));
        Assert.Equal(11, type.DataSize);
        using BinaryReader reader = new(new MemoryStream(Convert.FromHexString("B0C7A9C60201B004FFFF01")));
        FixedDictionary record = Assert.IsType<FixedDictionary>(type.GetValue(reader, null));
        Assert.Equal((short)1200, record["endTime"]);
        Assert.Equal((short)-1, record["startTime"]);
        Assert.Equal((byte)2, record["entitySubtypeId"]);
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
    }

    private static ReplayDefinitionStore CreateStore() => new(NullLogger<DefaultDefinitionStore>.Instance, new AssemblyDefinitionLoader());
    private static XmlElement Node(string xml)
    {
        XmlDocument document = new();
        document.LoadXml(xml);
        return document.DocumentElement!;
    }
}
