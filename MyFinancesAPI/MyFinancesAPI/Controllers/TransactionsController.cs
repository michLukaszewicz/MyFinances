using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models;
using System.Text.Json;

namespace MyFinancesAPI.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class TransactionsController : ControllerBase
    {
        private const string TEST_DATA_PATH = "TestData/UserData.json";

        [ProducesResponseType(typeof(ContentResult), 200)]
        [HttpGet]
        public IActionResult Get()
        {
            if (!System.IO.File.Exists(TEST_DATA_PATH))
            {
                return NotFound("Could not find the test data file");
            }
            string testData = System.IO.File.ReadAllText(TEST_DATA_PATH);
            if (testData != null && !string.IsNullOrEmpty(testData))
            {
                Transaction[]? deserialized = JsonSerializer.Deserialize<Transaction[]>(testData);
                return Ok(deserialized);
            }
            return NotFound();
        }
    }
}