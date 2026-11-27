import AppKit
import CoreGraphics
import Foundation

struct IconSize {
    let filename: String
    let pixels: Int
}

func color(_ hex: UInt32) -> CGColor {
    CGColor(
        red: CGFloat((hex >> 16) & 0xFF) / 255,
        green: CGFloat((hex >> 8) & 0xFF) / 255,
        blue: CGFloat(hex & 0xFF) / 255,
        alpha: 1
    )
}

func polygon(
    _ context: CGContext,
    _ points: [CGPoint],
    fill: UInt32,
    stroke: UInt32? = nil,
    lineWidth: CGFloat = 1
) {
    guard let first = points.first else { return }
    let path = CGMutablePath()
    path.move(to: first)
    for point in points.dropFirst() {
        path.addLine(to: point)
    }
    path.closeSubpath()
    context.addPath(path)
    context.setFillColor(color(fill))
    if let stroke {
        context.setStrokeColor(color(stroke))
        context.setLineWidth(lineWidth)
        context.drawPath(using: .fillStroke)
    } else {
        context.fillPath()
    }
}

func drawIcon(size: Int, to destination: URL) throws {
    guard let context = CGContext(
        data: nil,
        width: size,
        height: size,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else {
        throw NSError(domain: "AuralitaIcon", code: 1, userInfo: [
            NSLocalizedDescriptionKey: "Could not create icon drawing context."
        ])
    }

    let scale = CGFloat(size) / 512
    context.scaleBy(x: scale, y: scale)

    let background = CGPath(
        roundedRect: CGRect(x: 16, y: 16, width: 480, height: 480),
        cornerWidth: 96,
        cornerHeight: 96,
        transform: nil
    )
    context.addPath(background)
    context.setFillColor(color(0x24232A))
    context.fillPath()
    context.addPath(background)
    context.setStrokeColor(color(0x595661))
    context.setLineWidth(4)
    context.strokePath()

    context.saveGState()
    context.translateBy(x: 80, y: 432)
    context.scaleBy(x: 5.5, y: -5.5)

    polygon(context, [
        CGPoint(x: 25, y: 5), CGPoint(x: 39, y: 5), CGPoint(x: 49, y: 23),
        CGPoint(x: 32, y: 60), CGPoint(x: 15, y: 23)
    ], fill: 0x65459B, stroke: 0x35234F, lineWidth: 1.5)
    polygon(context, [
        CGPoint(x: 25, y: 5), CGPoint(x: 32, y: 23), CGPoint(x: 15, y: 23)
    ], fill: 0xB49ADA)
    polygon(context, [
        CGPoint(x: 39, y: 5), CGPoint(x: 32, y: 23), CGPoint(x: 49, y: 23)
    ], fill: 0x8B6ABD)
    polygon(context, [
        CGPoint(x: 15, y: 23), CGPoint(x: 32, y: 23), CGPoint(x: 32, y: 60)
    ], fill: 0x7756AA)
    polygon(context, [
        CGPoint(x: 32, y: 23), CGPoint(x: 49, y: 23), CGPoint(x: 32, y: 60)
    ], fill: 0x50367F)
    polygon(context, [
        CGPoint(x: 5, y: 31), CGPoint(x: 15, y: 25), CGPoint(x: 24, y: 37),
        CGPoint(x: 17, y: 54), CGPoint(x: 7, y: 44)
    ], fill: 0x8061B3, stroke: 0x493263, lineWidth: 1.2)
    polygon(context, [
        CGPoint(x: 5, y: 31), CGPoint(x: 15, y: 25), CGPoint(x: 15, y: 40),
        CGPoint(x: 7, y: 44)
    ], fill: 0xB49ADA)
    polygon(context, [
        CGPoint(x: 40, y: 37), CGPoint(x: 50, y: 25), CGPoint(x: 59, y: 32),
        CGPoint(x: 57, y: 44), CGPoint(x: 47, y: 54)
    ], fill: 0x71529F, stroke: 0x493263, lineWidth: 1.2)
    polygon(context, [
        CGPoint(x: 50, y: 25), CGPoint(x: 59, y: 32), CGPoint(x: 57, y: 44),
        CGPoint(x: 49, y: 40)
    ], fill: 0x9B7ACB)
    polygon(context, [
        CGPoint(x: 28, y: 12), CGPoint(x: 32, y: 9), CGPoint(x: 36, y: 12),
        CGPoint(x: 32, y: 16)
    ], fill: 0xD99682)
    context.restoreGState()

    guard let image = context.makeImage() else {
        throw NSError(domain: "AuralitaIcon", code: 2, userInfo: [
            NSLocalizedDescriptionKey: "Could not render icon image."
        ])
    }
    let bitmap = NSBitmapImageRep(cgImage: image)
    guard let png = bitmap.representation(using: .png, properties: [:]) else {
        throw NSError(domain: "AuralitaIcon", code: 3, userInfo: [
            NSLocalizedDescriptionKey: "Could not encode icon PNG."
        ])
    }
    try png.write(to: destination)
}

guard CommandLine.arguments.count == 2 else {
    fatalError("Usage: generate-macos-icon.swift <iconset-directory>")
}

let iconset = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)

let sizes = [
    IconSize(filename: "icon_16x16.png", pixels: 16),
    IconSize(filename: "icon_16x16@2x.png", pixels: 32),
    IconSize(filename: "icon_32x32.png", pixels: 32),
    IconSize(filename: "icon_32x32@2x.png", pixels: 64),
    IconSize(filename: "icon_128x128.png", pixels: 128),
    IconSize(filename: "icon_128x128@2x.png", pixels: 256),
    IconSize(filename: "icon_256x256.png", pixels: 256),
    IconSize(filename: "icon_256x256@2x.png", pixels: 512),
    IconSize(filename: "icon_512x512.png", pixels: 512),
    IconSize(filename: "icon_512x512@2x.png", pixels: 1024)
]

for iconSize in sizes {
    try drawIcon(size: iconSize.pixels, to: iconset.appendingPathComponent(iconSize.filename))
}
