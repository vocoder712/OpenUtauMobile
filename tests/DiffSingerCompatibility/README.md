# DiffSinger Mobile compatibility regression checks

From the repository root (PowerShell):

```powershell
./tests/DiffSingerCompatibility/run.ps1
```

The default baseline is the `dev` commit merged into PR #287 (Core
`0.1.570.1-alpha-2b03ad562fa6`). Override with `-Baseline <commit>` when auditing
another upstream revision. The script reads that revision directly from Git,
compiles its speaker manager under a different class name for differential
comparison, and writes generated fixtures under ignored `artifacts/pr287`.

The executable calls the actual production methods, using reflection only to
construct immutable render fixtures and reach private installer/phonemizer
methods. It checks:

- Phone embedding shape, orientation and one-hot selection.
- Voice Color 0/50/100%, multiple curves over 100%, negative and tiny weights.
- Padded inter-phone silence, CLR transitions, head and tail mapping.
- Exact/path suffix matching, missing-suffix warning deduplication, invalid lists.
- Both duration-config layouts, `dsdur` priority and distinct Location/config errors.
- All subbank fallback levels, missing subbanks, disabled and empty speakers.
- Archive type detection, Enunu priority, explicit declarations and warning fallback.
- Phone results and 100 seeded multi-curve/gap phrases against upstream `np.dot`
  (absolute float tolerance `1e-5`).
- Unchanged frame mapping/weight source and identical G2P/Localization Git blobs.

Config-layout tests stop deliberately at G2P loading after checking the selected
config; they do not require ONNX models or represent an end-to-end voicebank render.
The executable runs on the host runtime, not Android. Android Release builds and
real-device DiffSinger rendering must be checked separately; a passing host test
or APK build does not establish that the original device crash is resolved.

To run just the production regression checks without the upstream comparison:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet run --project tests/DiffSingerCompatibility
```
