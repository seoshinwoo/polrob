import AppKit

let root = "/Users/seoshinwoo/Documents/code/projects/polrob"
let source = "/Users/seoshinwoo/.codex/generated_images/01a0b967-e8cf-76f0-9316-162b6c47cea6/exec-6295cb57-dd74-49e6-9033-94db7f969f65.png"
let directory = root + "/tmp/map-v6-review"
try FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)
let mapPath = directory + "/final-map.png"
if !FileManager.default.fileExists(atPath: mapPath) {
    try FileManager.default.copyItem(atPath: source, toPath: mapPath)
}
let map = NSImage(contentsOfFile: mapPath)!
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 930, pixelsHigh: 780,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSGraphicsContext.current?.imageInterpolation = .high
map.draw(in: NSRect(x: 0, y: 0, width: 930, height: 780),
    from: NSRect(x: 440, y: 1536 - 20 - 260, width: 310, height: 260),
    operation: .copy, fraction: 1)
let sprite = NSImage(contentsOfFile: root + "/polrob.Client/Resources/Raw/char_police.png")!
let side = CGFloat(1088) * (50 * 0.86 / 512) * (1024 / 2560) * 3
sprite.draw(in: NSRect(x: (592 - 440) * 3 - side / 2,
    y: 780 - (251 - 20) * 3 - side / 2, width: side, height: side),
    from: NSRect(x: 0, y: 0, width: 1088, height: 1088), operation: .sourceOver, fraction: 1)
NSGraphicsContext.restoreGraphicsState()
let path = directory + "/police-station-with-actual-character.png"
try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
print(path)
