using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace valheimCLI
{
    internal static class UiCommands
    {
        public static bool TryExecute(string command, Action<string> output)
        {
            string[] parts = command.Split(new[] { ' ' }, 2);
            if (parts[0] == "cli_ui_text")
            {
                string filter = parts.Length == 2 ? parts[1] : string.Empty;
                foreach (TMP_Text label in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (label.gameObject.activeInHierarchy && label.text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        output($"name={label.name} text={SingleLine(label.text)}");
                    }
                }
                foreach (Text label in Resources.FindObjectsOfTypeAll<Text>())
                {
                    if (label.gameObject.activeInHierarchy && label.text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        output($"name={label.name} text={SingleLine(label.text)}");
                    }
                }
                output("OK: Active UI text listed");
                return true;
            }
            if (parts[0] == "cli_ui_tree")
            {
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                {
                    output("ERROR: Usage: cli_ui_tree <object-name> [max-depth]");
                    return true;
                }
                string rootName = parts[1].Trim();
                int maxDepth = 6;
                int split = rootName.LastIndexOf(' ');
                if (split > 0 && int.TryParse(rootName.Substring(split + 1), out int depth))
                {
                    maxDepth = depth;
                    rootName = rootName.Substring(0, split);
                }
                RectTransform[] roots = Resources.FindObjectsOfTypeAll<RectTransform>()
                    .Where(rect => rect.gameObject.activeInHierarchy && rect.name == rootName).ToArray();
                foreach (RectTransform root in roots)
                {
                    WriteTree(root, 0, maxDepth, output);
                }
                output($"OK: {roots.Length} active UI roots named '{rootName}'");
                return true;
            }
            if (parts[0] != "cli_ui_buttons" && parts[0] != "cli_ui_click")
            {
                return false;
            }

            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>()
                .Where(button => button.gameObject.activeInHierarchy)
                .OrderBy(button => button.GetInstanceID()).ToArray();
            if (parts[0] == "cli_ui_buttons")
            {
                foreach (Button button in buttons)
                {
                    string text = string.Join(" | ", button.GetComponentsInChildren<TMP_Text>()
                        .Select(label => label.text).Concat(button.GetComponentsInChildren<Text>().Select(label => label.text)));
                    output($"id={button.GetInstanceID()} interactable={button.IsInteractable()} name={button.name} text={SingleLine(text)}");
                }
                output($"OK: {buttons.Length} active buttons");
                return true;
            }

            if (parts.Length != 2 || !int.TryParse(parts[1], out int id))
            {
                output("ERROR: Usage: cli_ui_click <instance-id from cli_ui_buttons>");
                return true;
            }
            Button? target = buttons.SingleOrDefault(button => button.GetInstanceID() == id);
            if (target == null || !target.IsInteractable())
            {
                output("ERROR: Button is absent, inactive, or not interactable. Run cli_ui_buttons again.");
                return true;
            }
            string name = target.name;
            target.onClick.Invoke();
            output($"OK: Invoked button {id} ({name})");
            return true;
        }

        // One line per node: active state, rect size and screen position, and the components that decide
        // what is drawn (text, image, button state). Inactive children are listed so hidden layers are visible.
        private static void WriteTree(Transform node, int depth, int maxDepth, Action<string> output)
        {
            string indent = new string(' ', depth * 2);
            string line = $"{indent}{node.name} active={node.gameObject.activeSelf}";
            if (node is RectTransform rect)
            {
                Vector3[] corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                line += FormattableString.Invariant($" size={rect.rect.width:F0}x{rect.rect.height:F0} screen=({corners[0].x:F0},{corners[0].y:F0})-({corners[2].x:F0},{corners[2].y:F0})");
            }
            foreach (TMP_Text label in node.GetComponents<TMP_Text>())
            {
                line += FormattableString.Invariant($" tmp='{SingleLine(label.text)}' font={(label.font != null ? label.font.name : "none")} size={label.fontSize:F0} auto={label.enableAutoSizing} color={ColorText(label.color)} alpha={label.alpha:F2} enabled={label.enabled}");
            }
            foreach (Text label in node.GetComponents<Text>())
            {
                line += FormattableString.Invariant($" text='{SingleLine(label.text)}' size={label.fontSize} color={ColorText(label.color)} enabled={label.enabled}");
            }
            foreach (Image image in node.GetComponents<Image>())
            {
                line += $" image={(image.sprite != null ? image.sprite.name : "none")} color={ColorText(image.color)} enabled={image.enabled}";
            }
            foreach (Selectable selectable in node.GetComponents<Selectable>())
            {
                line += $" {selectable.GetType().Name} interactable={selectable.IsInteractable()} transition={selectable.transition}";
            }
            CanvasGroup group = node.GetComponent<CanvasGroup>();
            if (group != null)
            {
                line += FormattableString.Invariant($" canvasGroupAlpha={group.alpha:F2}");
            }
            output(line);
            if (depth >= maxDepth)
            {
                return;
            }
            foreach (Transform child in node)
            {
                WriteTree(child, depth + 1, maxDepth, output);
            }
        }

        private static string ColorText(Color color) =>
            FormattableString.Invariant($"{color.r:F2},{color.g:F2},{color.b:F2},{color.a:F2}");

        private static string SingleLine(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
