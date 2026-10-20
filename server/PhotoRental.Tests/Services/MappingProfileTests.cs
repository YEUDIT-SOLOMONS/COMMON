using AutoMapper;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Service.Mapping;

namespace PhotoRental.Tests.Services;

public class MappingProfileTests
{
    [Fact]
    public void Configuration_IsValid() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).AssertConfigurationIsValid();

    [Fact]
    public void User_MapsRoleAndDefaultQuietHours()
    {
        var dto = TestMapper.Instance.Map<UserDto>(new User { Id = "u1", Name = "נועה", Role = UserRole.Owner, PasswordHash = "secret" });

        Assert.Equal("Owner", dto.Role);
        Assert.False(string.IsNullOrEmpty(dto.QuietFrom));
    }

    [Fact]
    public void ItemBlock_FormatsDates()
    {
        var dto = TestMapper.Instance.Map<ItemBlockDto>(new ItemBlock { Id = "b1", From = new DateTime(2026, 11, 2), To = new DateTime(2026, 11, 4) });

        Assert.Equal("2026-11-02", dto.From);
        Assert.Equal("2026-11-04", dto.To);
    }
}
