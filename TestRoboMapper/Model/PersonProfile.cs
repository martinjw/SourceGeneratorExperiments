using RoboMapper;

namespace TestRoboMapper.Model;

public class PersonProfile : MappingProfile
{
    public PersonProfile()
    {
        CreateMap<Person, PersonDto>()
            .ForMember(nameof(PersonDto.FirstName), p => p.FirstName)
            .ForMember(nameof(PersonDto.LastName), p => p.LastName)
            .ForMember(nameof(PersonDto.DateOfBirth), p => p.DateOfBirth)
            .ForMember(nameof(PersonDto.Address), p => p.Address)
            .ReverseMap();

        CreateMap<Address, AddressDto>()
            .ReverseMap();
    }
}