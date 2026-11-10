# Moein head-and-torso reconstruction

`MoeinBust.glb` depicts Iranian singer Nasrollah Moein Najafabadi (معین).
It is an AI-assisted approximation from licensed photographs, not a physical scan,
an official artist asset, or a complete multi-view reconstruction. Unseen surfaces,
including the back of the head and jacket, are inferred. Hair and facial detail
are softer than the reference photographs.

## Reference photographs

1. **Moein Live in Toronto 2023**, by **Mehrshad Adkins 01**.
   [Wikimedia Commons source](https://commons.wikimedia.org/wiki/File:Moein_Live_in_Toronto_2023.jpg),
   [original photograph](https://upload.wikimedia.org/wikipedia/commons/4/48/Moein_Live_in_Toronto_2023.jpg).
2. **IMG20190617214541687moein**, by **Mehrshad.Adkins**.
   [Wikimedia Commons source](https://commons.wikimedia.org/wiki/File:IMG20190617214541687moein.jpg),
   [original photograph](https://upload.wikimedia.org/wikipedia/commons/1/12/IMG20190617214541687moein.jpg).

Both reference photographs are licensed under
[Creative Commons Attribution-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-sa/4.0/).
The generated reference and reconstructed model are distributed as adaptations under
the same license. [License legal code](https://creativecommons.org/licenses/by-sa/4.0/legalcode.en).
No endorsement by Moein, the photographers or the generation-service operators is implied.
This asset license does not apply to the application's unrelated source code.

## Changes and generation

- A new frontal head-and-torso reference was generated with **built-in image_gen**.
  Background, microphone and hands were removed; pose and lighting were reconstructed.
- That transparent portrait was converted to a textured volumetric mesh using the
  public [TRELLIS demo](https://huggingface.co/spaces/trellis-community/TRELLIS).
- Area-weighted smooth vertex normals were baked across UV seams.
- The exported material was set to nonmetallic, with roughness 0.9.
- Original geometry and the embedded texture were otherwise retained.
- The app presents the bust with yaw 180 degrees, view scale 1 and vertical focus 0.

Created 2026-10-30. The exact image prompt, photo credits, model parameters and checksums
are saved in [Moein-generation.json](Moein-generation.json).
The intermediate portrait is [Moein-reference.png](Moein-reference.png).

## Asset and validation

- GLB 2.0; one textured mesh; no external files, compression extensions or runtime service required.
- 23,767 vertices; 36,986 triangles; embedded 2048 x 2048 PNG texture.
- 5,612,952 bytes.
- SHA-256: `c14f3341b7a7545e05cfbfae670fdb17ab3efc6d1326131ef1367543d24c13e9`.
- Rendered successfully by the application's native ArtistModelView at 180 x 220,
  156 x 168 and 420 x 420. Front, side and back views were checked.
- The sample profile's names, statistics and songs remain demonstration data;
  this replacement only changes its 3D visual, not the database or artist catalog.
