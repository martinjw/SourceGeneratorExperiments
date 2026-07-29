namespace TestRoboMapper.FormModel
{
    public class MyFormDto
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public FormAddressDto Address { get; set; }
        public IList<FormAddressDto> OtherAddresses { get; set; }
    }
}
