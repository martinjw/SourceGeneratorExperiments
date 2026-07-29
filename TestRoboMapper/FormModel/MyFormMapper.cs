using MediatorLib.Mapping;

namespace TestRoboMapper.FormModel
{
    public class MyFormMapper : MappingProfile
    {
        public MyFormMapper()
        {
            CreateMap<FormAddress, FormAddressDto>().ReverseMap();
            CreateMap<MyForm, MyFormDto>().ReverseMap();
            CreateMap<GenericForm, MyFormDto>().ReverseMap();
        }
    }
}
