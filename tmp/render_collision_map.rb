require "base64"

ROOT = File.expand_path("..", __dir__)
MAP_SOURCE = File.join(ROOT, "polrob.Shared/Models/Map.cs")
CLIENT_SOURCE = File.join(ROOT, "polrob.Client/GamePlay.xaml.cs")
OUTPUT = File.join(__dir__, "current_map_collision_debug.svg")

def data_uri(path)
  "data:image/png;base64,#{Base64.strict_encode64(File.binread(path))}"
end

map_source = File.read(MAP_SOURCE)
client_source = File.read(CLIENT_SOURCE)

prop_block = map_source[/public static readonly MapPropLayout\[\] PropLayouts =\s*\[(.*?)\n\s*\];/m, 1]
props = prop_block.scan(/new\("([^"]+)",\s*([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f(?:,\s*IsTriangular:\s*true)?\)/).map do |asset, x, y, width, height|
  match = prop_block.match(/new\("#{Regexp.escape(asset)}",\s*#{Regexp.escape(x)}f,\s*#{Regexp.escape(y)}f,\s*#{Regexp.escape(width)}f,\s*#{Regexp.escape(height)}f(,\s*IsTriangular:\s*true)?\)/)
  { asset: asset, x: x.to_f, y: y.to_f, width: width.to_f, height: height.to_f, triangular: !match[1].nil? }
end

road_block = client_source[/private static readonly RoadTilePlacement\[\] RoadTilePlacements =\s*\[(.*?)\n\s*\];/m, 1]
roads = road_block.scan(/new\((\d+),\s*(\d+),\s*(\d+),\s*(\d+)\)/).map do |row, column, asset, rotation|
  { row: row.to_i, column: column.to_i, asset: asset.to_i, rotation: rotation.to_i }
end

images_root = File.join(ROOT, "polrob.Client/Resources/Images")
raw_root = File.join(ROOT, "polrob.Client/Resources/Raw")
city = data_uri(File.join(raw_root, "FloorTiles/city_ground.png"))
forest = data_uri(File.join(raw_root, "FloorTiles/forest_ground.png"))
street_uris = (1..7).to_h { |n| [n, data_uri(File.join(raw_root, "FloorTiles/street-#{n}.png"))] }
prop_uris = props.map { |p| p[:asset] }.uniq.to_h { |asset| [asset, data_uri(File.join(images_root, asset))] }

svg = []
svg << <<~SVG
  <svg xmlns="http://www.w3.org/2000/svg" width="1280" height="2000" viewBox="0 0 2560 4000">
    <defs>
      <pattern id="city" width="256" height="256" patternUnits="userSpaceOnUse">
        <image href="#{city}" width="256" height="256" preserveAspectRatio="none"/>
      </pattern>
      <pattern id="forest" width="256" height="256" patternUnits="userSpaceOnUse">
        <image href="#{forest}" width="256" height="256" preserveAspectRatio="none"/>
      </pattern>
      <filter id="shadow" x="-30%" y="-30%" width="160%" height="160%">
        <feDropShadow dx="0" dy="3" stdDeviation="4" flood-opacity="0.8"/>
      </filter>
    </defs>
    <rect width="2560" height="4000" fill="#1b2024"/>
    <text x="64" y="68" font-family="Arial, sans-serif" font-size="38" font-weight="700" fill="white">CURRENT MAP · PHYSICS COLLISION DEBUG</text>
    <rect x="64" y="91" width="34" height="24" rx="4" fill="#ff2d2d" fill-opacity="0.25" stroke="#ff3030" stroke-width="6"/>
    <text x="116" y="113" font-family="Arial, sans-serif" font-size="25" fill="#f5f5f5">red fill / outline = movement-blocking collider (#{props.length} objects)</text>
    <g transform="translate(0 160)">
      <rect width="2560" height="3072" fill="url(#city)"/>
      <rect y="3072" width="2560" height="768" fill="url(#forest)"/>
SVG

roads.each_with_index do |road, index|
  x = road[:column] * 256
  y = road[:row] * 256
  cx = x + 128
  cy = y + 128
  clip = ""
  if [2, 3].include?(road[:asset])
    clip_id = "roadclip#{index}"
    clip_cx = road[:asset] == 2 ? x : x + 256
    svg << %(<clipPath id="#{clip_id}"><circle cx="#{clip_cx}" cy="#{y + 128}" r="256"/></clipPath>\n)
    clip = %( clip-path="url(##{clip_id})")
  end
  svg << %(<image href="#{street_uris[road[:asset]]}" x="#{x}" y="#{y}" width="256" height="256" preserveAspectRatio="none" transform="rotate(#{road[:rotation]} #{cx} #{cy})"#{clip}/>\n)
end

props.each do |prop|
  left = prop[:x] - prop[:width] / 2.0
  top = prop[:y] - prop[:height] / 2.0
  svg << %(<image href="#{prop_uris[prop[:asset]]}" x="#{left}" y="#{top}" width="#{prop[:width]}" height="#{prop[:height]}" preserveAspectRatio="none"/>\n)
end

props.each_with_index do |prop, index|
  left = prop[:x] - prop[:width] / 2.0
  top = prop[:y] - prop[:height] / 2.0
  right = prop[:x] + prop[:width] / 2.0
  bottom = prop[:y] + prop[:height] / 2.0
  if prop[:triangular]
    svg << %(<polygon points="#{prop[:x]},#{top} #{right},#{bottom} #{left},#{bottom}" fill="#ff2020" fill-opacity="0.24" stroke="#ff3030" stroke-width="9" stroke-linejoin="round"/>\n)
  else
    svg << %(<rect x="#{left}" y="#{top}" width="#{prop[:width]}" height="#{prop[:height]}" fill="#ff2020" fill-opacity="0.24" stroke="#ff3030" stroke-width="9"/>\n)
  end
  label = File.basename(prop[:asset], ".png").upcase
  label_width = [label.length * 17 + 38, 110].max
  svg << %(<g filter="url(#shadow)"><rect x="#{left}" y="#{top - 38}" width="#{label_width}" height="34" rx="8" fill="#111" fill-opacity="0.84"/><text x="#{left + 12}" y="#{top - 12}" font-family="Arial, sans-serif" font-size="22" font-weight="700" fill="#fff">#{index + 1} · #{label}</text></g>\n)
end

svg << <<~SVG
      <rect x="4" y="4" width="2552" height="3832" fill="none" stroke="#fff" stroke-opacity="0.55" stroke-width="8"/>
    </g>
  </svg>
SVG

File.write(OUTPUT, svg.join)
puts OUTPUT
puts "props=#{props.length} roads=#{roads.length}"
