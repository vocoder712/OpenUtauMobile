Third-party notices for code adapted into this repository.

Avalonia Fluent theme source

- Source: https://github.com/AvaloniaUI/Avalonia/tree/12.1.0/src/Avalonia.Themes.Fluent
- Copyright (c) AvaloniaUI OÜ. MIT License.
- License: licenses/Avalonia.Themes.Fluent.MIT.txt
- Adapted XAML resides in OpenUtauMobile/Themes/OpenUtauMobile/{Controls,Accents,Strings,DensityStyles}.
- Changes: local resource URIs/private theme names, complete independent entry point,
  semantic palettes, shared button templates, input/state/cursor handling and metrics.
- The pinned commit, original SHA-256 hashes and template contracts are recorded in
  OpenUtauMobile/Themes/OpenUtauMobile/UPSTREAM.json.

OpenUtau.Core

- Source project: OpenUtau
- License: MIT License
- License file retained in repository: licenses/OpenUtau.MIT.txt
- Scope: the OpenUtau.Core project and the code that depends on it remain subject to the MIT terms from the upstream project.

HifiSampler

- Source project: hifisampler
- Source location used during development: hifisampler-main/hifisampler-main
- License: Apache License 2.0
- Files in this repository containing adapted logic:
  - OpenUtau.Plugin.Builtin/HifiSampler/HifiSamplerDsp.cs
  - OpenUtau.Plugin.Builtin/HifiSampler/HifiSamplerRenderer.cs
- Nature of changes: the original Python-based pipeline was adapted and rewritten for OpenUtauMobile2 in C#, integrated into the renderer architecture, and modified for the project's runtime, dependency, and rendering systems.

The full license text used for this dependency is included in licenses/HifiSampler.Apache-2.0.txt.
