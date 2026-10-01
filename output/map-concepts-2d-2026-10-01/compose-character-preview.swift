import AppKit
import Foundation

// Draws the existing, unmodified PNG sprites onto a concept map. It does not
// redraw characters, remove their alpha, or modify any source/runtime assets.
// Usage: swift compose-character-preview.swift config.json
// Positions and crop rectangles use ORIGINAL MAP PIXELS, origin at top-left.

struct Character: Decodable {
    let kind: String
    let x: Double
    let y: Double
    let angleDegrees: Double?
    let radiusWorldUnits: Double?
}

struct Crop: Decodable {
    let x: Double
    let y: Double
    let width: Double
    let height: Double
    let scale: Double?
    let outputPath: String
}

struct Config: Decodable {
    let mapPath: String
    let outputPath: String
    let spriteDirectory: String?
    let worldWidth: Double?
    let pixelsPerWorldUnit: Double?
    let outputScale: Double?
    let characters: [Character]
    let crop: Crop?
    let crops: [Crop]?
}

struct InputImage {
    let image: NSImage
    let width: Int
    let height: Int
}

enum PreviewError: Error, CustomStringConvertible {
    case message(String)
    var description: String {
        switch self { case .message(let message): return message }
    }
}

func require(_ condition: Bool, _ message: String) throws {
    if !condition { throw PreviewError.message(message) }
}

func readImage(_ path: String) throws -> InputImage {
    let data = try Data(contentsOf: URL(fileURLWithPath: path))
    guard let bitmap = NSBitmapImageRep(data: data) else {
        throw PreviewError.message("Cannot decode image: \(path)")
    }
    // Explicitly normalize point size to pixel size, independent of PNG DPI.
    let image = NSImage(size: NSSize(width: bitmap.pixelsWide, height: bitmap.pixelsHigh))
    image.addRepresentation(bitmap)
    return InputImage(image: image, width: bitmap.pixelsWide, height: bitmap.pixelsHigh)
}

func resolvedPath(_ path: String, relativeTo directory: URL) -> String {
    if path.hasPrefix("/") { return path }
    return directory.appendingPathComponent(path).standardizedFileURL.path
}

do {
    try require(CommandLine.arguments.count == 2,
                "Usage: swift compose-character-preview.swift config.json")
    let configURL = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL
    let configDirectory = configURL.deletingLastPathComponent()
    let config = try JSONDecoder().decode(Config.self, from: Data(contentsOf: configURL))
    let map = try readImage(resolvedPath(config.mapPath, relativeTo: configDirectory))
    let defaultSpriteDirectory = "/Users/seoshinwoo/Documents/code/projects/polrob/polrob.Client/Resources/Raw"
    let spriteDirectory = resolvedPath(config.spriteDirectory ?? defaultSpriteDirectory,
                                      relativeTo: configDirectory)
    let police = try readImage(spriteDirectory + "/char_police.png")
    let robber = try readImage(spriteDirectory + "/char_robber.png")
    try require(police.width == 1088 && police.height == 1088 &&
                robber.width == 1088 && robber.height == 1088,
                "Expected current normalized 1088×1088 character PNGs.")
    let worldWidth = config.worldWidth ?? 2560
    let mapScale = config.pixelsPerWorldUnit ?? Double(map.width) / worldWidth
    let outputScale = config.outputScale ?? 1
    try require(worldWidth.isFinite && worldWidth > 0 && mapScale.isFinite && mapScale > 0 &&
                outputScale.isFinite && outputScale > 0, "Scale values must be positive and finite.")
    for character in config.characters {
        try require(["police", "robber"].contains(character.kind),
                    "Unsupported character kind: \(character.kind). Use police or robber.")
        try require(character.x.isFinite && character.y.isFinite &&
                    (character.angleDegrees ?? 0).isFinite &&
                    (character.radiusWorldUnits ?? 25).isFinite &&
                    (character.radiusWorldUnits ?? 25) > 0, "Character values must be finite.")
    }

    func render(rect: NSRect, scale: Double, destination: String) throws {
        try require(scale.isFinite && scale > 0 && rect.width > 0 && rect.height > 0 &&
                    rect.minX >= 0 && rect.minY >= 0 &&
                    rect.maxX <= Double(map.width) && rect.maxY <= Double(map.height),
                    "Crop must be a positive rectangle inside the original map.")
        let outputWidth = Int((rect.width * scale).rounded())
        let outputHeight = Int((rect.height * scale).rounded())
        try require(outputWidth > 0 && outputHeight > 0 &&
                    outputWidth <= 16384 && outputHeight <= 16384,
                    "Output must be between 1 and 16384 pixels per side.")
        guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil,
            pixelsWide: outputWidth, pixelsHigh: outputHeight, bitsPerSample: 8,
            samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
            let context = NSGraphicsContext(bitmapImageRep: bitmap) else {
            throw PreviewError.message("Cannot allocate preview bitmap.")
        }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = context
        context.imageInterpolation = .high
        let actualScaleX = Double(outputWidth) / rect.width
        let actualScaleY = Double(outputHeight) / rect.height
        let mapSource = NSRect(x: rect.minX,
            y: Double(map.height) - rect.minY - rect.height,
            width: rect.width, height: rect.height)
        map.image.draw(in: NSRect(x: 0, y: 0, width: outputWidth, height: outputHeight),
                       from: mapSource, operation: .copy, fraction: 1)
        for character in config.characters {
            let sprite = character.kind == "police" ? police : robber
            // Same body width and pivot as GamePlay.CreatePlayerDestinationRect.
            // Current live client/server radius is 25, so body width is 43 world units.
            let sourceToWorld = (character.radiusWorldUnits ?? 25) * 2 * 0.86 / 512
            let spriteWidth = Double(sprite.width) * sourceToWorld * mapScale * actualScaleX
            let spriteHeight = Double(sprite.height) * sourceToWorld * mapScale * actualScaleY
            NSGraphicsContext.saveGraphicsState()
            let transform = NSAffineTransform()
            transform.translateX(by: (character.x - rect.minX) * actualScaleX,
                                 yBy: Double(outputHeight) - (character.y - rect.minY) * actualScaleY)
            // Screen-space positive angles are clockwise; AppKit uses +Y up.
            transform.rotate(byDegrees: -(character.angleDegrees ?? 0))
            transform.concat()
            sprite.image.draw(in: NSRect(x: -spriteWidth / 2, y: -spriteHeight / 2,
                                        width: spriteWidth, height: spriteHeight),
                              from: NSRect(x: 0, y: 0, width: sprite.width, height: sprite.height),
                              operation: .sourceOver, fraction: 1)
            NSGraphicsContext.restoreGraphicsState()
        }
        NSGraphicsContext.restoreGraphicsState()
        let outputURL = URL(fileURLWithPath: resolvedPath(destination, relativeTo: configDirectory))
        try FileManager.default.createDirectory(at: outputURL.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        guard let png = bitmap.representation(using: .png, properties: [:]) else {
            throw PreviewError.message("Cannot encode preview PNG.")
        }
        try png.write(to: outputURL)
        print("\(outputURL.path) (\(outputWidth)×\(outputHeight), body width \(43 * mapScale * actualScaleX)px)")
    }

    try render(rect: NSRect(x: 0, y: 0, width: map.width, height: map.height),
               scale: outputScale, destination: config.outputPath)
    for crop in (config.crops ?? []) + (config.crop.map { [$0] } ?? []) {
        try render(rect: NSRect(x: crop.x, y: crop.y, width: crop.width, height: crop.height),
                   scale: crop.scale ?? (2 / mapScale), destination: crop.outputPath)
    }
    print("Map scale: \(mapScale) pixels/world-unit. CameraZoom=2 matching crop scale: \(2 / mapScale)×.")
} catch {
    fputs("Preview error: \(error)\n", stderr)
    exit(1)
}
