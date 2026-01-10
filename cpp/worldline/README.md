Worldline iOS build notes

Vendored WORLD

This project can use a vendored copy of the WORLD resynthesis library to avoid downloading remote dependencies during configure/build.

If a vendored copy exists at:

  ../third_party/world/

then CMake will prefer that directory and use its CMake build. The vendor copy should include the original `LICENSE.txt` from WORLD.

If no vendored copy is present, the build will fall back to any local `build_xcode/_deps/world-src` or to FetchContent (which tries to download from https://github.com/mm-sv/world).

To reproduce a build without network access, provide a vendored `third_party/world/` or a prebuilt `libWORLD` in `build_xcode/_deps/world-build/Release/` or `runtimes/prebuilt/`.

---

## 🚀 Quick start — build iOS XCFramework

Prerequisites:
- macOS + Xcode (ensure correct Xcode with `xcode-select -p`)
- CMake (>= 3.20 recommended; for old WORLD you may pass `-DCMAKE_POLICY_VERSION_MINIMUM=3.5`)
- Xcode command line tools (`xcrun`, `lipo`, `xcodebuild`)

Vendored WORLD (recommended):
- If `third_party/world/` is present the build will prefer it (LICENSE preserved).

One-shot build (automated script):
```bash
cd cpp/worldline
./scripts/build_xcframework_all.sh
```

Environment variables (optional):
- `XCODEBUILD` — override xcodebuild path
- `LIPO` — override lipo path

Manual build steps (for debugging):
```bash
cmake -S . -B build_xcode -G Xcode -DCMAKE_BUILD_TYPE=Release
./scripts/build_sim_x86.sh
./scripts/build_sim_arm64.sh
./scripts/build_device_arm64.sh
lipo -create build_xcode/libworldline-iphonesim-arm64.dylib build_xcode/libworldline-iphonesim-x86_64.dylib -output build_xcode/libworldline-iphonesim-fat.dylib
xcodebuild -create-xcframework \
  -library build_xcode/libworldline-iphoneos-arm64.dylib -headers build_xcode/include \
  -library build_xcode/libworldline-iphonesim-fat.dylib -headers build_xcode/include \
  -output build_xcode/worldline.xcframework
cp -R build_xcode/worldline.xcframework runtimes/ios/
```

Artifact location:
- `cpp/worldline/runtimes/ios/worldline.xcframework` (ready for MAUI)

---

## 📦 Vendor & dependency strategy
- Priority: `third_party/world/` → local `_deps/world-src` → FetchContent (default uses `https://github.com/mmorise/World.git`).
- For reproducible builds, either vendor the source or publish prebuilt XCFrameworks as Release assets.

## ⚙️ CI suggestion (brief)
- macOS runner: checkout → `./scripts/build_xcframework_all.sh` → upload `build_xcode/worldline.xcframework` as Release asset.
- Prefer publishing artifacts via CI instead of committing large binaries to the repo.

## 📌 Notes
- `cpp/worldline/build_xcode/` is ignored in `.gitignore` (build outputs are not checked in).
- `third_party/world/LICENSE.txt` should keep the original license notice.

