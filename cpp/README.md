# Building (archived Bazel instructions)

Bazel-based build instructions are archived in `cpp/archived_builds/` and are no longer the default workflow.

To build **worldline for iOS** with the supported flow (CMake + Xcode):

1. Ensure CMake and Xcode are installed.
2. `cd` to `cpp/worldline`.
3. Run:
   ```bash
   cmake -S . -B build_xcode -G Xcode -DCMAKE_BUILD_TYPE=Release
   xcodebuild -project build_xcode/worldline.xcodeproj -scheme worldline -sdk iphonesimulator -configuration Release -arch arm64 build
   xcodebuild -project build_xcode/worldline/build_xcode/worldline.xcodeproj -scheme worldline -sdk iphoneos -configuration Release -arch arm64 build CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO
   ```

If Bazel is required again, see the original instructions in `cpp/archived_builds/`.
