# 하단 공사장·주택 공원 구역 위치 교환

내장 image_gen. 04-pond-construction.png를 편집하여 공사장을 좌하단, 주택·공원 구역을 우하단으로 옮깁니다.

```text
Use case: precise-object-edit.
The input image is the EDIT TARGET. Make ONE exact layout change: SWAP the contents of the two LOWER map parcels. Preserve all other artwork and map geometry as closely as possible. Same portrait1024x1536, same simple2D drawing style, asset designs, colors, outlines, scale and overhead camera.
SOURCE PARCEL A (currently lower-right): the tan dirt CONSTRUCTION LOT, about x575–975,y938–1330, with low incomplete concrete foundation, small blue-roof site office, crates, two orange-white barriers and a small gray material stack.
SOURCE PARCEL B (currently immediately to its LEFT, lower-left): the GREEN RESIDENTIAL/PARK BLOCK, about x94–515,y894–1338, with one red-roof house, green lawns, cream cross paths, trees, straight hedges and short cream wall pieces.

REQUIRED RESULT:
- LOWER LEFT becomes the construction/vacant lot: move the existing tan soil, low concrete foundation, blue-roof site office, wood crates, orange-white barriers and gray material stack into the former park/house parcel. Retain generous empty dirt, clear entrances and the same few objects. No new decoration.
- LOWER RIGHT becomes the residential/park block: move the existing red-roof house, green lawn, cream cross-paths, trees, hedges and short walls into the former construction parcel. Keep recognizable source assets and multiple open paths.
This is an exchange, not duplication: there must be only ONE construction lot at lower-left and ONE corresponding red-house/park block at lower-right.
Fit each set of contents inside its destination parcel's CURRENT outer outline; minor internal spacing adjustments are allowed because footprints differ, but keep building/tree sprite scale and camera consistent. Keep objects separated with walkable gaps. Do not mirror the building artwork.

LOCK EVERYTHING OUTSIDE THE TWO LOWER PARCELS:
Keep ALL neutral GRAY roads exactly where they are, including the vertical separator between the parcels, the east-west road above them, and the lower return road. Preserve road widths, right-angle corners and intersections. Keep the POND in its exact middle-right location and shape. Keep upper-left police station and adjacent jail, upper-right homes, middle-left shops, middle-right pond square and bottom-edge greenery exactly in place. Maintain all current colors: neutral gray streets, warm paving, sage grass, navy/blue/coral/teal roofs. No blue roads.
No style change, no added3D shading, textures, perspective, new assets, labels, arrows, UI or characters. Only existing POLICE sign. Return the complete map with these two lower land parcels exchanged.
```
