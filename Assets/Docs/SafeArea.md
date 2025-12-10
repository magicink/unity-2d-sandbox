# SafeArea helper — how to use

This project contains a simple SafeArea component at `Assets/Scripts/UI/SafeArea.cs`.

Why use it
- Mobile devices can have display cutouts (notches), rounded corners, or OS UI that reduces usable screen area.
- `Screen.safeArea` reports the region guaranteed to be unobstructed — use it to avoid placing important UI too close to device edges.

How to use (Canvas / UI)
1. Create an empty GameObject inside your Canvas (or use an existing top-level panel). This should be the container for your top-left UI.
2. Add the `SafeArea` component to that container (it requires a RectTransform).
3. Keep the container's child elements (e.g., your ammo counter Text) anchored/layouted on the top-left so they stay inside the safe area.

Fallback OnGUI support
- `Assets/Scripts/PhysicsGame/PhysicsGameUi.cs` now uses `Screen.safeArea` when drawing the OnGUI fallback text. It will render in the top-left of the device safe area with a small padding.

Per-element fitting
- If you prefer to leave the scene layout as-is (no container changes), you can attach the `SafeAreaFitter` component to an individual UI element (for example the ammo Text). This will nudge that element's anchored position to keep it inside the safe area.
Note: `PhysicsGameUi` used to auto-attach `SafeAreaFitter` at runtime by default, but to avoid unexpected scene layout changes this behavior is now disabled by default. To opt in at runtime, enable `autoApplySafeAreaToAmmoText` on `PhysicsGameUi`.

HUD font scaling
- `PhysicsGameUi` now supports automatic HUD font scaling. When enabled (default) the `ammoText` font size will scale proportionally with the device screen height so it looks larger on bigger screens and smaller on small devices.
- The following serialized fields control the behavior on `PhysicsGameUi`:
	- `autoScaleHudFont` (bool): enable/disable scaling
	- `hudBaseFontSize` (int): base font size used when screen height equals `referenceScreenHeight`
	- `referenceScreenHeight` (float): reference height in pixels (default 800)
	- `hudMinFontSize` / `hudMaxFontSize` (int): clamps for font size

Note: The OnGUI fallback also uses the same scaling, so the Canvas text and fallback text should match visually.

Background behind HUD text
- `PhysicsGameUi` can optionally add a subtle black background behind the `ammoText` to improve readability on busy backgrounds. This is controlled by these fields on `PhysicsGameUi`:
	- `addBackgroundToAmmoText` (bool): toggle background creation (default: enabled)
	- `ammoTextBackgroundPadding` (Vector2): horizontal/vertical padding in pixels
	- `ammoTextBackgroundColor` (Color): RGBA color of the background image

	Notes about background creation
	- The `TextBackground` helper now creates the background as a sibling GameObject placed directly behind the Text in the Canvas hierarchy. This avoids the background being drawn "on top" of the text (which can happen when the background is a child of the same GameObject).
	- The auto-created background is persistent (saved to the scene) so you can further tweak it in the editor. If you prefer transient runtime-only backgrounds, disable `autoCreateBackground` on the `TextBackground` component.

	For more predictable and designer-driven results, you can also add a `TextBackground` component manually to your `AmmoText` GameObject or create a background Image in the scene and size it to taste.

	Runtime fallback note
	- Older Unity versions and certain editor setups may not expose the legacy built-in sprite `UI/Skin/UISprite.psd` via Resources.GetBuiltinResource. The `TextBackground` helper now falls back to creating a tiny 1x1 runtime sprite so the background will always appear even when the builtin resource cannot be loaded.

Recovery guard
- `PhysicsGameUi` now includes a runtime recovery guard `ensureAmmoTextVisible` (enabled by default). If a runtime helper or previous setup moved or disabled your `AmmoText`, this option will try to restore a readable font size, color and a reasonable anchored position at Start. If your HUD still does not appear, check the GameObject is active, the Text component is enabled, and no other scripts are disabling it during runtime.

Notes
- The `SafeArea` component can stretch a RectTransform to exactly match the safe area. If you want the container to only *apply offset* instead of stretching, change the `stretchToSafeArea` field in the inspector (or modify the script).
- SafeArea runs in the Editor and Play mode; it detects changes to the safe area (resolution/rotation) and will update automatically.
