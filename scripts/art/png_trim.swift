// Trim a PNG to the bounding box of its visible pixels (alpha > 8), keeping a
// small margin — building art is sized by its visible extent on the globe.
// usage: swift scripts/art/png_trim.swift <in.png> <out.png> [margin px]
import AppKit

let args = CommandLine.arguments
let margin = args.count > 3 ? Int(args[3])! : 4
guard let img = NSImage(contentsOfFile: args[1]),
      let tiff = img.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff) else { exit(1) }
let w = rep.pixelsWide, h = rep.pixelsHigh
var minX = w, minY = h, maxX = -1, maxY = -1
for y in 0..<h { for x in 0..<w {
    if let c = rep.colorAt(x: x, y: y), c.alphaComponent > 8.0 / 255.0 {
        minX = min(minX, x); maxX = max(maxX, x); minY = min(minY, y); maxY = max(maxY, y)
    }
} }
guard maxX >= 0 else { exit(1) }
minX = max(0, minX - margin); minY = max(0, minY - margin)
maxX = min(w - 1, maxX + margin); maxY = min(h - 1, maxY + margin)
let cw = maxX - minX + 1, ch = maxY - minY + 1
let out = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: cw, pixelsHigh: ch, bitsPerSample: 8,
    samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
for y in 0..<ch { for x in 0..<cw { out.setColor(rep.colorAt(x: minX + x, y: minY + y)!, atX: x, y: y) } }
try! out.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: args[2]))
print("\(args[2]): \(cw)x\(ch)")
