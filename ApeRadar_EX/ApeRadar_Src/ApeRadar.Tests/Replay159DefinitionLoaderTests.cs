using ApeRadar.History;
using Microsoft.Extensions.Logging.Abstractions;
using Nodsoft.WowsReplaysUnpack.Core.DataTypes;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using Nodsoft.WowsReplaysUnpack.Core.Models;
using System.Xml;
using Xunit;

namespace ApeRadar.Tests;

public sealed class Replay159DefinitionLoaderTests
{
    private static readonly Version OldVersion = new(15, 8, 0);
    private static readonly Version NewVersion = new(15, 9, 0);

    [Fact]
    public void AdvertisedVersions_Include159InDescendingOrderWithoutChanging158()
    {
        AssemblyDefinitionLoader baseline = new();
        Replay159DefinitionLoader loader = new(baseline);
        Version[] versions = loader.GetSupportedVersions();
        Assert.Equal(NewVersion, versions[0]);
        Assert.Contains(OldVersion, versions);
        Assert.Equal(versions.OrderByDescending(version => version).Distinct(), versions);
        versions[0] = new Version(99, 0, 0);
        Assert.Equal(NewVersion, loader.GetSupportedVersions()[0]);
        Assert.Equal(baseline.GetFileAsXml(OldVersion, "alias.xml", "entity_defs").OuterXml,
            loader.GetFileAsXml(OldVersion, "alias.xml", "entity_defs").OuterXml);
    }

    [Fact]
    public void BuffSchema_ChangesElevenByteElementsToFifteenByteElementsOnlyFor159()
    {
        Replay159DefinitionLoader loader = new(new AssemblyDefinitionLoader());
        DefaultDefinitionStore store = new(NullLogger<DefaultDefinitionStore>.Instance, loader);
        XmlDocument type = new();
        type.LoadXml("<Type>BUFF_STATE</Type>");
        FixedDictDataType oldType = Assert.IsType<FixedDictDataType>(store.GetDataType(OldVersion, type.DocumentElement!));
        FixedDictDataType newType = Assert.IsType<FixedDictDataType>(store.GetDataType(NewVersion, type.DocumentElement!));
        Assert.Equal(11, oldType.DataSize);
        Assert.Equal(15, newType.DataSize);
        Assert.Equal(new[] { "paramsId", "entitySubtypeId", "level", "startTime", "duration", "source" }, newType.PropertyTypes.Keys);
        Assert.Contains("endTime", oldType.PropertyTypes.Keys);
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(123u); writer.Write((byte)2); writer.Write((sbyte)1);
            writer.Write(12.5f); writer.Write(-1f); writer.Write((byte)1);
        }
        stream.Position = 0;
        using BinaryReader reader = new(stream);
        FixedDictionary result = Assert.IsType<FixedDictionary>(newType.GetValue(reader, null));
        Assert.Equal(12.5f, result["startTime"]);
        Assert.Equal(-1f, result["duration"]);
        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal(11, Assert.IsType<FixedDictDataType>(store.GetDataType(OldVersion, type.DocumentElement!)).DataSize);
    }

    [Fact]
    public void AvatarClientMethod_KeepsSchemaOrderAndChangesTheSortedMethodTable()
    {
        Replay159DefinitionLoader loader = new(new AssemblyDefinitionLoader());
        XmlDocument document = loader.GetFileAsXml(NewVersion, "Avatar.def", "entity_defs");
        XmlNode added = document.SelectSingleNode("/root/ClientMethods/dev_receiveDebugModifiers")!;
        Assert.NotNull(added);
        Assert.Equal("dev_receiveNavigationDebugData", added.PreviousSibling!.Name);
        Assert.Equal("updateBuoyancyRudderCruise", added.NextSibling!.Name);
        Assert.Equal("BLOB", added.SelectSingleNode("Args/modifierInfo")!.InnerText);
        DefaultDefinitionStore store = new(NullLogger<DefaultDefinitionStore>.Instance, loader);
        EntityDefinition oldDefinition = store.GetEntityDefinition(OldVersion, "Avatar");
        EntityDefinition newDefinition = store.GetEntityDefinition(NewVersion, "Avatar");
        Assert.Equal(oldDefinition.ClientMethods.Count + 1, newDefinition.ClientMethods.Count);
        int insertion = newDefinition.ClientMethods.FindIndex(method => method.Name == "dev_receiveDebugModifiers");
        Assert.True(insertion >= 0);
        Assert.Single(newDefinition.ClientMethods[insertion].Arguments);
        Assert.Equal(oldDefinition.ClientMethods.Select(method => method.Name),
            newDefinition.ClientMethods.Where(method => method.Name != "dev_receiveDebugModifiers").Select(method => method.Name));
    }

    [Theory]
    [InlineData("Account.def", "entity_defs", "/root/ClientMethods/curVersion_15_9_0_13357625")]
    [InlineData("Account.def", "entity_defs", "/root/ClientMethods/receiveKillTagShipTotalStats/Args/fragsByCategory")]
    [InlineData("Account.def", "entity_defs", "/root/BaseMethods/requestKillTagShipTotalStats/Exposed")]
    [InlineData("Vehicle.def", "entity_defs", "/root/CellMethods/dev_requestDebugModifiers/Args/source")]
    [InlineData("InteractiveObject.def", "entity_defs", "/root/Properties/assetPath")]
    [InlineData("InteractiveObjectUDO.def", "user_data_object_defs", "/root/Properties/assetPath")]
    public void OtherChangedDefinitions_AreAppliedAtTheExpectedLocation(string file, string directory, string path)
    {
        Replay159DefinitionLoader loader = new(new AssemblyDefinitionLoader());
        Assert.NotNull(loader.GetFileAsXml(NewVersion, file, directory).SelectSingleNode(path));
        Assert.Null(loader.GetFileAsXml(OldVersion, file, directory).SelectSingleNode(path));
    }

    [Fact]
    public void InterfaceMetadata_UsesTheSameVariableLengthHeaderSize()
    {
        Replay159DefinitionLoader loader = new(new AssemblyDefinitionLoader());
        XmlDocument document = loader.GetFileAsXml(NewVersion, "DebugDrawEntity.def", "entity_defs", "interfaces");
        Assert.Equal("none", document.SelectSingleNode("/root/ClientMethods/debugExec/VariableLengthHeaderSize/WarnLevel")!.InnerText);
    }

    [Fact]
    public void CachedUnderlyingDocument_IsNotMutatedOrSharedAcrossOverlayCalls()
    {
        XmlDocument document = new AssemblyDefinitionLoader().GetFileAsXml(OldVersion, "alias.xml", "entity_defs");
        string original = document.OuterXml;
        TrackingLoader baseline = new(new[] { OldVersion }, document);
        Replay159DefinitionLoader loader = new(baseline);
        XmlDocument first = loader.GetFileAsXml(NewVersion, "alias.xml", "entity_defs");
        XmlDocument second = loader.GetFileAsXml(NewVersion, "alias.xml", "entity_defs");
        Assert.NotSame(document, first);
        Assert.NotSame(first, second);
        Assert.Equal(original, document.OuterXml);
        Assert.Equal(first.OuterXml, second.OuterXml);
        Assert.All(baseline.RequestedVersions, version => Assert.Equal(OldVersion, version));
    }

    [Fact]
    public void Native159Support_IsDelegatedWithoutApplyingOrReorderingTheOverlay()
    {
        XmlDocument native = new(); native.LoadXml("<root><native/></root>");
        TrackingLoader baseline = new(new[] { NewVersion, OldVersion }, native);
        Replay159DefinitionLoader loader = new(baseline);
        Assert.Same(native, loader.GetFileAsXml(NewVersion, "alias.xml", "entity_defs"));
        Assert.Equal(NewVersion, Assert.Single(baseline.RequestedVersions));
    }

    [Fact]
    public void Missing158Baseline_DoesNotAdvertiseUnsupported159()
    {
        TrackingLoader baseline = new(new[] { new Version(15, 7, 0) }, new XmlDocument());
        Replay159DefinitionLoader loader = new(baseline);
        Assert.DoesNotContain(NewVersion, loader.GetSupportedVersions());
    }

    [Fact]
    public void UnrelatedVersions_ArePassedToTheUnderlyingLoaderUnchanged()
    {
        TrackingLoader baseline = new(new[] { OldVersion }, new XmlDocument());
        Replay159DefinitionLoader loader = new(baseline);
        Version unknown = new(15, 10, 0);
        loader.GetFileAsXml(unknown, "Avatar.def", "entity_defs");
        Assert.Equal(unknown, Assert.Single(baseline.RequestedVersions));
    }

    [Fact]
    public void ChangedBaselineShape_FailsExplicitlyInsteadOfCreatingAPartialDefinition()
    {
        XmlDocument document = new(); document.LoadXml("<root/>");
        Replay159DefinitionLoader loader = new(new TrackingLoader(new[] { OldVersion }, document));
        Assert.Throws<InvalidDataException>(() => loader.GetFileAsXml(NewVersion, "alias.xml", "entity_defs"));
    }

    private sealed class TrackingLoader(Version[] versions, XmlDocument document) : IDefinitionLoader
    {
        internal List<Version> RequestedVersions { get; } = new();
        public Version[] GetSupportedVersions() => versions;
        public XmlDocument GetFileAsXml(Version version, string name, params string[] directoryNames)
        {
            RequestedVersions.Add(version);
            return document;
        }
    }
}
