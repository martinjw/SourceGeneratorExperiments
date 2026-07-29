namespace TestRoboMapper.FormModel
{
    public class MyForm
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public FormAddress Address { get; set; }
        public IList<FormAddress> OtherAddresses { get; set; }
    }
}
