// Run from the repository root. Both paths are optional:
// env DEVELOPER_DIR=/Library/Developer/CommandLineTools \
//   /Library/Developer/CommandLineTools/usr/bin/swift \
//   tools/PolRob.CharacterAnimation/make-preview.swift [input-directory] [output.gif]
import Foundation
import CoreGraphics
import CoreText
import ImageIO
import UniformTypeIdentifiers

struct PreviewError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}

struct Pose {
    let police: String
    let robber: String
    let policeLabel: String
    let robberLabel: String
    let delay: Double
}

let arguments = Array(CommandLine.arguments.dropFirst())
if arguments.count > 2 {
    throw PreviewError("Usage: make-preview.swift [input-directory] [output.gif]")
}
let input = URL(fileURLWithPath: arguments.first ?? "docs/character-animation/result", isDirectory: true)
let output = URL(fileURLWithPath: arguments.dropFirst().first ?? "docs/character-animation/animation.gif")
let width = 900, height = 600
let canvasSize: CGFloat = 1088
let bodyWidth: CGFloat = 512
let pivot: CGFloat = 544

let run = (1...8).map { index in
    Pose(police: "char_police_run_\(index).png", robber: "char_robber_run_\(index).png",
         policeLabel: "달리기 \(index) / 8", robberLabel: "달리기 \(index) / 8", delay: 0.1)
}
let poses = [Pose(police: "char_police.png", robber: "char_robber.png",
                  policeLabel: "기본", robberLabel: "기본", delay: 1.0)]
    + run + run
    + [Pose(police: "char_police_arrest.png", robber: "char_robber_surrend.png",
            policeLabel: "체포", robberLabel: "항복", delay: 1.1),
       Pose(police: "char_police.png", robber: "char_robber_prison_break.png",
            policeLabel: "기본", robberLabel: "탈옥", delay: 1.1)]
    + run

func color(_ red: CGFloat, _ green: CGFloat, _ blue: CGFloat) -> CGColor {
    CGColor(red: red / 255, green: green / 255, blue: blue / 255, alpha: 1)
}

let ink = color(30, 43, 57)
let muted = color(91, 103, 114)
let border = color(217, 216, 211)
let background = color(247, 247, 242)
let font = CTFontCreateWithName("AppleSDGothicNeo-Regular" as CFString, 15, nil)
let headingFont = CTFontCreateWithName("AppleSDGothicNeo-Bold" as CFString, 20, nil)

func text(_ value: String, at center: CGFloat, baseline: CGFloat,
          in context: CGContext, heading: Bool = false) {
    let attributes: [NSAttributedString.Key: Any] = [
        NSAttributedString.Key(kCTFontAttributeName as String): heading ? headingFont : font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): heading ? ink : muted
    ]
    let line = CTLineCreateWithAttributedString(NSAttributedString(string: value, attributes: attributes))
    let lineWidth = CGFloat(CTLineGetTypographicBounds(line, nil, nil, nil))
    context.saveGState()
    context.translateBy(x: center - lineWidth / 2, y: baseline)
    context.scaleBy(x: 1, y: -1)
    context.textMatrix = .identity
    context.textPosition = .zero
    CTLineDraw(line, context)
    context.restoreGState()
}

func draw(_ sprite: CGImage, centerX: CGFloat, centerY: CGFloat,
          diameter: CGFloat, in context: CGContext) {
    let scale = diameter * 0.86 / bodyWidth
    let destination = CGRect(x: centerX - pivot * scale, y: centerY - pivot * scale,
                             width: canvasSize * scale, height: canvasSize * scale)
    context.saveGState()
    context.translateBy(x: destination.minX, y: destination.maxY)
    context.scaleBy(x: 1, y: -1)
    context.draw(sprite, in: CGRect(origin: .zero, size: destination.size))
    context.restoreGState()
}

var sprites: [String: CGImage] = [:]
for file in Set(poses.flatMap { [$0.police, $0.robber] }) {
    let url = input.appendingPathComponent(file)
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        throw PreviewError("Cannot decode \(url.path)")
    }
    guard image.width == Int(canvasSize), image.height == Int(canvasSize) else {
        throw PreviewError("Expected 1088×1088: \(file) is \(image.width)×\(image.height)")
    }
    sprites[file] = image
}

try FileManager.default.createDirectory(at: output.deletingLastPathComponent(), withIntermediateDirectories: true)
guard let destination = CGImageDestinationCreateWithURL(output as CFURL, UTType.gif.identifier as CFString,
                                                       poses.count, nil) else {
    throw PreviewError("Cannot create \(output.path)")
}
CGImageDestinationSetProperties(destination, [kCGImagePropertyGIFDictionary: [kCGImagePropertyGIFLoopCount: 0]] as CFDictionary)

for pose in poses {
    guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8,
                                  bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(),
                                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
        throw PreviewError("Cannot create preview canvas")
    }
    context.translateBy(x: 0, y: CGFloat(height))
    context.scaleBy(x: 1, y: -1)
    context.interpolationQuality = .high
    context.setShouldAntialias(true)
    context.setFillColor(background)
    context.fill(CGRect(x: 0, y: 0, width: width, height: height))
    context.setStrokeColor(border)
    context.setLineWidth(1)
    context.move(to: CGPoint(x: 450, y: 18))
    context.addLine(to: CGPoint(x: 450, y: 580))
    context.strokePath()

    let columns: [(CGFloat, String, String, String)] = [
        (225, "경찰", pose.police, pose.policeLabel),
        (675, "도둑", pose.robber, pose.robberLabel)
    ]
    for (center, role, file, label) in columns {
        guard let sprite = sprites[file] else { throw PreviewError("Missing cached sprite: \(file)") }
        text("\(role) · \(label)", at: center, baseline: 28, in: context, heading: true)
        draw(sprite, centerX: center, centerY: 214, diameter: 190, in: context)
        text("확대 보기 · 원본 몸체 고정, 팔만 동작", at: center, baseline: 393, in: context)
        draw(sprite, centerX: center, centerY: 489, diameter: 50, in: context)
        text("게임 기준 · 지름 50 / 몸통 너비 43", at: center, baseline: 593, in: context)
    }
    guard let frame = context.makeImage() else { throw PreviewError("Cannot render GIF frame") }
    let properties: [CFString: Any] = [
        kCGImagePropertyGIFDictionary: [
            kCGImagePropertyGIFDelayTime: pose.delay,
            kCGImagePropertyGIFUnclampedDelayTime: pose.delay
        ]
    ]
    CGImageDestinationAddImage(destination, frame, properties as CFDictionary)
}
guard CGImageDestinationFinalize(destination) else { throw PreviewError("Cannot finish \(output.path)") }

// Read back the encoded artifact, including its frame count and timing.
guard let verification = CGImageSourceCreateWithURL(output as CFURL, nil),
      CGImageSourceGetCount(verification) == poses.count else {
    throw PreviewError("GIF verification failed")
}
for index in poses.indices {
    guard let properties = CGImageSourceCopyPropertiesAtIndex(verification, index, nil) as? [CFString: Any],
          let gif = properties[kCGImagePropertyGIFDictionary] as? [CFString: Any],
          let delay = gif[kCGImagePropertyGIFUnclampedDelayTime] as? Double
            ?? gif[kCGImagePropertyGIFDelayTime] as? Double,
          abs(delay - poses[index].delay) < 0.011 else {
        throw PreviewError("GIF frame timing verification failed at frame \(index)")
    }
}
let duration = poses.reduce(0) { $0 + $1.delay }
print("Saved \(output.path) — \(width)×\(height), \(poses.count) frames, \(String(format: "%.1f", duration)) seconds, loops forever; verified frame timings.")
