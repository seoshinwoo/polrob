import AppKit

let root = "/Users/seoshinwoo/Documents/code/projects/polrob"
let output = root + "/tmp/map-v5-review"
try FileManager.default.createDirectory(atPath: output, withIntermediateDirectories: true)
let names = ["police_station", "jail", "cafe", "donut", "burger", "warehouse", "house-orange"]
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 1400, pixelsHigh: 760,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
NSGraphicsContext.current?.imageInterpolation = .high
NSColor(calibratedRed: 0.91, green: 0.93, blue: 0.92, alpha: 1).setFill()
NSRect(x: 0, y: 0, width: 1400, height: 760).fill()
for (index, name) in names.enumerated() {
    let img = NSImage(contentsOfFile: root + "/polrob.Client/Resources/Raw/MapAssets/" + name + ".png")!
    let cellX = CGFloat(index % 4) * 350
    let cellY = CGFloat(1 - index / 4) * 380
    let scale = min(320 / img.size.width, 320 / img.size.height)
    let w = img.size.width * scale, h = img.size.height * scale
    img.draw(in: NSRect(x: cellX + (350 - w) / 2, y: cellY + 48 + (320 - h) / 2,
        width: w, height: h), from: .zero, operation: .sourceOver, fraction: 1)
    let title = NSAttributedString(string: name, attributes: [
        .font: NSFont.systemFont(ofSize: 20, weight: .semibold), .foregroundColor: NSColor.darkGray])
    title.draw(at: NSPoint(x: cellX + (350 - title.size().width) / 2, y: cellY + 16))
}
NSGraphicsContext.restoreGraphicsState()
let path = output + "/existing-building-references.png"
try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
print(path)
