# Optional legacy game profiles

The default auto path does not need these profiles. They package the previously exercised scene bootstrap, target
selection, capture and original-handler checks. They do not infer arbitrary game
initialization. Their creation belongs to implementation/setup cost.

| Profile | Scene | Cases / selected existing targets |
|---|---|---|
| `trash-dash` | `Assets/Scenes/Shop.unity` | items: ItemsList, Coin, Premium; characters: CharacterList, TabsSwitch, StoreTitle; themes: ThemeList |
| `survivors-kit` | `Assets/RGame/RoguelikeKit/Scenes/Menus/MainMenu.unity` | character list/back/confirm, three existing power-up entries, settings group |

Both profiles use Unity 6000.3.21f1, isolated project copies, a temporary capture
camera/Canvas mode, and actual Play Mode. Trash Dash uses 540x960 and checks
600x800; Survivors uses 960x540 and checks 800x600. Clips are two seconds, 30fps.

Trash Dash initializes dedicated test save data (75000 coins, 100 premium, one
owned character/theme), then checks native virtual-currency purchases and tabs.
Its copy excludes the obsolete Asset Store editor package and the real-money IAP
package; mobile dependency assembly updating is disabled. This is a desktop UI
test, not an Android/IAP deployment test. Unity may upgrade other packages and
render-pipeline assets in the copy. The source project remains untouched.

Survivors activates original menu panels and checks original character selection,
navigation, upgrade purchase and volume control. Its adapter temporarily names
the root `GameUI` and camera `Camera` in memory; it never saves those changes to
the scene. This follows the prior benchmark integration and is not a general
requirement for another game's hierarchy.

For another uGUI project, use the default automatic discovery path described in
[automatic binding](automatic-binding.md). A custom adapter is no longer needed
for structure-based target selection, template capture or native clip binding.
Project-specific behavioral assertions remain optional additional test work;
automatic binding cannot infer a correct purchase or other game outcome.

Requirements: Python with numpy, scipy, opencv-python and Pillow; an installed
licensed Unity Editor; ffmpeg only when input consists of videos rather than
numbered PNGs. No network model calls are made by the Python helper itself.
