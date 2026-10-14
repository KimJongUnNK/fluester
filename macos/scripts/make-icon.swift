// Zeichnet das App-Symbol (1024 × 1024 PNG): macOS-Squircle, tiefer Verlauf, weiße Wellenform.
// Aufruf: swift scripts/make-icon.swift ausgabe.png
import AppKit

let out = CommandLine.arguments.dropFirst().first ?? "AppIcon.png"
let size = 1024
guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4,
                                 hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { exit(1) }
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
let ctx = NSGraphicsContext.current!.cgContext
ctx.clear(CGRect(x: 0, y: 0, width: size, height: size))

// macOS-Raster: 824 px Fläche mit 100 px Rand, Ecken ≈ 185 px
let rect = NSRect(x: 100, y: 100, width: 824, height: 824)
let shape = NSBezierPath(roundedRect: rect, xRadius: 185, yRadius: 185)

// Schatten
ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 28, color: NSColor.black.withAlphaComponent(0.35).cgColor)
NSColor.black.setFill()
shape.fill()
ctx.restoreGState()

// Verlauf: Nachtblau → Violett → Magenta
let gradient = NSGradient(colors: [
    NSColor(srgbRed: 0.06, green: 0.07, blue: 0.20, alpha: 1),
    NSColor(srgbRed: 0.31, green: 0.16, blue: 0.78, alpha: 1),
    NSColor(srgbRed: 0.86, green: 0.25, blue: 0.62, alpha: 1)
], atLocations: [0, 0.62, 1], colorSpace: .sRGB)!
gradient.draw(in: shape, angle: 60)

// Glanz oben
ctx.saveGState()
shape.addClip()
let gloss = NSGradient(colors: [NSColor.white.withAlphaComponent(0.18), NSColor.white.withAlphaComponent(0)])!
gloss.draw(in: NSRect(x: 100, y: 520, width: 824, height: 404), angle: -90)
ctx.restoreGState()

// Wellenform: 9 Balken, symmetrisch
let heights: [CGFloat] = [0.20, 0.38, 0.62, 0.86, 1.0, 0.86, 0.62, 0.38, 0.20]
let barW: CGFloat = 46, gap: CGFloat = 30
let total = CGFloat(heights.count) * barW + CGFloat(heights.count - 1) * gap
var x = 512 - total / 2
ctx.saveGState()
ctx.setShadow(offset: .zero, blur: 30, color: NSColor.white.withAlphaComponent(0.45).cgColor)
for h in heights {
    let barH = 70 + h * 380
    let bar = NSBezierPath(roundedRect: NSRect(x: x, y: 512 - barH / 2, width: barW, height: barH), xRadius: barW / 2, yRadius: barW / 2)
    NSColor.white.setFill()
    bar.fill()
    x += barW + gap
}
ctx.restoreGState()

NSGraphicsContext.restoreGraphicsState()
guard let png = rep.representation(using: .png, properties: [:]) else { exit(1) }
try! png.write(to: URL(fileURLWithPath: out))
print("Symbol: \(out)")
