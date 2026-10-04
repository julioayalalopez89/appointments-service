using Appointments.Api.Models;

namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Avisa al salón de los cambios en las citas. Las implementaciones deben volver
/// enseguida (el envío real ocurre en segundo plano) para no retrasar la respuesta de la API.
/// </summary>
public interface INotificationService
{
    /// <summary>Se ha reservado una cita nueva.</summary>
    void AppointmentBooked(Appointment appointment);

    /// <summary>Se ha cancelado una cita. <paramref name="reason"/> es opcional.</summary>
    void AppointmentCancelled(Appointment appointment, string? reason);
}
