# Gigante Rei
The boss is registered as GiantKing in the wave system and appears in the final wave of SampleScene. Existing waves are preserved.

Edit Assets/Waves/Enemies/GiantKing.prefab to tune the boss in the Inspector. The inactive scene model is a retained template. Boss health defaults to 1200, and rage starts at 50% health.

Phase one: pursue the closest living player; 10-metre fixed-heading sprint over 3 seconds; one jump with an 11-metre landing square; 7–10 staggered pillar warnings.
Phase two: 20-metre sprint; two consecutive jumps; 14–20 pillars; 1.5x damage, shorter cooldowns and faster pillar warnings.

Default damage: entrance 25, sprint 12, jump 18, pillar 15. Pillars deal damage only as they emerge, once per pillar per player. Sprint contact is limited to one hit per player per sprint. Dash avoids all attacks and uses existing perfect-dodge feedback.

The entrance warns in red for 2.5 seconds over the arena interior, then turns white for 0.3 seconds before dealing damage. Entrance Dodge Cue Duration controls this additional reaction window in the boss prefab. A 4.5-metre strip along the movement boundary remains safe. Warning timing, safe-edge width, damage, movement, distances and phase-two multipliers are serialized.

The two unspecified animations use existing clips: Sword_Heavy_Combo for stones and Spell_Simple_Enter for rage. Both can be replaced through the boss prefab. Sprint and jump use the same animation sources and VFX helpers as the regular Giant.

The boss spawns at the center of the playable arena. Its model stays hidden during the red and white entrance warnings, then appears when the initial impact resolves. The animator stays active while the renderers are hidden, and co-op replicas derive visibility from the synchronized phase. The boss health bar appears at spawn and hides on death/despawn. Hit flashes and floating damage numbers reuse existing combat feedback. Host authority synchronizes health, phases, positions, locked attacks and deterministic pillar layouts; joined players receive their damage and dodge results through existing co-op snapshots.

Visual helpers and the playable graph clean up when the boss despawns.
