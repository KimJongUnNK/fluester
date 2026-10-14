// swift-tools-version:5.10
// Flüster – Diktieren in jeder Mac-App. Halten, sprechen, loslassen.
import PackageDescription

let package = Package(
    name: "Fluester",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "Fluester", targets: ["Fluester"]),
        .library(name: "FluesterCore", targets: ["FluesterCore"])
    ],
    targets: [
        // Reine Logik ohne AppKit: Textbereinigung, Wörterbuch, Snippets, Stile, Prompts, WAV, API-Formate
        .target(name: "FluesterCore"),
        // Die App: Menüleiste, Hotkey, Mikrofon, Spracherkennung, KI, Einfügen, Einstellungen
        .executableTarget(
            name: "Fluester",
            dependencies: ["FluesterCore"],
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("AVFoundation"),
                .linkedFramework("Speech"),
                .linkedFramework("ApplicationServices"),
                .linkedFramework("Carbon"),
                .linkedFramework("ServiceManagement")
            ]
        ),
        .testTarget(name: "FluesterCoreTests", dependencies: ["FluesterCore"])
    ]
)
