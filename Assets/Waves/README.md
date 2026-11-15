# Configuring waves and sub-waves

Open SampleScene and select **Wave System** in the Hierarchy.

Expand **Waves → a wave → Sub Waves → a sub-wave → Enemies**. Each enemy group selects a type, count, initial spawn delay and interval between spawns. Add, remove or reorder waves, sub-waves and enemy groups with Unity's list controls.

**Same-type replacements happen automatically.** Killing one enemy immediately releases one enemy of the same type from the next sub-wave, while other enemies remain alive. For example, if sub-wave 1 has a RedNinja and a Giant, killing the RedNinja releases the RedNinja from sub-wave 2 without waiting for the Giant. Killing that new RedNinja can release one from sub-wave 3.

Each configured enemy spawns only once. Replacements consume the next sub-wave's configured count; they are never added again when that sub-wave becomes current. A kill releases at most one replacement, only from the immediately following sub-wave, and only if that type has enemies left to spawn there. It never releases enemies from the next whole wave.

Enemies without a matching predecessor, and any remaining larger counts, unlock after all enemies in the preceding sub-wave have been defeated and its scheduled spawns have finished. The displayed sub-wave is the oldest unfinished one. **Enemies Left** counts all living enemies and all enemies still to spawn in the current whole wave, including later sub-waves.

A **wave** ends only after all of its sub-waves have been defeated, with no living enemies or pending spawns. Disabled or stunned living enemies still block completion. Time alone never skips a wave.

**Wave → Delay Before Start** is the preparation time before that wave. **Sub-wave → Delay Before Start** applies to the remaining enemies after the preceding sub-wave clears. Same-type replacements bypass sub-wave and group delays; no extra Inspector setting is needed.

Wave 1 is configured as two encounters: **one LungeMan → kill it → one LungeMan**. The whole-wave counter stays on wave 1 until the second enemy is defeated. Other configured wave compositions are preserved.

**Max Concurrent Enemies** limits living enemies across all sub-waves. A released enemy waits if the arena is full or no safe spawn position is available. Spawn Points, Spawn Radius and the minimum distances control placement on the walkable arena.

Available types: LungeMan, Giant, RedNinja and TrueHealer. Their prefabs are in Assets/Waves/Enemies. Original scene enemies are retained as inactive templates.

The host controls progression and replacements in co-op; joined players receive spawned enemies, their health, the current sub-wave and wave totals. After the final wave is cleared, spawning stops.
