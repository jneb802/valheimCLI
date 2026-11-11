using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

namespace valheimCLI;

/// <summary>Read-only container persistence checks. These commands do not open, lock or change a container.</summary>
internal static class ContainerInspectionCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_container_fingerprint", "Compare a nearby container's saved item bytes with a fresh save of its loaded inventory: cli_container_fingerprint [radius=5]", args =>
        {
            float radius = 5f;
            if (args.Length > 1 && (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || radius <= 0 || radius > 50))
            { args.Context.AddString("ERROR: cli_container_fingerprint [radius 0-50]"); return; }
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("ERROR: No local player"); return; }
            Container? container = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(item => Vector3.Distance(item.transform.position, player.transform.position) <= radius)
                .OrderBy(item => Vector3.Distance(item.transform.position, player.transform.position)).FirstOrDefault();
            ZNetView? view = container != null ? container.GetComponent<ZNetView>() : null;
            if (container == null || view == null || !view.IsValid()) { args.Context.AddString("ERROR: No networked container within " + radius + " m"); return; }
            ZDO zdo = view.GetZDO();
            byte[] saved = zdo.GetByteArray(ZDOVars.s_items) ?? Array.Empty<byte>();
            ZPackage package = new ZPackage();
            container.GetInventory().Save(package);
            byte[] fresh = package.GetArray();
            string Hash(byte[] bytes) { using SHA256 sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(bytes)); }
            string Version(byte[] bytes) => bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0).ToString(CultureInfo.InvariantCulture) : "none";
            args.Context.AddString($"container={container.name} zdo={zdo.m_uid} owner={zdo.GetOwner()} localIsOwner={view.IsOwner()} inUse={container.IsInUse()} items={container.GetInventory().NrOfItems()} size={container.GetInventory().GetWidth()}x{container.GetInventory().GetHeight()}");
            args.Context.AddString($"saved bytes={saved.Length} version={Version(saved)} sha256={Hash(saved)}");
            args.Context.AddString($"fresh bytes={fresh.Length} version={Version(fresh)} sha256={Hash(fresh)} matches={saved.SequenceEqual(fresh)}");
            foreach (ItemDrop.ItemData item in container.GetInventory().GetAllItems())
                args.Context.AddString($"item={item.m_dropPrefab?.name ?? item.m_shared.m_name} stack={item.m_stack} grid={item.m_gridPos.x},{item.m_gridPos.y} customKeys={item.m_customData.Count}");
        }, isCheat: true);
    }
}
