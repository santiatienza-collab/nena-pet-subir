// swift-tools-version:5.5
import PackageDescription

let package = Package(
    name: "NenaPet",
    platforms: [.macOS(.v11)],
    targets: [
        .executableTarget(name: "NenaPet", path: "Sources/NenaPet")
    ]
)
