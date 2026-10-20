namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IContactMessageRepository
{
    Task<ContactMessage> CreateAsync(ContactMessage message);
}
