using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

internal static class StateInspectionCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_status_detail", "Read scalar fields and properties of a local player's named status: <status-name>", args =>
        {
            if (args.Length != 2 || Player.m_localPlayer == null)
            { args.Context.AddString("ERROR: Local player and exact status name required"); return; }
            StatusEffect? status = Player.m_localPlayer.GetSEMan().GetStatusEffect(args[1].GetStableHashCode());
            if (status == null) { args.Context.AddString("ERROR: Status is not active"); return; }
            args.Context.AddString("STATUS_DETAIL name=" + status.name + " type=" + status.GetType().FullName);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type? type = status.GetType(); type != null && typeof(StatusEffect).IsAssignableFrom(type); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(flags).Where(field => Scalar(field.FieldType)).OrderBy(field => field.Name))
                    args.Context.AddString(type.Name + "." + field.Name + "=" + Format(field.GetValue(status)));
                foreach (PropertyInfo property in type.GetProperties(flags).Where(property => Scalar(property.PropertyType) &&
                    property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null).OrderBy(property => property.Name))
                {
                    try { args.Context.AddString(type.Name + "." + property.Name + "=" + Format(property.GetValue(status))); }
                    catch (Exception exception) { args.Context.AddString(property.Name + "=<unavailable:" + exception.GetBaseException().GetType().Name + ">"); }
                }
            }
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_zdo_value", "Read one replicated field: <user:id|self> <string|int|long|float|bool|vector|zdoid> <key>. Missing fields return the game's default.", args =>
        {
            if (args.Length != 4 || ZDOMan.instance == null)
            { args.Context.AddString("ERROR: Expected object ID, type and key in a loaded world"); return; }
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
            if (record == null) { args.Context.AddString("ERROR: Object is not available on this client"); return; }
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
                default: args.Context.AddString("ERROR: Unsupported field type"); return;
            }
            args.Context.AddString($"ZDO id={record.m_uid} type={args[2]} key={key} value={Format(value)}");
        }, isCheat: true);
    }

    private static bool Scalar(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);
    private static string Format(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture)?.Replace("\r", "\\r").Replace("\n", "\\n") ?? "null";
}
