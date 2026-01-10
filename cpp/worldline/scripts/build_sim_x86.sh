#!/usr/bin/env bash
set -euo pipefail
ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)
BUILD_DIR="$ROOT_DIR/build_xcode"

mkdir -p "$BUILD_DIR"
cd "$ROOT_DIR"
cmake -S . -B "$BUILD_DIR" -G Xcode -DCMAKE_BUILD_TYPE=Release
xcodebuild -project "$BUILD_DIR/worldline.xcodeproj" -scheme worldline -sdk iphonesimulator -configuration Release -arch x86_64 build
cp "$BUILD_DIR/Release/libworldline.dylib" "$BUILD_DIR/libworldline-iphonesim-x86_64.dylib" 2>/dev/null || true

echo "Built simulator x86_64 -> $BUILD_DIR/libworldline-iphonesim-x86_64.dylib"