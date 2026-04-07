# Popup / Dialog Advanced Features

[English](PopupFeatures.md) | [简体中文](PopupFeatures_zh.md)

> This document is based on the source code of `OpenUtauMobile` and the [`CommunityToolkit.Maui`](https://github.com/CommunityToolkit/Maui) library (v11.2.0) that the project uses. All code examples are drawn from the actual popup files under `OpenUtauMobile/Views/Controls/`.

---

## 1. Framework Overview

All popups in this project are built on **`CommunityToolkit.Maui.Views.Popup`** (namespace `CommunityToolkit.Maui.Views`), not an Avalonia `Window`. The toolkit is registered in `MauiProgram.cs`:

```csharp
builder.UseMauiCommunityToolkit();
```

A popup is shown from a `Page` (or `ContentPage`) using the `ShowPopupAsync` / `ShowPopup` extension methods:

```csharp
// Awaitable – blocks until the popup is closed and returns a result
object? result = await this.ShowPopupAsync(new RenamePopup(oldName, title));

// Fire-and-forget – show without waiting for a result
this.ShowPopup(new LoadingPopup(showProgressBar: true));
```

---

## 2. Key Popup Properties

### 2.1 `Color` — Overlay / Backdrop Color

`Color` controls the **scrim** (the semi-transparent layer drawn behind the popup content but in front of the page content).

| Value | Effect |
|---|---|
| `Color="Transparent"` | No visible backdrop (the page is fully visible behind the popup) |
| `Color="#80000000"` | 50 % black overlay (standard modal dimming) |
| `Color="Black"` | Fully opaque black overlay |

Every popup in this project sets `Color="Transparent"`, making the page visible behind the popup:

```xml
<toolkit:Popup ...
    Color="Transparent"
    x:Class="OpenUtauMobile.Views.Controls.RenamePopup">
```

### 2.2 `HorizontalOptions` / `VerticalOptions` — Positioning

These layout options determine where the popup is anchored on screen.

| Value | Meaning |
|---|---|
| `Center` (default) | Centered horizontally / vertically |
| `Start` | Aligned to the left / top edge |
| `End` | Aligned to the right / bottom edge |
| `Fill` | Stretched to fill the available width / height |

`ErrorPopup.xaml` demonstrates explicit centering:

```xml
<toolkit:Popup ...
    HorizontalOptions="Center"
    VerticalOptions="Center">
```

Most other popups omit these properties and therefore default to center-center placement.

### 2.3 `Size` — Explicit Dimensions

You can set a fixed pixel size via the `Size` property in code-behind:

```csharp
// In the Popup constructor
Size = new Size(400, 300);
```

Alternatively, size the **content** with `WidthRequest` / `HeightRequest` on the inner `Border` and leave `Size` unset, which is the pattern used throughout this project:

```xml
<Border WidthRequest="400" ...>
```

### 2.4 `CanBeDismissedByTappingOutsideOfPopup` — Dismissal Control

| Value | Behavior |
|---|---|
| `True` (default) | Tapping outside the popup content closes it |
| `False` | The popup can only be closed programmatically |

`LoadingPopup` and `ErrorPopup` set this to `False` because they represent blocking operations:

```xml
<toolkit:Popup ...
    CanBeDismissedByTappingOutsideOfPopup="False">
```

### 2.5 `Anchor` — Contextual / Relative Positioning

`CommunityToolkit.Maui.Views.Popup` exposes an `Anchor` property that accepts a `View`. When set, the popup is positioned relative to that view rather than centered on screen — useful for context menus or tooltips.

```csharp
// Anchor the popup below a button
var popup = new MyContextMenuPopup();
popup.Anchor = myButton;
await this.ShowPopupAsync(popup);
```

No popup in this project currently uses `Anchor`, but it is fully supported by the toolkit.

### 2.6 `CloseAsync` / Return Value — Communication Back to Caller

Popups communicate results back to the calling page through `CloseAsync`:

```csharp
// Inside the Popup code-behind
private void OnConfirmClicked(object sender, EventArgs e)
{
    CloseAsync(EntryName.Text);   // pass a result value
}

private void OnCancelClicked(object sender, EventArgs e)
{
    CloseAsync(null);             // pass null for cancellation
}
```

---

## 3. Semi-Transparent Popups

### 3.1 Transparent Overlay + Opaque Content

The most common pattern: clear the `Color` overlay and give the content an opaque background.

```xml
<toolkit:Popup Color="Transparent">
    <Border Background="White" StrokeShape="RoundRectangle 10">
        <!-- content -->
    </Border>
</toolkit:Popup>
```

All 16 project popups follow this pattern.

### 3.2 Semi-Transparent Content

Use an alpha component in the `Background` color (ARGB hex: `#AARRGGBB`):

```xml
<toolkit:Popup Color="Transparent">
    <Border Background="#CC444444">   <!-- 80 % opaque dark grey -->
        <!-- content -->
    </Border>
</toolkit:Popup>
```

Or set `Opacity` on any element:

```xml
<Border Background="White" Opacity="0.85">
```

### 3.3 Semi-Transparent Overlay + Opaque Content

```xml
<toolkit:Popup Color="#80000000">  <!-- 50 % black scrim -->
    <Border Background="White" StrokeShape="RoundRectangle 10">
        <!-- content -->
    </Border>
</toolkit:Popup>
```

### 3.4 Blurred Background

.NET MAUI does not provide a built-in blur effect. Platform-specific effects (e.g., `UIBlurEffect` on iOS, `RenderEffect` with `Blur` on Android 12+) would need to be implemented as a `PlatformEffect` or `MauiContext` customization and applied to the page content beneath the popup.

---

## 4. Non-Rectangular / Irregular Shapes

The `Popup` control itself is always rectangular. Irregular shapes are achieved by styling the **content** inside the popup:

### 4.1 Rounded Corners — `StrokeShape="RoundRectangle N"`

All project popups use a `Border` with a `RoundRectangle` stroke shape:

```xml
<Border StrokeThickness="0"
        Background="{DynamicResource PopupBackground}"
        WidthRequest="400"
        StrokeShape="RoundRectangle 10">
    <!-- content -->
</Border>
```

The `Color="Transparent"` on the `Popup` ensures that the rounded corners of the `Border` are truly visible (the backdrop does not cover them with a solid color).

### 4.2 Ellipse / Circle Shape

```xml
<toolkit:Popup Color="Transparent">
    <Border StrokeShape="Ellipse"
            WidthRequest="200" HeightRequest="200"
            Background="LightBlue">
        <!-- circular content -->
    </Border>
</toolkit:Popup>
```

Several singer avatar elements in `ChooseSingerPopup.xaml` already demonstrate `StrokeShape="Ellipse"` for inner elements.

### 4.3 Custom Geometry via `Clip`

For fully arbitrary shapes, apply a geometry clip to the content:

```xml
<toolkit:Popup Color="Transparent">
    <ContentView WidthRequest="300" HeightRequest="200">
        <ContentView.Clip>
            <PathGeometry Figures="M 0,50 L 50,0 L 250,0 L 300,50 L 250,100 L 50,100 Z" />
        </ContentView.Clip>
        <Border Background="White" />
    </ContentView>
</toolkit:Popup>
```

Note: Clipping makes corners transparent but touch events may still be captured by the rectangular hit-test region on some platforms.

### 4.4 SkiaSharp for Complex Shapes

The project already depends on `SkiaSharp.Views.Maui.Controls`. An `SKCanvasView` inside a popup can render any shape, including star polygons or speech-bubble callouts, while keeping the surrounding area transparent.

---

## 5. Draggable Popups

`CommunityToolkit.Maui.Views.Popup` does **not** provide a built-in drag API. Dragging must be implemented manually using MAUI gesture recognizers.

### Implementation Pattern

```xml
<toolkit:Popup x:Name="ThisPopup" Color="Transparent">
    <Border x:Name="PopupContent"
            Background="White"
            StrokeShape="RoundRectangle 10"
            WidthRequest="300">
        <!-- drag handle -->
        <Grid RowDefinitions="40,*">
            <BoxView Grid.Row="0" BackgroundColor="#66CCFF">
                <BoxView.GestureRecognizers>
                    <PanGestureRecognizer PanUpdated="OnDragHandlePanUpdated" />
                </BoxView.GestureRecognizers>
            </BoxView>
            <!-- rest of content -->
        </Grid>
    </Border>
</toolkit:Popup>
```

```csharp
private double _dragOffsetX, _dragOffsetY;

private void OnDragHandlePanUpdated(object sender, PanUpdatedEventArgs e)
{
    switch (e.StatusType)
    {
        case GestureStatus.Running:
            // Shift the content view
            PopupContent.TranslationX = _dragOffsetX + e.TotalX;
            PopupContent.TranslationY = _dragOffsetY + e.TotalY;
            break;
        case GestureStatus.Completed:
            _dragOffsetX = PopupContent.TranslationX;
            _dragOffsetY = PopupContent.TranslationY;
            break;
    }
}
```

**Limitation:** Because `TranslationX/Y` is applied to the content view rather than the popup itself, the popup host region remains at its original position. Touch events outside the content's visual bounds but inside the original popup rectangle may still be captured.

---

## 6. Resizable Popups

`CommunityToolkit.Maui.Views.Popup` does **not** provide built-in resize handles. Resizing must be implemented with custom gesture recognizers attached to corner or edge elements.

### Implementation Pattern

```xml
<toolkit:Popup Color="Transparent">
    <!-- Outer container sized manually -->
    <Border x:Name="ResizableContent"
            Background="White"
            StrokeShape="RoundRectangle 10"
            WidthRequest="300" HeightRequest="200">
        <Grid>
            <!-- main content -->

            <!-- resize handle (bottom-right corner) -->
            <BoxView HorizontalOptions="End"
                     VerticalOptions="End"
                     WidthRequest="20" HeightRequest="20"
                     BackgroundColor="#66CCFF">
                <BoxView.GestureRecognizers>
                    <PanGestureRecognizer PanUpdated="OnResizeHandlePanUpdated" />
                </BoxView.GestureRecognizers>
            </BoxView>
        </Grid>
    </Border>
</toolkit:Popup>
```

```csharp
private double _baseWidth = 300, _baseHeight = 200;

private void OnResizeHandlePanUpdated(object sender, PanUpdatedEventArgs e)
{
    if (e.StatusType == GestureStatus.Running)
    {
        ResizableContent.WidthRequest  = Math.Max(200, _baseWidth  + e.TotalX);
        ResizableContent.HeightRequest = Math.Max(100, _baseHeight + e.TotalY);
    }
    else if (e.StatusType == GestureStatus.Completed)
    {
        _baseWidth  = ResizableContent.WidthRequest;
        _baseHeight = ResizableContent.HeightRequest;
    }
}
```

---

## 7. Modal vs Non-Modal

All `CommunityToolkit.Maui.Views.Popup` instances are **modal** by default — they block interaction with the underlying page while open.

Non-modal floating panels are not directly supported by `Popup`. Alternatives within .NET MAUI:
- Use an `AbsoluteLayout` overlay on the current page and manage it manually.
- Use a `Shell` modal with a transparent `ContentPage`.
- Use a platform-native floating window (requires `DependencyService` or a platform-specific handler).

---

## 8. Feature Summary

| Feature | Supported? | How |
|---|---|---|
| Center positioning | ✅ Built-in | Default behavior |
| Custom position (H/V options) | ✅ Built-in | `HorizontalOptions` / `VerticalOptions` |
| Anchor to a view | ✅ Built-in | `Popup.Anchor` property |
| Transparent overlay | ✅ Built-in | `Color="Transparent"` |
| Semi-transparent overlay | ✅ Built-in | `Color="#80000000"` etc. |
| Opaque content | ✅ Built-in | Set `Background` on inner `Border` |
| Semi-transparent content | ✅ Built-in | Alpha color or `Opacity` on content |
| Background blur | ⚠️ Platform code required | iOS `UIBlurEffect`, Android `RenderEffect` |
| Rounded corners | ✅ Built-in (via content) | `StrokeShape="RoundRectangle N"` on `Border` |
| Ellipse / circle shape | ✅ Built-in (via content) | `StrokeShape="Ellipse"` on `Border` |
| Custom path shape | ✅ Supported (via content) | `ContentView.Clip` + `PathGeometry` |
| SkiaSharp drawn shapes | ✅ Supported | `SKCanvasView` inside popup |
| Dismiss on tap outside | ✅ Built-in | `CanBeDismissedByTappingOutsideOfPopup` |
| Block dismiss | ✅ Built-in | `CanBeDismissedByTappingOutsideOfPopup="False"` |
| Return value to caller | ✅ Built-in | `CloseAsync(result)` |
| Draggable popup | ⚠️ Custom required | `PanGestureRecognizer` + `TranslationX/Y` |
| Resizable popup | ⚠️ Custom required | `PanGestureRecognizer` + `WidthRequest/HeightRequest` |
| Non-modal floating panel | ❌ Not in Popup | Use `AbsoluteLayout` overlay or platform code |
| Animation on open/close | ⚠️ Limited | Override `OnAppearing`/`OnDisappearing` with `Animation` |

---

## 9. Demo Popup

`OpenUtauMobile/Views/Controls/DemoAdvancedPopup.xaml` demonstrates all directly-supported advanced features in one popup:

- `Color="Transparent"` — no backdrop
- `HorizontalOptions="End" VerticalOptions="Start"` — positioned at the top-right corner
- `StrokeShape="RoundRectangle 16"` — rounded corners
- `Background="#CC1A1A2E"` — semi-transparent dark content background
- `CanBeDismissedByTappingOutsideOfPopup="True"` — dismisses on outside tap
- `PanGestureRecognizer` on the title bar — drag to reposition
