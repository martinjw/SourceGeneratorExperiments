using RoboMapper;
using TestRoboMapper.Model;

namespace TestRoboMapper
{
    [TestClass]
    public class PersonMappingTests
    {
        // IMPORTANT: referencing the profile ensures the generator includes it.
        private static readonly PersonProfile _profile = new PersonProfile();

        private readonly IMapper _mapper = new Mapper();

        [TestMethod]
        public void Person_Maps_To_PersonDto_Correctly()
        {
            // Arrange


            var person = new Person
            {
                FirstName = "Alice",
                LastName = "Janssens",
                DateOfBirth = new DateTime(2000, 5, 12),
                Address = new Address
                {
                    Street = "Rue Haute 42",
                    City = "Brussels",
                    PostalCode = "1000"
                }
            };

            // Act
            var dto = _mapper.Map<PersonDto>(person);

            // Assert
            Assert.IsNotNull(dto);
            Assert.AreEqual("Alice", dto.FirstName);
            Assert.AreEqual("Janssens", dto.LastName);
            Assert.AreEqual(new DateTime(2000, 5, 12), dto.DateOfBirth);

            Assert.IsNotNull(dto.Address);
            Assert.AreEqual("Rue Haute 42", dto.Address.Street);
            Assert.AreEqual("Brussels", dto.Address.City);
            Assert.AreEqual("1000", dto.Address.PostalCode);
        }

        [TestMethod]
        public void PersonDto_Maps_Back_To_Person_With_ReverseMap()
        {
            // Arrange
            var dto = new PersonDto
            {
                FirstName = "Bart",
                LastName = "Dupont",
                DateOfBirth = new DateTime(1985, 1, 20),
                Address = new AddressDto
                {
                    Street = "Hoofdstraat 5",
                    City = "Antwerp",
                    PostalCode = "2000"
                }
            };

            // Act
            var person = _mapper.Map<Person>(dto);

            // Assert
            Assert.IsNotNull(person);
            Assert.AreEqual("Bart", person.FirstName);
            Assert.AreEqual("Dupont", person.LastName);
            Assert.AreEqual(new DateTime(1985, 1, 20), person.DateOfBirth);

            Assert.IsNotNull(person.Address);
            Assert.AreEqual("Hoofdstraat 5", person.Address.Street);
            Assert.AreEqual("Antwerp", person.Address.City);
            Assert.AreEqual("2000", person.Address.PostalCode);
        }
    }

}
