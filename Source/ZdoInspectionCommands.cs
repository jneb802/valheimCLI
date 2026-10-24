using System;
using System.Globalization;
using System.Security.Cryptography;
using UnityEngine;

namespace valheimCLI;

internal static class ZdoInspectionCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_zdo_read", "Read a replicated object field: <user:id|self> <string|int|long|float|bool|vector|zdoid|bytes> <key> [base64]. Bytes default to length and SHA256; base64 explicitly exports their contents.", args =>
        {
            if (args.Length < 4 || args.Length > 5 || ZDOMan.instance == null ||
                (args.Length == 5 && (args[2] != "bytes" || args[4] != "base64")))
            { args.Context.AddString("ERROR: Expected object ID, field type, key and optional bytes base64 flag in a loaded world"); return; }
            ZDO? record;
            if (args[1] == "self") record = Player.m_localPlayer?.GetComponent<ZNetView>()?.GetZDO();
            else
            {
                string[] parts = args[1].Split(':');
                if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) ||
                    !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
                { args.Context.AddString("ERROR: Invalid object ID"); return; }
                record = ZDOMan.instance.GetZDO(new ZDOID(user, id));
            }
            if (record == null) { args.Context.AddString("ERROR: Object is not available locally"); return; }
            string key = args[3];
            object value;
            switch (args[2])
            {
                case "string": value = record.GetString(key); break;
                case "int": value = record.GetInt(key); break;
                case "long": value = record.GetLong(key); break;
                case "float": value = record.GetFloat(key); break;
                case "bool": value = record.GetBool(key); break;
                case "vector": value = record.GetVec3(key, Vector3.zero); break;
                case "zdoid": value = record.GetZDOID(key); break;
                case "bytes":
                    byte[] bytes = record.GetByteArray(key);
                    if (bytes == null) value = "missing";
                    else
                    {
                        using SHA256 sha = SHA256.Create();
                        value = "length=" + bytes.Length + " sha256=" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        if (args.Length == 5) value += " base64=" + Convert.ToBase64String(bytes);
                    }
                    break;
                default: args.Context.AddString("ERROR: Unsupported field type"); return;
            }
            string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Replace("\r", "\\r").Replace("\n", "\\n") ?? "null";
            args.Context.AddString($"ZDO id={record.m_uid} revision={record.DataRevision} owner={record.GetOwner()} type={args[2]} key={key} value={text}");
        }, isCheat: true);
    }
}
