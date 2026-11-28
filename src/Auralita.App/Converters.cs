using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Auralita.App;

internal sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Success = new(Colors.LimeGreen, Colors.Gray);
    public static readonly BoolToBrushConverter Accent = new(Colors.MediumPurple, Colors.Gray);
    public static readonly BoolToBrushConverter Error = new(Colors.Red, Colors.Gray);

    private readonly Color _trueColor;
    private readonly Color _falseColor;

    public BoolToBrushConverter()
    {
        _trueColor = Colors.White;
        _falseColor = Colors.Gray;
    }

    public BoolToBrushConverter(Color trueColor, Color falseColor)
    {
        _trueColor = trueColor;
        _falseColor = falseColor;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var param = parameter as string;
            if (!string.IsNullOrEmpty(param))
            {
                var parts = param.Split('|');
                if (parts.Length == 2)
                {
                    var trueBrush = ParseBrush(parts[0]);
                    var falseBrush = ParseBrush(parts[1]);
                    return b ? trueBrush : falseBrush;
                }
                if (param == "PlayPause")
                {
                    return b ? Brushes.MediumPurple : Brushes.OrangeRed;
                }
                if (param == "Shuffle")
                {
                    return b ? new SolidColorBrush(Color.Parse("#B49ADA")) : Brushes.Gray;
                }
            }
            return new SolidColorBrush(b ? _trueColor : _falseColor);
        }

        return new SolidColorBrush(_falseColor);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IBrush ParseBrush(string brushName)
    {
        return brushName switch
        {
            "AccentBrush" => new SolidColorBrush(Color.Parse("#B49ADA")),
            "TextSecondaryBrush" => new SolidColorBrush(Color.Parse("#B8AFBF")),
            "AccentForegroundBrush" => new SolidColorBrush(Color.Parse("#211A2B")),
            "CoralBrush" => new SolidColorBrush(Color.Parse("#E9A08B")),
            _ => Brushes.Gray
        };
    }
}

internal sealed class BoolToStringConverter : IValueConverter
{
    public BoolToStringConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && parameter is string param)
        {
            var parts = param.Split('|');
            if (parts.Length == 2)
            {
                return b ? parts[0] : parts[1];
            }
            if (param == "PlayPause")
            {
                return b ? "Pause" : "Play";
            }
            if (param == "MutePath")
            {
                return b ? "Unmute" : "Mute";
            }
        }
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class BoolToInverseConverter : IValueConverter
{
    public BoolToInverseConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return !b;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class IntToBoolConverter : IValueConverter
{
    public IntToBoolConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i)
        {
            return i > 0;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class StringToBoolConverter : IValueConverter
{
    public StringToBoolConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class IntToTrackCountConverter : IValueConverter
{
    public IntToTrackCountConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count == 1 ? "1 track" : $"{count} tracks";
        }
        return "0 tracks";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class TrackDetailConverter : IValueConverter
{
    public TrackDetailConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text) || text == "Unknown album" || text == "Unknown artist")
        {
            return string.Empty;
        }
        return text;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class DoubleToWidthConverter : IValueConverter
{
    public DoubleToWidthConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double fraction && parameter is string param && double.TryParse(param, out var maxWidth))
        {
            return maxWidth * Math.Clamp(fraction, 0, 1);
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class RepeatModeToBrushConverter : IValueConverter
{
    public RepeatModeToBrushConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RepeatMode mode)
        {
            return mode == RepeatMode.Off ? Brushes.Gray : new SolidColorBrush(Color.Parse("#B49ADA"));
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class RepeatModeToPathConverter : IValueConverter
{
    public RepeatModeToPathConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RepeatMode mode)
        {
            return mode switch
            {
                RepeatMode.Off => "M 4,4 L 12,4 L 12,12 L 4,12 Z M 6,6 L 10,6 L 10,10 L 6,10 Z", // Repeat off
                RepeatMode.All => "M 4,4 L 12,4 L 12,12 L 4,12 Z M 6,6 L 10,6 L 10,10 L 6,10 Z", // Repeat all
                RepeatMode.One => "M 4,4 L 12,4 L 12,12 L 4,12 Z M 6,6 L 10,6 L 10,10 L 6,10 Z M 8,4 L 8,12", // Repeat one
                _ => "M 4,4 L 12,4 L 12,12 L 4,12 Z M 6,6 L 10,6 L 10,10 L 6,10 Z"
            };
        }
        return "M 4,4 L 12,4 L 12,12 L 4,12 Z M 6,6 L 10,6 L 10,10 L 6,10 Z";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class RepeatModeToTooltipConverter : IValueConverter
{
    public RepeatModeToTooltipConverter() { }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RepeatMode mode)
        {
            return mode switch
            {
                RepeatMode.Off => "Repeat Off",
                RepeatMode.All => "Repeat All",
                RepeatMode.One => "Repeat One",
                _ => "Repeat"
            };
        }
        return "Repeat";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}