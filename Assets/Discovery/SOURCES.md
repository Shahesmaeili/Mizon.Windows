# Discovery demo image credits

These downloaded thumbnails are local presentation assets for fictional music cards and friend activities. They do not replace API-provided story, artist, or user media. Stock portraits illustrate fictional demo accounts; the photographed people are not identified as those accounts.

Wikimedia Commons supplied resized JPEG thumbnails (960px covers, 250px portraits). No image content was edited; the UI may crop images to fill its layout. Each image retains the license listed below, independently of the application code.

| Local file | Original source / photographer | License |
| --- | --- | --- |
| `after-the-rain.jpg` | [Rain on a window.jpg](https://commons.wikimedia.org/wiki/File:Rain_on_a_window.jpg) — W.carter | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0) |
| `blue-hour.jpg` | [Moraine Lake sunrise.jpg](https://commons.wikimedia.org/wiki/File:Moraine_Lake_sunrise.jpg) — Jakub Fryš | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0) |
| `city-lights.jpg` | [Colorful illuminated facades of buildings at night, with green, blue and pink lights, Kabukicho, Shinjuku, Tokyo.jpg](https://commons.wikimedia.org/wiki/File:Colorful_illuminated_facades_of_buildings_at_night,_with_green,_blue_and_pink_lights,_Kabukicho,_Shinjuku,_Tokyo.jpg) — Basile Morin | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0) |
| `first-light.jpg` | [Himalaya sunrise.jpg](https://commons.wikimedia.org/wiki/File:Himalaya_sunrise.jpg) — Ali Sabbagh | [CC0](http://creativecommons.org/publicdomain/zero/1.0/deed.en) |
| `friend-amir.jpg` | [Curly hair and freckles man (Unsplash).jpg](https://commons.wikimedia.org/wiki/File:Curly_hair_and_freckles_man_(Unsplash).jpg) — Jeremy Bishop tidesinourveins | [CC0](http://creativecommons.org/publicdomain/zero/1.0/deed.en) |
| `friend-sara.jpg` | [Winter Woman Portrait.jpg](https://commons.wikimedia.org/wiki/File:Winter_Woman_Portrait.jpg) — Petr Kratochvil | [CC0](http://creativecommons.org/publicdomain/zero/1.0/deed.en) |
| `golden-days.jpg` | [Sand dunes at sunset.jpg](https://commons.wikimedia.org/wiki/File:Sand_dunes_at_sunset.jpg) — Jon Sullivan | Public domain (see source) |
| `midnight-drive.jpg` | [Kabukicho red gate and colorful neon street signs at night, Shinjuku, Tokyo, Japan.jpg](https://commons.wikimedia.org/wiki/File:Kabukicho_red_gate_and_colorful_neon_street_signs_at_night,_Shinjuku,_Tokyo,_Japan.jpg) — Basile Morin | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0) |
| `ocean-air.jpg` | [Drone view of ocean waves (Unsplash).jpg](https://commons.wikimedia.org/wiki/File:Drone_view_of_ocean_waves_(Unsplash).jpg) — Caleb Jones gcalebjones | [CC0](http://creativecommons.org/publicdomain/zero/1.0/deed.en) |
| `quiet-room.jpg` | [Forest trees in thick fog.jpg](https://commons.wikimedia.org/wiki/File:Forest_trees_in_thick_fog.jpg) — Mar Mkrtchyan | [CC0](http://creativecommons.org/publicdomain/zero/1.0/deed.en) |
| `slow-motion.jpg` | [Concert blue lights.jpg](https://commons.wikimedia.org/wiki/File:Concert_blue_lights.jpg) — Lemonade2806 | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0) |
| `somewhere-new.jpg` | [Comandau forest railway.jpg](https://commons.wikimedia.org/wiki/File:Comandau_forest_railway.jpg) — Jan Pešula | [CC0](http://creativecommons.org/publicdomain/zero/1.0/deed.en) |
| `stay-awhile.jpg` | [Three pink cherry blossoms in Tuntorp.jpg](https://commons.wikimedia.org/wiki/File:Three_pink_cherry_blossoms_in_Tuntorp.jpg) — W.carter | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0) |
| `velvet-sky.jpg` | [Aurora borealis over Eielson Air Force Base, Alaska.jpg](https://commons.wikimedia.org/wiki/File:Aurora_borealis_over_Eielson_Air_Force_Base,_Alaska.jpg) — United States Air Force photo by Senior Airman Joshua Strang | Public domain (see source) |


Downloaded from Wikimedia Commons on 2026-10-29. The linked original file pages contain the full provenance and licensing statements.

## Featured performer

`featured-performer.png` is an original fictional performer generated with the built-in imagegen tool on 2026-10-29. It is not a photograph of any artist named in the sample data. The alpha channel is preserved.

Prompt: "Create a premium editorial music app hero asset, a photorealistic cutout on a genuinely transparent background. Single fictional young adult male indie singer of Middle Eastern appearance, dark wavy hair, tasteful black over-ear headphones, wearing a deep burgundy overshirt over black shirt. Waist-up dynamic portrait facing camera slightly turned, one hand lightly touching an earcup, expressive relaxed confident face, no microphone blocking face. Natural photographic studio lighting with soft magenta rim light and teal accent, crisply detailed fabric and hair, high-end magazine campaign. Entire head and both arms inside image with clear transparent margins, torso extends to bottom edge. Vertical composition with subject in central 70 percent. No text, no logos, no background, no border, no shadow ground. This will be layered over a colored rectangular native music discovery banner, with the head extending above the card."

## Layout references

### Floating story editorial strip (2026-11-11)

`StoryOrbitShowcase` follows [Zummedy Store Blog Design — Manuel Rovira](https://dribbble.com/shots/10996724-Zummedy-Store-Blog-Design),
specifically the user's [three-collage reference](https://cdn.dribbble.com/userupload/36919937/file/original-e77a90b91a73746153dd33367d85943c.png).
The user requested the original English labels `Models`, `Illustration`, `Team`,
and approved a similar replacement when the independent left portrait could not
be located. These are decorative editorial samples, not artist/profile API data.
The reference screenshot itself is not shipped. Crops, silhouettes, typography,
numbers and badges are native XAML/Composition layers, not flattened screenshots.

`Common/StoryOrbitGeometry.cs` separately traces the six photo and outer-paper
boundaries from the 1600x1200 reference. Hidden portions under lettering/badges
are smooth interpolations; this is not the designer's original vector file.
The paper outline is not a scaled copy of the photo. Layout/crop coordinates
share that reference space; surrounding matte remains translucent over Mizon.

| Asset | Source / credit | Provenance |
| --- | --- | --- |
| `orbit-models.png` | Original fictional adult portrait generated using built-in imagegen, 2026-11-11 | User-approved similar replacement for the left image; not a photograph of Sasha Belyaeva or a reconstruction of the exact original. Final prompt below. |
| `orbit-illustration.jpg` | Leslie Rosique — Modern Kabuki Theatre Origami Witch; [artist project](https://www.behance.net/gallery/79712277/Character-Design-Challenge-Kabuki-Theatre), [Character Design Challenge credit/gallery](https://character-design.squarespace.com/winners5/winners-of-the-kabuki-challenge) | [Original JPEG hosted by the challenge gallery](https://images.squarespace-cdn.com/content/v1/54fc8146e4b02a22841f4df7/1557075017355-28KK3R578A3DTHX9STI8/18b.jpg). Visually matched to the reference. Copyright remains with the artist; no open commercial redistribution license verified. |
| `orbit-team.jpg` | Lena Smirnova / Lena & Artem Studio — [Portraits for platfor.ma](https://www.behance.net/gallery/60929763/Portraits-for-platforma) | [Photographer portfolio image](https://i-p.rmcdn.net/5a5f902ddf66014b7c0f35e5/5088082/image-dcea0662-fd5b-4317-89e6-6f8dd007a41c.jpg?cH=2497&cW=1906&cX=50&cY=0&w=804). Visually matched; original downloaded JPEG unchanged. The Behance module displays attribution/non-commercial/no-derivatives terms; not a freely licensed commercial asset. |

Third-party image rights are separate from the application code license. These
user-requested reference images are design samples, not evidence of acquired
production redistribution rights. The native UI crops their unmodified bytes.

The editorial lettering uses [Bodoni Moda](https://github.com/google/fonts/tree/main/ofl/bodonimoda),
an open font selected for the reference's high-contrast serif look. The exact
font used by the reference author was not identified. `Assets/Fonts/BodoniModa.ttf`
is the unmodified upstream variable font; `BodoniModa-OFL.txt` ships its SIL OFL 1.1
license and copyright notice.

Final built-in imagegen prompt for `orbit-models.png`:

> Use case: photorealistic-natural. Asset type: original square editorial photograph for the left organic portrait collage of a native music app. Create one high-quality realistic fashion portrait of a fictional adult woman (not a named real person), front-facing with a calm serious expression. Straight short chin-length turquoise/aqua blue bob with dark roots and a center part, pale natural skin, subtle makeup. She wears an oversized dark olive-green military-style trench coat, and holds a modest bouquet of pink/lilac and purple flowers across her lower torso. Background is a plain warm sage/olive gray-green studio backdrop, subtly darker at left, no scenery. Soft directional editorial photography lighting, tactile cloth, natural skin texture, subdued sage and dark forest greens contrasted with vivid turquoise hair. Square 1024 by 1024 composition: upper-body portrait, entire head comfortably inside frame; head centered horizontally around 57% of canvas width, top of hair at about 13% of canvas height, face centered near 36% height; shoulders and green coat span the lower half, bouquet chiefly at lower-left. Clean visual likeness of a premium 2019 fashion editorial, not a cartoon or 3D render. This image will be clipped into an asymmetric soft frame by code, so fill the ENTIRE square with the photograph. No frame, no cutout shape, no titles, no numbers, no stars, no logos, no watermark, no UI, no typography.

### Existing discovery sections

- [NFT Store Desktop — Manuel Rovira](https://dribbble.com/shots/15319401-NFT-Store-Desktop): nested rounded cards, contributor strip, artwork and inset action row; orbital sculptural display.
- [ValuBet — Slava Kornilov / Geex Arts](https://dribbble.com/shots/8801346-ValuBet): central figure protruding from a colored banner, lateral information and split activity line.
- [ValueBet dark edition — Slava Kornilov / Geex Arts](https://dribbble.com/shots/7800822-ValueBet): current hero reference. A single opaque dark 770:360 surface with the figure cropped inside, three thumbnails in the upper corner, side identities, centered track metadata, and a split line with three inset audience/action groups. Height is bounded to 190–340 logical pixels; compact layouts keep all content inside the same card.

Reference screenshots are not shipped as UI assets. Interactive sculptures are procedural 3D geometry rendered by `DiscoverySculpture`.

The current hero shows the selected record's complete cover photograph at full opacity. The earlier generated performer is retained as an unused design asset and is no longer layered over the cover. Hero rendering uses the same `DevWinUI.ImageFrame` as StoreCarousel, including `UseAutoShadowColor`, centered shadow offsets, opacity 1 and blur radius 80. Its shadow therefore uses the same edge-color calculation, not a separately chosen accent.

The top artist cards follow the profile-card composition in [the ValueBet profiles reference](https://cdn.dribbble.com/userupload/36701567/file/original-2a60baf2d76db2de275318be5f965900.png): opaque banner, overlapping ringed avatar, centered identity and statistics, follow action, and a bottom sparkline. Only the lower information surface is translucent. Additional stock portrait and concert credits are in [TOP-ARTISTS-SOURCES.md](TOP-ARTISTS-SOURCES.md).
