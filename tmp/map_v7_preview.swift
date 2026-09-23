import AppKit

let root = "/Users/seoshinwoo/Documents/code/projects/polrob"
let source = "/Users/seoshinwoo/.codex/generated_images/01a0b967-e8cf-76f0-9316-162b6c47cea6/exec-0aed3b99-f1c4-4ab6-8b16-8a1655434a1d.png"
let directory = root + "/tmp/map-v7-review"
try FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)
let mapPath = directory + "/final-map.png"
if !FileManager.default.fileExists(atPath: mapPath) {
    try FileManager.default.copyItem(atPath: source, toPath: mapPath)
}
let map = NSImage(contentsOfFile: mapPath)!
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 1509, pixelsHigh: 540,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSGraphicsContext.current?.imageInterpolation = .high
NSColor(calibratedWhite: 0.96, alpha: 1).setFill()
NSRect(x: 0, y: 0, width: 1509, height: 540).fill()
let panels: [(CGFloat, CGFloat, String, CGFloat, CGFloat)] = [
    (180, 370, "char_police", 334, 478),
    (195, 575, "char_robber", 343, 683),
    (545, 370, "char_police", 691, 478)
]
let side = CGFloat(1088) * (50 * 0.86 / 512) * (1024 / 2560) * 3
for (index, panel) in panels.enumerated() {
    let (cx, cy, name, px, py) = panel
    let ox = CGFloat(index * 507)
    map.draw(in: NSRect(x: ox, y: 0, width: 495, height: 540),
        from: NSRect(x: cx, y: 1536 - cy - 180, width: 165, height: 180),
        operation: .copy, fraction: 1)
    let sprite = NSImage(contentsOfFile: root + "/polrob.Client/Resources/Raw/" + name + ".png")!
    sprite.draw(in: NSRect(x: ox + (px - cx) * 3 - side / 2,
        y: 540 - (py - cy) * 3 - side / 2, width: side, height: side),
        from: NSRect(x: 0, y: 0, width: 1088, height: 1088),
        operation: .sourceOver, fraction: 1)
}
NSGraphicsContext.restoreGraphicsState()
let path = directory + "/shop-details-with-actual-characters.png"
try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
print(path)
