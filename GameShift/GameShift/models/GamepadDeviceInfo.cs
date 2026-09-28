namespace GameShift.Models;

/// <param name="Name">
/// Имя из <c>Windows.Gaming.Input.RawGameController.DisplayName</c> —
/// то же, что Windows показывает в Settings → Bluetooth & devices.
/// </param>
/// <param name="DeviceId">
/// Стабильный идентификатор устройства от WGI (NonRoamableId).
/// </param>
/// <param name="IsSupported">
/// true — устройство представляется системе как стандартный Gamepad
/// (Xbox-style API: A/B/X/Y, LB/RB, и т.д.). Опрашивается, кнопки пишутся
/// в комбинацию.
/// false — Joy-Con в одиночном режиме, экзотические HID-джойстики и т.п.:
/// показываем в списке для информации, но кнопки не считываются.
/// </param>
public sealed record GamepadDeviceInfo(
    string Name,
    string DeviceId,
    bool IsSupported);