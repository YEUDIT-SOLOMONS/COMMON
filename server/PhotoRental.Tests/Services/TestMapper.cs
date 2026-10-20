using AutoMapper;
using PhotoRental.Service.Mapping;

namespace PhotoRental.Tests.Services;

public static class TestMapper
{
    public static readonly IMapper Instance =
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();
}
