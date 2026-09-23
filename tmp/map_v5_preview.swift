import AppKit

let root = "/Users/seoshinwoo/Documents/code/projects/polrob"
let map = NSImage(contentsOfFile: root + "/tmp/map-v5-review/final-map.png")!
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 960, pixelsHigh: 1065,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSGraphicsContext.current?.imageInterpolation = .high
map.draw(in: NSRect(x: 0, y: 0, width: 960, height: 1065),
    from: NSRect(x: 175, y: 1536 - 377 - 355, width: 320, height: 355),
    operation: .copy, fraction: 1)
// Same source PNGs and body scale as the actual game renderer.
let side = CGFloat(1088) * (50 * 0.86 / 512) * (1024 / 2560) * 3
for (name, x, y) in [("char_police", CGFloat(333), CGFloat(474)),
                       ("char_robber", CGFloat(399), CGFloat(568))] {
    let sprite = NSImage(contentsOfFile: root + "/polrob.Client/Resources/Raw/" + name + ".png")!
    sprite.draw(in: NSRect(x: (x - 175) * 3 - side / 2,
        y: 1065 - (y - 377) * 3 - side / 2, width: side, height: side),
        from: NSRect(x: 0, y: 0, width: 1088, height: 1088),
        operation: .sourceOver, fraction: 1)
}
NSGraphicsContext.restoreGraphicsState()
let path = root + "/tmp/map-v5-review/shops-with-actual-characters.png"
try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
print(path)
