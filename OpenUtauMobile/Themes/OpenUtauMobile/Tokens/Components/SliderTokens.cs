using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens;

/// <summary>共享滑块规格。</summary>
public static class SliderTokens
{
    public static double TrackHeight => 16d;
    public static double HandleWidth => 4d;
    public static double HandleHeight => 44d;
    public static double ActiveHandleWidth => 2d;
    public static double HandleGap => 6d;
    public static double InsideCorner => 2d;
    public static double StopSize => 4d;
    public static double StopInset => 6d;
    public static double MinHeight => InteractionTokens.TouchTarget;
}
