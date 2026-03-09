# Platform-Adaptive Bags

## Concept

System bags (toolbar, settings) use the same underlying Grid model across platforms but render differently based on input method. A keyboard toolbar is a 1x9 grid with number key labels. A controller toolbar might have additional slots (explicit cut/paste buttons as replacements for drag-and-drop) and use a spatial renderer that mirrors the physical controller button layout.

## Cohesion: Very High

Zero new game mechanics. The grid model is unchanged — this is purely a rendering/presentation concern. Different platforms just get different renderers for the same data. Additional controller-specific slots (cut/paste tools) are just more cells in the toolbar bag with appropriate category filters.

## Intuition: High

Players on each platform see controls that match their input device. A controller player sees tool slots arranged like their controller face buttons. A keyboard player sees numbered hotbar slots. The underlying system is identical — they're just looking at the same bag through different lenses.

## Architecture

- `IGridRenderer` strategy pattern, selected per-platform at startup
- Controller toolbar: wider grid (e.g. 1x12) with extra utility slots for actions that keyboard users do via drag-and-drop
- Renderer maps cell indices to spatial positions matching controller layout (face buttons, bumpers, triggers)
- Platform detected at startup -> selects renderer + toolbar template
- Settings bag similarly adapts: controller settings might include dead zones, stick sensitivity as unique items with numeric properties

## Methodology Fit

- **Builds on:** Meta-Bags, existing renderer architecture
- **New friction:** None for the player — this is a developer-side concern
- **Reduces friction:** Each platform feels native rather than adapted
- **Emergent potential:** Moderate. Players switching platforms discover the same bag underneath different presentations, reinforcing the metastructure insight

## Status

Proposed. Can be implemented independently of Progressive Unveiling. Relevant whenever multi-platform support begins.
