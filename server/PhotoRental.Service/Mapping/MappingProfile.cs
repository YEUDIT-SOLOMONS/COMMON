namespace PhotoRental.Service.Mapping;

using AutoMapper;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;

// מיפוי ישויות ה-Core ל-DTOs שיוצאים מה-API - ישויות לא יוצאות מהשרת כמו שהן
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Category, CategoryDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id ?? string.Empty));

        CreateMap<User, UserDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id ?? string.Empty))
            .ForMember(d => d.Role, o => o.MapFrom(s => s.Role.ToString()))
            .ForMember(d => d.QuietFrom, o => o.MapFrom(s => s.QuietFrom ?? QuietHours.DefaultFrom))
            .ForMember(d => d.QuietTo, o => o.MapFrom(s => s.QuietTo ?? QuietHours.DefaultTo));

        CreateMap<ItemBlock, ItemBlockDto>()
            .ForMember(d => d.Id, o => o.MapFrom(s => s.Id ?? string.Empty))
            .ForMember(d => d.From, o => o.MapFrom(s => s.From.ToString("yyyy-MM-dd")))
            .ForMember(d => d.To, o => o.MapFrom(s => s.To.ToString("yyyy-MM-dd")));
    }
}
