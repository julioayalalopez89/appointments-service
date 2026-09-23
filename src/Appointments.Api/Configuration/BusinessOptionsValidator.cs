using Microsoft.Extensions.Options;

namespace Appointments.Api.Configuration;

/// <summary>
/// Valida la configuración "Business" al arrancar: si algo está mal, la app
/// no arranca y el error dice exactamente qué corregir.
/// </summary>
public class BusinessOptionsValidator : IValidateOptions<BusinessOptions>
{
    public ValidateOptionsResult Validate(string? name, BusinessOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TimeZone))
        {
            errors.Add("Business:TimeZone es obligatorio (ej. \"America/New_York\").");
        }
        else
        {
            try
            {
                options.GetTimeZoneInfo();
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                errors.Add($"Business:TimeZone \"{options.TimeZone}\" no es una zona horaria válida.");
            }
        }

        if (options.SlotIntervalMinutes is < 5 or > 240)
        {
            errors.Add("Business:SlotIntervalMinutes debe estar entre 5 y 240.");
        }

        if (options.MaxConcurrentAppointments is < 1 or > 100)
        {
            errors.Add("Business:MaxConcurrentAppointments debe estar entre 1 y 100.");
        }

        var duplicated = options.OpeningHours
            .GroupBy(d => d.Day)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var day in duplicated)
        {
            errors.Add($"Business:OpeningHours tiene {day} repetido.");
        }

        foreach (var day in options.OpeningHours)
        {
            if (day.Close <= day.Open)
            {
                errors.Add($"Business:OpeningHours {day.Day}: la hora de cierre ({day.Close:HH\\:mm}) debe ser posterior a la de apertura ({day.Open:HH\\:mm}).");
            }
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
