using MediatorLib.Mapping;

namespace TestRoboMapper.Model;

public class PersonProfile : MappingProfile
{
    public PersonProfile()
    {
        CreateMap<Person, PersonDto>()
            .ReverseMap();

        CreateMap<Address, AddressDto>()
            .ReverseMap();
    }
}