namespace TestRoboMapper.Model;

public class PersonDto
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime DateOfBirth { get; set; }
    public AddressDto Address { get; set; } = new();
}