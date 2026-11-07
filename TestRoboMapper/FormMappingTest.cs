using System;
using System.Collections.Generic;
using System.Text;
using MediatorMapper.Mapping;
using TestRoboMapper.FormModel;

namespace TestRoboMapper
{
    [TestClass]
    public class FormMappingTest
    {
        [TestMethod]
        public void TestMap()
        {
            var name = "Bart";
            var city = "Brussels";
            var age = 30;

            var mapper = new Mapper();
            var form = new GenericForm
            {
                Name = name,
                Age = age,
                Address = new FormAddress { City = city },
                OtherAddresses = new List<FormAddress>
                {
                    new FormAddress { City = "Antwerp" },
                    new FormAddress { City = "Ghent" }
                }
            };
            var dto = mapper.Map<MyFormDto>(form);

            Assert.IsNotNull(dto);
            Assert.AreEqual(name, dto.Name);
            Assert.AreEqual(age, dto.Age);
            Assert.IsNotNull(dto.Address);
            Assert.AreEqual(city, dto.Address.City);
            Assert.IsNotNull(dto.OtherAddresses);
            Assert.HasCount(2, dto.OtherAddresses);
            Assert.AreEqual("Antwerp", dto.OtherAddresses[0].City);
            Assert.AreEqual("Ghent", dto.OtherAddresses[1].City);
        }

        [TestMethod]
        public void TestMapWithNullCollection()
        {
            var mapper = new Mapper();
            var form = new GenericForm
            {
                Name = "Test",
                Age = 25,
                Address = new FormAddress { City = "Brussels" },
                OtherAddresses = null
            };
            var dto = mapper.Map<MyFormDto>(form);

            Assert.IsNotNull(dto);
            Assert.AreEqual("Test", dto.Name);
            Assert.IsNull(dto.OtherAddresses);
        }

        [TestMethod]
        public void TestMapWithEmptyCollection()
        {
            var mapper = new Mapper();
            var form = new GenericForm
            {
                Name = "Test",
                Age = 25,
                Address = new FormAddress { City = "Brussels" },
                OtherAddresses = new List<FormAddress>()
            };
            var dto = mapper.Map<MyFormDto>(form);

            Assert.IsNotNull(dto);
            Assert.IsNotNull(dto.OtherAddresses);
            Assert.IsEmpty(dto.OtherAddresses);
        }
    }
}
