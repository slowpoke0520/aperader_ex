using Microsoft.Extensions.Logging;
using Nodsoft.WowsReplaysUnpack.Core.DataTypes;
using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace ApeRadar.History
{
    internal sealed class ReplayDefinitionStore : DefaultDefinitionStore
    {
        public ReplayDefinitionStore(ILogger<DefaultDefinitionStore> logger, IDefinitionLoader loader)
            : base(logger, new Replay159DefinitionLoader(loader)) { }

        protected override DataTypeBase GetDataTypeInternal(Version version, Dictionary<string, XmlNode> aliases, XmlNode node)
        {
            HashSet<string> visited = new(StringComparer.Ordinal);
            XmlNode current = node;
            while (true)
            {
                string name = current.ChildNodes.Cast<XmlNode>().First(n => n.NodeType == XmlNodeType.Text).Value!.Trim();
                // Nodsoft 3.0.45-beta represents both UINT8 and the BOOL alias as
                // bool. Preserve BOOL, but never collapse numeric bytes (e.g.
                // ship levels or vehicle subtypes) into false.
                if (name == "BOOL") return base.GetDataTypeInternal(version, aliases, current);
                if (!visited.Add(name)) throw new InvalidDataException("Replay data type aliases contain a cycle.");
                if (aliases.TryGetValue(name, out XmlNode? mapped))
                {
                    current = mapped;
                    continue;
                }
                return name == "UINT8"
                    ? new ReplayUnsignedByteDataType(version, this, current)
                    : base.GetDataTypeInternal(version, aliases, current);
            }
        }
    }

    internal sealed class ReplayUnsignedByteDataType : DataTypeBase
    {
        public ReplayUnsignedByteDataType(Version version, IDefinitionStore store, XmlNode node)
            : base(version, store, node, typeof(byte)) { }

        public override int DataSize => 1;
        protected override object GetValueInternal(BinaryReader reader, XmlNode? node, int headerSize) => reader.ReadByte();

        public override object GetDefaultValue(XmlNode? node, bool forArray = false)
        {
            string? text = (forArray ? node?.InnerText : node?.SelectSingleNode("Default")?.InnerText)?.Trim();
            if (string.IsNullOrEmpty(text) || text.Equals("false", StringComparison.OrdinalIgnoreCase)) return (byte)0;
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase)) return (byte)1;
            return byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
