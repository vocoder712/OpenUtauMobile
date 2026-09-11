# Architecture boundaries

## Core and application ownership

Prefer existing Core APIs for project semantics, commands, validation and synthesis;
keep mobile interaction, progress and error presentation in the application layer.
Duplicating a Core operation in a Mobile adapter creates a second behavioral owner,
while putting UI orchestration in Core increases the cost of upstream synchronization.
Core contains intentional OPUM adaptations, not an interchangeable pristine copy.
The [upstream guide](UPSTREAM_SYNC.md) owns synchronization and compatibility review
for Core and the built-in Plugin; local feature work does not fork Plugin behavior.

## Host capabilities, not platform-aware ViewModels

[ServiceHub](../OpenUtauMobile/Services/ServiceHub.cs) is the capability boundary.
Hosts register native implementations; portable implementations can stay shared
(for example, the Avalonia clipboard service). ViewModels consume capabilities
rather than select native APIs or move platform dependencies into Core.
The shared application owns navigation and UI lifetime composition; host-specific
bootstrap responsibilities and exceptions are described in [Platforms](PLATFORMS.md).

## Command ordering across threads

Background work is not permission to mutate the project concurrently. Commands
and undo-group boundaries must remain ordered on DocManager's main thread.
Simply wrapping a synchronous command-producing operation in `Task.Run` can let
undo boundaries overtake posted commands. The thread-scoped
`RunWithSynchronousMainThreadDispatch` bridge preserves that ordering without
making unrelated background dispatch globally blocking. It accepts synchronous
work, not an async delegate; the UI thread must remain free to process dispatch.
See [DocManager](../OpenUtau.Core/DocManager.cs) and the existing synchronous batch
caller in [PianoRollViewModel](../OpenUtauMobile/ViewModels/PianoRollViewModel.cs).
