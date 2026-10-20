namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IContactMessageService
{
    Task SendAsync(ContactMessageCreateDto dto);
}
