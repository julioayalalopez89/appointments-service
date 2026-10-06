using Appointments.Api.Models;

namespace Appointments.Api.Services.Notifications;

/// <summary>
/// Avisa al salón de los cambios en las citas (y confirma la reserva a la clienta). Las implementaciones deben volver
/// enseguida (el envío real ocurre en segundo plano) para no retrasar la respuesta de la API.
/// </summary>
public interface INotificationService
{
    /// <summary>Se ha reservado una cita nueva: aviso al salón y, si dejó email, confirmación a la clienta.</summary>
    void AppointmentBooked(Appointment appointment);

    /// <summary>Se ha cancelado una cita. <paramref name="reason"/> es opcional.</summary>
    void AppointmentCancelled(Appointment appointment, string? reason);
}
