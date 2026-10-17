# Crossbow validation commands

These commands support live tests through normal game actions:

| Command | Purpose |
| --- | --- |
| `cli_use_item <prefab-or-display-name>` | Request normal inventory item use, including ammo selection. Reports the request, not whether native equipment handling accepted it. |
| `cli_hold_secondary_attack [seconds]` | Hold special attack through player controls. Uses the same controller suspension and restoration as primary attack. |
| `cli_reload_state` | Report selected ammo, loaded state, reload action time and duration, recoil, body mass, and current attack damage multiplier. |
| `cli_grapple_state` | Report rope attachment, anchor, equipped-item requirement, and player velocity. |
| `cli_item_attack_state <prefab>` | Inspect primary and secondary attack settings, projectile behavior, and rope settings. |
| `cli_apply_magic_item_json <item> <base64-json>` | Apply complete Epic Loot magic-item data for test setup, including sockets and set IDs. Requires Epic Loot; reports an error when unavailable. |

Item use, special attack, and magic-item setup require developer commands.
State inspection does not change the result under test. `cli_aim_at` now sets
the player look pitch as well as direction, so a target above or below the player
remains aimed at after the next native look update.

Live validation used Valnet client 01 with the Praetoris Season 8 production
mod set. It exercised ammo choices during crossbow firing, reload timing and
damage multipliers, complete set and socket setup, and special-attack controls.
