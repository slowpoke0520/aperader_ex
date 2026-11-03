using Nodsoft.WowsReplaysUnpack.Core.Definitions;
using System;
using System.IO;
using System.Linq;
using System.Xml;

namespace ApeRadar.History
{
    // Protocol facts checked against the 110-file 15.8/15.9 definition trees in
    // Nodsoft/WoWS-ReplaysUnpack at f54abc5ea50fff11216f2f96282d902d0781df2d.
    // Apply only the seven changed files to the dependency's 15.8 definitions.
    // An upstream loader with native 15.9 support is used without this overlay.
    internal sealed class Replay159DefinitionLoader : IDefinitionLoader
    {
        internal static readonly Version Version159 = new(15, 9, 0);
        private static readonly Version Version158 = new(15, 8, 0);
        private readonly IDefinitionLoader baseLoader;
        private readonly bool applyOverlay;
        private readonly Version[] supportedVersions;

        internal Replay159DefinitionLoader(IDefinitionLoader baseLoader)
        {
            this.baseLoader = baseLoader;
            Version[] versions = baseLoader.GetSupportedVersions();
            applyOverlay = !versions.Contains(Version159) && versions.Contains(Version158);
            supportedVersions = (applyOverlay ? versions.Append(Version159) : versions)
                .Distinct().OrderByDescending(version => version).ToArray();
        }

        public Version[] GetSupportedVersions() => (Version[])supportedVersions.Clone();

        public XmlDocument GetFileAsXml(Version clientVersion, string name, params string[] directoryNames)
        {
            if (!applyOverlay || clientVersion != Version159)
                return baseLoader.GetFileAsXml(clientVersion, name, directoryNames);

            // Never mutate a document owned or cached by the underlying loader.
            XmlDocument document = (XmlDocument)baseLoader.GetFileAsXml(Version158, name, directoryNames).CloneNode(true);
            string path = string.Join("/", directoryNames.Append(name)).ToLowerInvariant();
            switch (path)
            {
                case "entity_defs/alias.xml":
                    XmlNode fields = Require(document, "/root/BUFF_STATE/Properties");
                    fields.RemoveChild(Require(document, "/root/BUFF_STATE/Properties/endTime"));
                    XmlNode start = Require(document, "/root/BUFF_STATE/Properties/startTime");
                    Require(document, "/root/BUFF_STATE/Properties/startTime/Type").InnerText = "FLOAT";
                    XmlElement duration = document.CreateElement("duration");
                    Append(duration, "Type", "FLOAT");
                    fields.InsertAfter(duration, start);
                    break;
                case "entity_defs/avatar.def":
                    XmlElement debug = Method(document, "dev_receiveDebugModifiers", ("modifierInfo", "BLOB"));
                    Append(Append(debug, "VariableLengthHeaderSize"), "WarnLevel", "none");
                    InsertAfter(document, "/root/ClientMethods/dev_receiveNavigationDebugData", debug);
                    break;
                case "entity_defs/account.def":
                    Rename(Require(document, "/root/ClientMethods/curVersion_15_8_0_13187581"), "curVersion_15_9_0_13357625");
                    InsertAfter(document, "/root/ClientMethods/receiveDeficitList",
                        Method(document, "receiveKillTagShipTotalStats", ("shipId", "SHIP_ID"), ("fragsByCategory", "MSGPACK_BLOB")));
                    XmlElement request = Method(document, "requestKillTagShipTotalStats", ("shipId", "SHIP_ID"));
                    Append(request, "Exposed");
                    InsertAfter(document, "/root/BaseMethods/onClientReady", request);
                    break;
                case "entity_defs/vehicle.def":
                    XmlElement modifiers = document.CreateElement("dev_requestDebugModifiers");
                    Append(modifiers, "Exposed");
                    Append(Append(modifiers, "Args"), "source", "INT32");
                    Append(modifiers, "Flags", "OWN_CLIENT");
                    InsertAfter(document, "/root/CellMethods/dev_setVehicleHealth", modifiers);
                    break;
                case "entity_defs/interactiveobject.def":
                case "user_data_object_defs/interactiveobjectudo.def":
                    Rename(Require(document, "/root/Properties/modelPath"), "assetPath");
                    break;
                case "entity_defs/interfaces/debugdrawentity.def":
                    Append(Append(Require(document, "/root/ClientMethods/debugExec"), "VariableLengthHeaderSize"), "WarnLevel", "none");
                    break;
            }
            return document;
        }

        private static XmlNode Require(XmlDocument document, string path) =>
            document.SelectSingleNode(path) ?? throw new InvalidDataException($"Replay 15.9 definition prerequisite is missing: {path}");

        private static XmlElement Append(XmlNode parent, string name, string? value = null)
        {
            XmlElement child = parent.OwnerDocument!.CreateElement(name);
            if (value != null) child.InnerText = value;
            parent.AppendChild(child);
            return child;
        }

        private static XmlElement Method(XmlDocument document, string name, params (string Name, string Type)[] arguments)
        {
            XmlElement method = document.CreateElement(name);
            XmlElement args = Append(method, "Args");
            foreach (var argument in arguments) Append(args, argument.Name, argument.Type);
            return method;
        }

        private static void InsertAfter(XmlDocument document, string anchorPath, XmlNode value)
        {
            XmlNode anchor = Require(document, anchorPath);
            anchor.ParentNode!.InsertAfter(value, anchor);
        }

        private static void Rename(XmlNode original, string name)
        {
            XmlElement replacement = original.OwnerDocument!.CreateElement(name);
            if (original.Attributes != null)
                foreach (XmlAttribute attribute in original.Attributes)
                    replacement.Attributes.Append((XmlAttribute)attribute.CloneNode(true));
            foreach (XmlNode child in original.ChildNodes) replacement.AppendChild(child.CloneNode(true));
            original.ParentNode!.ReplaceChild(replacement, original);
        }
    }
}
