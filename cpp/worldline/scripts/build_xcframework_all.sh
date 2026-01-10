#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)
BUILD_DIR="$ROOT_DIR/build_xcode"
XC_OUT="$BUILD_DIR/worldline.xcframework"
INCLUDE_DIR="$BUILD_DIR/include"
RUNTIMES_DIR="$ROOT_DIR/runtimes/ios"

# Prefer explicit Xcode toolchain for reliability, but allow override
XCODEBUILD=${XCODEBUILD:-/Applications/Xcode.app/Contents/Developer/usr/bin/xcodebuild}

mkdir -p "$BUILD_DIR" "$INCLUDE_DIR" "$RUNTIMES_DIR"
cd "$ROOT_DIR"

echo "Configuring with CMake..."
cmake -S . -B "$BUILD_DIR" -G Xcode -DCMAKE_BUILD_TYPE=Release

# Build simulator x86_64
echo "Building simulator x86_64..."
$XCODEBUILD -project "$BUILD_DIR/worldline.xcodeproj" -scheme worldline -sdk iphonesimulator -configuration Release -arch x86_64 build
cp "$BUILD_DIR/Release/libworldline.dylib" "$BUILD_DIR/libworldline-iphonesim-x86_64.dylib" 2>/dev/null || true

# Build simulator arm64
echo "Building simulator arm64..."
$XCODEBUILD -project "$BUILD_DIR/worldline.xcodeproj" -scheme worldline -sdk iphonesimulator -configuration Release -arch arm64 build
cp "$BUILD_DIR/Release/libworldline.dylib" "$BUILD_DIR/libworldline-iphonesim-arm64.dylib" 2>/dev/null || true

# Build device arm64 (disable signing for local builds)
echo "Building device arm64..."
$XCODEBUILD -project "$BUILD_DIR/worldline.xcodeproj" -scheme worldline -sdk iphoneos -configuration Release -arch arm64 build CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO || true
cp "$BUILD_DIR/Release/libworldline.dylib" "$BUILD_DIR/libworldline-iphoneos-arm64.dylib" 2>/dev/null || true

# Prepare headers
if [ -f "$ROOT_DIR/worldline.h" ]; then
  cp -f "$ROOT_DIR/worldline.h" "$INCLUDE_DIR/"
else
  echo "Warning: worldline.h not found in source root; XCFramework headers may be incomplete" >&2
fi

# Locate lipo tool (use xcrun or which as fallback)
LIPO=${LIPO:-$(xcrun -f lipo 2>/dev/null || which lipo || true)}
if [ -z "$LIPO" ] || [ ! -x "$LIPO" ]; then
  echo "Error: 'lipo' tool not found. Ensure Xcode command line tools are installed or lipo is available in PATH." >&2
  exit 1
fi

# Merge simulator slices into fat dylib (if both present)
SIM_LIB=""
if [ -f "$BUILD_DIR/libworldline-iphonesim-arm64.dylib" ] && [ -f "$BUILD_DIR/libworldline-iphonesim-x86_64.dylib" ]; then
  echo "Merging simulator slices into fat dylib..."
  "$LIPO" -create "$BUILD_DIR/libworldline-iphonesim-arm64.dylib" "$BUILD_DIR/libworldline-iphonesim-x86_64.dylib" -output "$BUILD_DIR/libworldline-iphonesim-fat.dylib"
  SIM_LIB="$BUILD_DIR/libworldline-iphonesim-fat.dylib"
elif [ -f "$BUILD_DIR/libworldline-iphonesim-arm64.dylib" ]; then
  SIM_LIB="$BUILD_DIR/libworldline-iphonesim-arm64.dylib"
elif [ -f "$BUILD_DIR/libworldline-iphonesim-x86_64.dylib" ]; then
  SIM_LIB="$BUILD_DIR/libworldline-iphonesim-x86_64.dylib"
else
  echo "Error: No simulator library found to include in XCFramework" >&2
  exit 1
fi

# Create XCFramework
echo "Creating XCFramework at: $XC_OUT"
rm -rf "$XC_OUT" || true
$XCODEBUILD -create-xcframework \
  -library "$BUILD_DIR/libworldline-iphoneos-arm64.dylib" -headers "$INCLUDE_DIR" \
  -library "$SIM_LIB" -headers "$INCLUDE_DIR" \
  -output "$XC_OUT"

# Copy to runtimes for MAUI
rm -rf "$RUNTIMES_DIR/worldline.xcframework" || true
mkdir -p "$RUNTIMES_DIR"
cp -R "$XC_OUT" "$RUNTIMES_DIR/"

echo "XCFramework created at: $XC_OUT and copied to $RUNTIMES_DIR/"
