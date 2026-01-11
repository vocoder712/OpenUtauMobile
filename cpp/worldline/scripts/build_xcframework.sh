#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)
BUILD_DIR="$ROOT_DIR/build_xcode"
XCFRAMEWORK_OUT="$ROOT_DIR/build_xcode/worldline.xcframework"
ARTIFACTS_DIR="$ROOT_DIR/runtimes/ios"

mkdir -p "$BUILD_DIR"
cd "$ROOT_DIR"

# Configure
cmake -S . -B "$BUILD_DIR" -G Xcode -DCMAKE_BUILD_TYPE=Release

# Build simulator (arm64)
xcodebuild -project "$BUILD_DIR/worldline.xcodeproj" -scheme worldline -sdk iphonesimulator -configuration Release -arch arm64 build

# Build device (arm64) - disable signing for local builds
xcodebuild -project "$BUILD_DIR/worldline.xcodeproj" -scheme worldline -sdk iphoneos -configuration Release -arch arm64 build CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO || true

# Copy products
cp "$BUILD_DIR/Release/libworldline.dylib" "$BUILD_DIR/libworldline-iphoneos.dylib" 2>/dev/null || true
cp "$BUILD_DIR/Release/libworldline.dylib" "$BUILD_DIR/libworldline-iphonesim.dylib" 2>/dev/null || true

# Create minimal headers dir
mkdir -p "$BUILD_DIR/include"
cp worldline.h "$BUILD_DIR/include/" || true

# Create XCFramework
rm -rf "$XCFRAMEWORK_OUT"
xcodebuild -create-xcframework \
  -library "$BUILD_DIR/libworldline-iphoneos.dylib" -headers "$BUILD_DIR/include" \
  -library "$BUILD_DIR/libworldline-iphonesim.dylib" -headers "$BUILD_DIR/include" \
  -output "$XCFRAMEWORK_OUT"

# Copy to runtimes for MAUI
mkdir -p "$ARTIFACTS_DIR"
cp -R "$XCFRAMEWORK_OUT" "$ARTIFACTS_DIR/"

echo "XCFramework created at: $XCFRAMEWORK_OUT and copied to $ARTIFACTS_DIR"