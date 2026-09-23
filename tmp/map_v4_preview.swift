import AppKit

// Preview-only composition: draw the actual game PNGs, never regenerate sprites.
let project = "/Users/seoshinwoo/Documents/code/projects/polrob"
let mapPath = CommandLine.arguments[1]
let version = CommandLine.arguments[2]
let map = NSImage(contentsOfFile: mapPath)!
let mapRep = NSBitmapImageRep(data: try Data(contentsOf: URL(fileURLWithPath: mapPath)))!
let police = NSImage(contentsOfFile: project + "/polrob.Client/Resources/Raw/char_police.png")!
let robber = NSImage(contentsOfFile: project + "/polrob.Client/Resources/Raw/char_robber.png")!
let directory = project + "/tmp/map-v4-review"
try FileManager.default.createDirectory(atPath: directory, withIntermediateDirectories: true)

// Coordinate specs use a 1024 x 1536 reference canvas. Match the existing
// renderer: 2560-wide world, 50 diameter, .86 body ratio, 512px body profile.
let referenceScale = CGFloat(mapRep.pixelsWide) / 1024
let previewScale = CGFloat(4)
let spriteSide = CGFloat(1088) * (50 * 0.86 / 512) * (1024 / 2560) * previewScale
let panels: [(String, CGFloat, CGFloat, CGFloat, CGFloat, CGFloat, CGFloat)] = [
    ("01-alley", 180, 390, 332, 476, 399, 573),
    ("02-trees", 700, 1000, 822, 1078, 915, 1145),
    ("03-yard", 365, 1030, 550, 1250, 550, 1070)
]

for (name, cropX, cropY, policeX, policeY, robberX, robberY) in panels {
    let width = CGFloat(300), height = CGFloat(240)
    let outputWidth = Int(width * previewScale), outputHeight = Int(height * previewScale)
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: outputWidth,
        pixelsHigh: outputHeight, bitsPerSample: 8, samplesPerPixel: 4,
        hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
        bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    NSGraphicsContext.current?.imageInterpolation = .high
    map.draw(in: NSRect(x: 0, y: 0, width: outputWidth, height: outputHeight),
        from: NSRect(x: cropX * referenceScale,
            y: CGFloat(mapRep.pixelsHigh) - (cropY + height) * referenceScale,
            width: width * referenceScale, height: height * referenceScale),
        operation: .copy, fraction: 1)
    for (sprite, x, y) in [(police, policeX, policeY), (robber, robberX, robberY)] {
        sprite.draw(in: NSRect(x: (x - cropX) * previewScale - spriteSide / 2,
            y: CGFloat(outputHeight) - (y - cropY) * previewScale - spriteSide / 2,
            width: spriteSide, height: spriteSide),
            from: NSRect(x: 0, y: 0, width: 1088, height: 1088),
            operation: .sourceOver, fraction: 1)
    }
    NSGraphicsContext.restoreGraphicsState()
    let path = directory + "/" + version + "-" + name + ".png"
    try bitmap.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
    print(path)
}
