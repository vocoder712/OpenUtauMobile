# Contributing to OpenUtau Mobile

[简体中文](CONTRIBUTING.md) | **English**

Thank you for your interest in contributing to OpenUtau Mobile!

Contributions of all kinds are welcome, including code, bug fixes, documentation, translations, UI/UX improvements, testing, and technical discussions.

OpenUtau Mobile is currently under active development. Architecture, APIs, and platform-specific implementations may change frequently, so please keep changes focused and coordinate large changes before investing significant work.

## Ways to contribute

You can contribute by:

* fixing bugs;
* implementing new features;
* improving performance;
* improving UI/UX;
* adding or improving platform support;
* improving documentation and translations;
* testing builds on different devices and platforms;
* reviewing Pull Requests;
* participating in Issues and Discussions.

For substantial new features or architectural changes, please discuss the proposal before starting implementation. This helps avoid duplicated work and ensures that the change fits the current project direction.

Other planned development tasks can be found in [TODO](https://docs.qq.com/sheet/DV2NuakZtQW1LZUNS).

---

## Setting up your development environment

### 1. Fork and clone the repository

Fork the repository on GitHub, then clone your fork:

```bash
git clone https://github.com/<your-username>/OpenUtauMobile.git
cd OpenUtauMobile
```

Add the official repository as an upstream remote:

```bash
git remote add upstream https://github.com/vocoder712/OpenUtauMobile.git
```

You can verify the remotes with:

```bash
git remote -v
```

### 2. Switch to the development branch

Active development takes place on `dev`.

```bash
git switch dev
```

Update it before starting new work:

```bash
git fetch upstream
git pull --ff-only upstream dev
```

Do not base new contributions on `master` unless the change specifically targets the legacy version.

### 3. Install the required toolchain

The exact .NET SDK version is defined by [`global.json`](global.json).

Project-specific target frameworks and platform requirements are defined by the corresponding project files (`*.csproj`).

These repository files are the source of truth for SDK and target framework versions.

Verify your .NET installation with:

```bash
dotnet --version
dotnet --info
```

For Android development, you will also need the Android SDK and the workloads required by the currently configured .NET SDK.

### 4. Restore dependencies

From the repository root:

```bash
dotnet restore
```

### 5. IDE

Common choices include:

* JetBrains Rider
* Visual Studio
* Visual Studio Code with C# tooling

Whichever editor you use, make sure it respects the repository's [`.editorconfig`](.editorconfig).

---

## Branches

### `dev`

`dev` is the main development and integration branch for OpenUtau Mobile V2.

Normal contributions should target this branch.

### `master`

`master` contains the legacy generation of OpenUtau Mobile.

It is not the base branch for normal V2 development.

Unless explicitly required, Pull Requests should not target `master`.

---

## Code style

Follow the existing code style and project architecture.

In particular:

* respect the repository's `.editorconfig`;
* keep platform-independent logic out of platform-specific projects when possible;
* prefer focused changes that are easy to review;
* preserve compatibility with the architectures and platforms affected by your change.

---

## Commit messages

OpenUtau Mobile follows a lightweight form of the Conventional Commits convention.

Preferred format:

```text
<type>: <description>
```

Common types include:

```text
feature
fix
performance
refactor
docs
test
build
ci
chore
```

Keep the first line short and describe what the commit changes.

For larger commits, add a body explaining why the change was necessary.

Example:

```text
feature: add a phoneme and parameter panel and translations

Add a collapsible phoneme and parameter panel beneath the piano roll to support editing phoneme timing, aliases, overlap, preutterance, and expression parameters.

The panel has four editing modes: Simple Phoneme (timing and aliases), Advanced Phoneme (timing, preutterance, and overlap handles), Parameter Draw, and Parameter Erase.
```

If a commit relates to an Issue or Pull Request, reference it where useful:

```text
Fixes #123
Refs #456
```

Perfect commit history is appreciated but not required for every contribution. Pull Requests may be squashed during merge.

---

## Pull Request process

### 1. Keep your branch up to date

Before opening or updating a Pull Request:

```bash
git fetch upstream
```

If the branch is private to you, rebasing onto the latest `dev` is preferred:

```bash
git rebase upstream/dev
```

If you have already pushed the rebased branch:

```bash
git push --force-with-lease
```

Use `--force-with-lease`, not plain `--force`.

If multiple developers are sharing the same branch, do not rewrite its history without coordinating with them.

### 2. Build and test your changes

Before submitting a Pull Request:

* restore dependencies successfully;
* build the affected project(s);
* run relevant tests if available;
* manually verify the affected functionality where appropriate;
* test platform-specific changes on the corresponding platform whenever possible.

A Pull Request does not need to build every supported platform locally, but changes should not knowingly break unrelated platforms.

CI checks should pass before the Pull Request is merged.

### 3. Keep the Pull Request focused

A Pull Request should normally represent one logical change.

Avoid combining:

* unrelated bug fixes;
* broad formatting changes;
* dependency upgrades unrelated to the feature;
* large refactors that are not required by the change.

Large changes are easier to review when divided into logically independent Pull Requests.

### 4. Write a useful description

The Pull Request description should explain:

* what changed;
* why the change is needed;
* how it was implemented when the implementation is not obvious;
* how it was tested;
* which platforms are affected.

Include screenshots or screen recordings for visible UI changes when useful.

Reference related Issues, Pull Requests, Discussions, or commits.

Examples:

```text
Fixes #123
Refs #456
```

If the Pull Request makes another Pull Request unnecessary, mention that explicitly as well.

### 5. Target `dev`

Normal Pull Requests should use:

```text
base: dev
```

Do not target `master` unless the change is specifically intended for the legacy branch.

---

## Merge, rebase, and squash policy

These operations serve different purposes and should not be used interchangeably.

### Rebase

Rebase is mainly used to update a topic branch onto the latest `dev` while keeping its history linear.

Typical use:

```bash
git fetch upstream
git rebase upstream/dev
```

### Squash

Squashing combines several commits into a smaller number of logical commits.

Ordinary Pull Requests are generally good candidates for **Squash and merge**, especially when the branch contains temporary commits such as:

```text
fix typo
address review
try another approach
fix build
```

The final commit should describe the complete logical change rather than the intermediate development process.

### Merge commits

Not recommended.

### Default policy

For normal contributions:

1. create a topic branch from `dev`;
2. commit normally while developing;
3. rebase the topic branch onto the latest `dev` when appropriate;
4. open a Pull Request targeting `dev`;
5. use **Squash and merge** for the final integration unless there is a specific reason to preserve the individual commits.

---

## AI-assisted contributions

Using coding assistants or other AI tools is recommended.

**However, the contributor must be responsible for the submitted code.**

Before submitting AI-assisted changes:

* review every relevant change;
* understand the behavior being modified;
* remove unrelated or speculative changes;
* build and test the result;
* verify that generated code follows existing architecture and style;
* do not include secrets, credentials, private data, or copyrighted material that cannot legally be contributed.

Large AI-generated rewrites without a clear reason or without verification may be rejected even if they compile.

AI tools can assist development, not replace review and engineering judgment.

---

## Upstream synchronization

OpenUtau Mobile periodically synchronizes code from the upstream OpenUtau repository.

Upstream synchronization is a repository maintenance operation and is different from normal feature development.

Do **not** manually merge arbitrary OpenUtau upstream commits into `dev`.

See [Upstream Synchronization Guide](docs/UPSTREAM_SYNC.md) for the complete procedure.

---

## 

## Reporting bugs

When reporting a reproducible bug, include as much relevant information as possible:

* OpenUtau Mobile version or commit;
* device model;
* operating system and version;
* affected platform;
* reproduction steps;
* expected behavior;
* actual behavior;
* screenshots or screen recordings where useful;
* logs or stack traces where available.

For Android crashes, `adb logcat` is often useful:

```bash
adb logcat > log.txt
```

Start logging, reproduce the issue, stop logging, and remove personal or unrelated information before sharing the log.

---

## Licensing

By contributing to OpenUtau Mobile, you agree that your contributions will be distributed under the repository's applicable license.

Do not submit code, assets, libraries, models, or other materials unless they can legally be included and redistributed by the project.

Third-party code and assets must retain any required notices and licensing information.
