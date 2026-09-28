namespace GameShift.Models;

/// <summary>
/// Кнопки геймпада в маппинге XInput. Используется в комбинации
/// активации; короткое имя для UI получаем через
/// <see cref="GamepadButtonExtensions.DisplayName"/>.
/// </summary>
public enum GamepadButton
{
    A, B, X, Y,
    LB, RB, LT, RT,
    Back, Start,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftStick, RightStick,
    Guide,
}

public static class GamepadButtonExtensions
{
    /// <summary>Короткое имя для отображения на пилюле комбинации.</summary>
    public static string DisplayName(this GamepadButton button) => button switch
    {
        GamepadButton.A          => "A",
        GamepadButton.B          => "B",
        GamepadButton.X          => "X",
        GamepadButton.Y          => "Y",
        GamepadButton.LB         => "LB",
        GamepadButton.RB         => "RB",
        GamepadButton.LT         => "LT",
        GamepadButton.RT         => "RT",
        GamepadButton.Back       => "BACK",
        GamepadButton.Start      => "START",
        GamepadButton.DPadUp     => "↑",
        GamepadButton.DPadDown   => "↓",
        GamepadButton.DPadLeft   => "←",
        GamepadButton.DPadRight  => "→",
        GamepadButton.LeftStick  => "L3",
        GamepadButton.RightStick => "R3",
        GamepadButton.Guide      => "GUIDE",
        _                        => "?",
    };
}
