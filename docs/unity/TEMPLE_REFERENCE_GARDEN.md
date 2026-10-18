# Temple reference garden visual pass

The supplied garden references guide the ivory plaster, slate roof
trim, warm timber, sculptural rocks, planted banks and blue-green pond. The
character, interface and watermark are excluded. This is a stylized adaptation
of the existing garden, not an exact reconstruction of the reference game.

## Changes and ownership

- Existing scene-owned materials under `Art/TemplePond/Materials` use a quieter
  palette and lower surface gloss. Timber reuses the existing 1K wood texture.
- The follow-up revision removes the green bias from ambient fill, plaster,
  stone and rock materials. Green remains localized to vegetation. It replaces
  the six detached teal peaks with one connected, broad mountain and places
  shrubs on authored paths along both pond borders, the rear terrace edges,
  and around the four trees on planted ground.
- The pond keeps its existing shader and `_RippleMotion` adapter binding. Its
  base color, opacity, normal strength and smoothness are artistic adjustments;
  the normalized action space, approved motion range and transitions are intact.
- `Art/TemplePond/ReferenceGarden/TempleReferenceGarden.prefab` contains static
  shrubs, lotus clusters, sculptural rocks, one mountain, seven stylized trees,
  a meditation cabin, a double doorway, hanging wall foliage and four lanterns.
  Lantern paper uses an unlit material; no new lights or audio are added.
- Scene ambient fill and directional-light shadow strength soften contrast.
  Camera pose, XR setup, session controls, adaptive mappings and birds remain.
- The five original tree instances are inactive in this scene; their FBX assets
  and metadata remain available. Replacement trees have curved trunks, exposed
  branches, layered foliage cores and smaller opaque leaf silhouettes.
- The tree behind the seat is removed. A curved-roof timber cabin occupies the
  existing platform, with an open front, rear screen and low side screens. The
  camera stays at its original pose inside the cabin; roof geometry starts above
  y=5.7, with the seated camera at y=3.6486. The existing stairs and floor remain.
- A low-poly closed double door with timber lattice panels covers the blank
  rear wall inside the temple entrance. It is static decoration, not an
  interactive door or a new locomotion route.
- Leaf silhouettes per canopy increase from 76 to 144, with a third foliage
  core for fullness. Four trees remain inside the garden; three larger trees
  stand beyond the left wall, right wall and temple rear wall respectively.
  All seven trees share four combined mesh/material groups, with no alpha blend.
- Walls now use `Textures/Wall/T_TempleWeatheredPlaster.png`: cool-grey worn
  plaster over exposed grey stone courses, generated from the supplied visual
  direction. The wall shader projects a single albedo sample in world units so
  courses stay level across differently rotated/scaled FBXs. Taller facades
  repeat only the plaster cap. The old plain footing overlay boxes are removed.
- Eight small patches of opaque hanging foliage decorate the two side walls
  and the walls beside the temple. They use two combined leaf groups and the
  existing branch material for stems, with no motion, transparency or shadows.
- `Editor/TempleReferenceGardenBuilder.cs` reproduces the decorative prefab and
  native mesh/material assets with a fixed seed. Its menu rebuilds the assets;
  it does not add another scene instance. Generated assets retain their GUIDs
  when rebuilt. These meshes have no runtime generator or per-frame work.
- `Art/TemplePond/Shaders/TempleMountain.shader` uses vertex colors and simple
  vertex lighting for rock variation without texture maps. It responds to the
  existing directional light and fog, includes stereo instancing macros, and
  adds no shadow or reflection pass.

## Quest-oriented choices

The source FBX audit found small architectural meshes and rocks: the existing
rock assets are 268 triangles each and the largest tree asset is 6,734 triangles.
The original models are retained on disk; this scene renders the lighter tree
replacement instead. The revised decoration, trees and architecture contain
44,786 triangles in 23 combined mesh/material groups. The cabin and doorway
add 1,784 triangles and four groups; they require no new texture maps. The asset
test's content budget is now 45,000 triangles to cover the additional requested
trees and architecture. This is an authoring regression limit, not a headset
frame-time budget or a change to a research safety parameter. Existing imported
static garden geometry is marked for Unity static batching. This is not a
measured draw-call or frame-rate claim; actual batching depends on the build.

New shrub, tree and flower geometry uses opaque materials, with two-sided leaf
surfaces where needed. It introduces no alpha blending, realtime shadows,
colliders, rigidbodies, animations or runtime behaviours. The existing single
transparent pond surface remains. There is no new reflection camera, screen
capture or post-processing effect. The wall finish adds one albedo texture,
without normal, height, metallic or occlusion maps.

Temple texture Android overrides cap leaves at 512 and other existing Temple
textures at 1024. Leaves use ASTC 4x4 to preserve alpha edges; other textures use
ASTC 6x6. Mipmaps stay enabled and leaf coverage is preserved at the material's
alpha cutoff. Source images and existing `.meta` GUIDs are retained; Read/Write
remains disabled. These are quality/storage choices, not evidence of an FPS gain.
The generated wall texture is also capped at 1024, uses Android ASTC 6x6,
trilinear mipmaps, horizontal repeat and vertical clamp, with Read/Write disabled.
`Shaders/TempleWeatheredWall.shader` has one texture sample, ambient plus main
directional diffuse lighting, fog, and URP shadow/depth passes. Its shared
material buffer and stereo macros cover the forward and depth paths; this still
requires device validation. Existing light/color/fog adaptation remains intact.

See Unity's [URP performance guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/configure-for-better-performance.html)
and [texture platform settings](https://docs.unity3d.com/6000.0/Documentation/Manual/class-TextureImporterOverride.html).

## Validation and headset handoff

The visual pass is previewed from the existing seated camera in an isolated
Unity 6000.0.82f1 project using the same assets and local package versions.
Preview images and execution logs are local development artifacts under
`.codex_tmp/temple-visual-preview`. Desktop preview rendering does not validate
stereo rendering or Quest 2 GPU performance.

`TempleReferenceGardenAssetTests` checks the decorative mesh budget, finite
vertices, material references and absence of new lights, physics or runtime
scripts. It also checks that the mountain is one connected surface with vertex
colors, and that tree leaves stay outside the authored seating clearance.
Cabin headroom, roof coverage over the seat, doorway placement, the three
exterior tree bases and removal of the seat tree are also checked.
Wall checks cover texture binding, size, compression, mipmaps, opaque material,
shader errors, retained shadow/depth passes and hanging-leaf placement.
`TemplePondSkyMotionPlayModeTests` covers the earlier sky/bird addition.

Validation performed for this pass:

- Unity 6000.0.82f1 compiled the project and rendered the revised scene in the
  isolated preview project, including seated, top-down and tree close-up views.
  Active scene MeshFilters total 66,624 triangles, excluding Terrain, compared
  with 63,896 before the wall pass and 73,322 before the earlier tree replacement.
  This is a geometry count, not FPS. Additional renders inspect the cabin, door
  and wall finish; `wall-preview.png` shows the masonry and hanging foliage.
- All 19 Edit Mode tests passed: eight garden/wall asset and placement checks
  and eleven existing Temple mapping checks. The result is recorded in
  `.codex_tmp/temple-visual-preview/wall-editmode-results.xml`.
- 10 sky/bird Play Mode tests passed during the earlier motion pass. The motion
  scripts and bindings are unchanged by this revision. That result is in
  `.codex_tmp/temple-visual-preview/playmode-results.xml`.
- New asset metadata, GUID uniqueness, scene file IDs, the single decorative
  prefab instance and the existing sky-motion binding were checked.
- No Android player build, Quest stereo check or device performance capture was
  performed. This is a visually reviewed implementation, not a device-certified
  performance result.

1. Let Unity import the changed assets. Reload `JapaneseTemplePondGarden` after
   saving any unrelated Editor work. The decoration is already wired in.
2. Inspect from the existing seated camera and enter Play Mode. Check both eyes
   on Quest 2, particularly tree silhouettes, the mountain shader in both eyes,
   leaf edges, the pond, lotus silhouettes and lanterns. Look up and sideways
   from inside the cabin to check roof and screen clearance in the headset.
   Inspect the new wall texture obliquely for mip shimmer and compression quality.
3. Test all five adaptive dimensions across their existing approved ranges,
   including pause/resume and emergency stop. Verify readable foreground edges
   and comfortable contrast at both light-intensity endpoints.
4. Profile a device development build at the study's chosen refresh rate. Record
   CPU/GPU frame times, draw calls, overdraw, texture memory and thermal behaviour
   using the existing Quest validation protocol. Inspect texture compression at
   headset resolution before freezing the study assets.

TODO(RESEARCH_DECISION): The revised palette, ambient fill and new decorative
content require visual/comfort review before study use. This pass does not
approve new physiological thresholds, safety ranges or study parameters.

## Wall texture provenance

Generated with the built-in image generation tool (no CLI/API fallback).
Saved project asset: `unity/Assets/Laminar VR/Art/TemplePond/Textures/Wall/T_TempleWeatheredPlaster.png`.
Unity's importer supplies the 1K mipmapped platform texture; the original PNG is retained.
The attached wall screenshot was visual direction, not an image-edit target.

Final generation prompt:

```text
Use case: game texture asset. Generate a new seamless horizontally tileable square base-color texture for a stylized Japanese garden courtyard wall in a mobile VR game. This is a flat orthographic material swatch filling the entire image edge to edge, not a scene or rendered object. Upper 65 percent: worn pale cool-neutral grey lime plaster, softly mottled, chalky, restrained desaturated grey with a very slight sage undertone. Lower 28 percent: exposed old grey stone brick masonry, five straight horizontal staggered courses, each block wide and low, subtly irregular worn edges and narrow pale mortar joints; restrained charcoal and warm-grey stones. Between plaster and masonry: irregular flaking plaster, small chipped patches exposing individual bricks, subtle beige-grey mineral staining. Hand-painted stylized realism, soft natural color variation, quiet medium-low contrast, clean readable masonry at small resolution, no sharp black cracks. Approximate material appearance of old Asian temple garden plaster over stone, grey plaster around sRGB #a4aaa7 and masonry #69716d. Uniform flat diffuse illumination, no directional shadows, no ambient cast shadows, no highlights, no perspective, no border, no floor, no architecture, no foliage, no text or watermark. Left and right edges must match seamlessly, masonry rows exactly horizontal. The top edge is plain lightly mottled plaster and the bottom edge is masonry. One texture only, not a texture map contact sheet.
```
