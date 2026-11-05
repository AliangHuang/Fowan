using Fowan.Diary.Shared.Models;
using Fowan.Diary.Shared.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Geolocation;

namespace Fowan.Diary.Windows.Coordination;

internal sealed class DiaryEnvironmentAcquisitionCoordinator(
    IDiaryReverseGeocoder reverseGeocoder,
    IDiaryWeatherProvider weatherProvider,
    Func<DiarySettings> settings,
    Func<XamlRoot> xamlRoot,
    Action<DiarySettings> saveSettings,
    Action<string, DiaryLocationDetails?> setLocation,
    Action<string, DiaryWeatherDetails?> setWeather,
    Func<string, string, Task> showMessage)
{
    private (double Latitude, double Longitude, DateTimeOffset AcquiredAt)? _cachedLocation;

    public async Task AcquireLocationAsync()
    {
        if (!await EnsureLocationEnabledAsync()) return;
        var coordinates = await GetDeviceLocationAsync();
        if (coordinates is null) return;
        try
        {
            var current = settings();
            var resolved = await reverseGeocoder.ReverseAsync(
                coordinates.Value.Latitude, coordinates.Value.Longitude, current.ReverseGeocoderEndpoint);
            var label = resolved is null ? CoordinateLabel(coordinates.Value) : ShortLocationLabel(resolved.DisplayName);
            setLocation(label, new DiaryLocationDetails
            {
                Source = "nominatim", Latitude = coordinates.Value.Latitude,
                Longitude = coordinates.Value.Longitude, ResolvedAt = DateTimeOffset.Now
            });
        }
        catch
        {
            setLocation(CoordinateLabel(coordinates.Value), new DiaryLocationDetails
            {
                Source = "device", Latitude = coordinates.Value.Latitude,
                Longitude = coordinates.Value.Longitude, ResolvedAt = DateTimeOffset.Now
            });
            await showMessage("地点名称获取失败", "已保存当前位置坐标。请检查网络后重试，或手动输入地点。");
        }
    }

    public async Task AcquireWeatherAsync()
    {
        if (!await EnsureWeatherEnabledAsync()) return;
        var coordinates = await GetDeviceLocationAsync();
        if (coordinates is null) return;
        try
        {
            var weather = await weatherProvider.GetCurrentAsync(
                coordinates.Value.Latitude, coordinates.Value.Longitude, settings().WeatherEndpoint);
            setWeather($"{weather.Condition} {Math.Round(weather.TemperatureCelsius):0}℃", new DiaryWeatherDetails
            {
                Source = "open-meteo", TemperatureCelsius = weather.TemperatureCelsius,
                WeatherCode = weather.WeatherCode, Latitude = weather.Latitude, Longitude = weather.Longitude,
                FetchedAt = weather.FetchedAt
            });
        }
        catch
        {
            await showMessage("天气获取失败", "现有天气信息未修改。请检查网络或稍后重试。");
        }
    }

    public Task<bool> ConfirmLocationConsentAsync() => ConfirmAsync(
        "启用自动定位？",
        "仅在你点击“获取当前位置”时，Fowan 才会请求 Windows 定位权限。取得的经纬度会发送给 Nominatim 用于解析可读地点名称；不会后台定位，也可以随时在设置中关闭。");

    public Task<bool> ConfirmWeatherConsentAsync() => ConfirmAsync(
        "启用自动天气？",
        "仅在你点击“自动获取当前位置天气”时，Fowan 才会把本次位置坐标发送给 Open-Meteo 查询当前天气。不会后台更新，也可以随时在设置中关闭。");

    private async Task<bool> EnsureLocationEnabledAsync()
    {
        var current = settings();
        if (current.LocationFeatureEnabled) return true;
        if (current.LocationConsentAcceptedAt is null && !await ConfirmLocationConsentAsync()) return false;
        current.LocationFeatureEnabled = true;
        current.LocationConsentAcceptedAt ??= DateTimeOffset.Now;
        saveSettings(current);
        return true;
    }

    private async Task<bool> EnsureWeatherEnabledAsync()
    {
        var current = settings();
        if (!current.LocationFeatureEnabled)
        {
            await showMessage("请先启用自动定位", "自动天气需要先取得一次由你主动触发的位置坐标。请在设置中启用自动定位。");
            return false;
        }
        if (current.WeatherFeatureEnabled) return true;
        if (current.WeatherConsentAcceptedAt is null && !await ConfirmWeatherConsentAsync()) return false;
        current.WeatherFeatureEnabled = true;
        current.WeatherConsentAcceptedAt ??= DateTimeOffset.Now;
        saveSettings(current);
        return true;
    }

    private async Task<bool> ConfirmAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot(), Title = title, Content = content,
            PrimaryButtonText = "同意并继续", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<(double Latitude, double Longitude)?> GetDeviceLocationAsync()
    {
        if (_cachedLocation is { } cached && DateTimeOffset.Now - cached.AcquiredAt < TimeSpan.FromMinutes(5))
            return (cached.Latitude, cached.Longitude);
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed)
            {
                await showMessage("定位权限未授予", "请在 Windows 设置中允许 Fowan 使用位置，或继续手动输入地点。");
                return null;
            }
            var position = await new Geolocator { DesiredAccuracyInMeters = 150 }.GetGeopositionAsync();
            var point = position.Coordinate.Point.Position;
            _cachedLocation = (point.Latitude, point.Longitude, DateTimeOffset.Now);
            return (point.Latitude, point.Longitude);
        }
        catch
        {
            await showMessage("无法获取当前位置", "请检查 Windows 定位服务和网络连接，或继续手动输入地点。");
            return null;
        }
    }

    private static string CoordinateLabel((double Latitude, double Longitude) point) => $"{point.Latitude:F4}, {point.Longitude:F4}";
    private static string ShortLocationLabel(string displayName)
    {
        var label = string.Join(" · ", displayName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(3));
        return string.IsNullOrWhiteSpace(label) ? displayName : label;
    }
}
