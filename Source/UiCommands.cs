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

        private static string SingleLine(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
