using Microsoft.AspNetCore.Mvc;
using MyFinancesAPI.Models;
using System.Text.Json;

namespace MyFinancesAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class TestDataController : ControllerBase
    {
        private const string TEST_DATA_PATH = "TestData/TransactionHistory.json";
        private const string NOT_FOUNT_MESSAGE = "Could not find the test data file.";

        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            if (!System.IO.File.Exists(TEST_DATA_PATH))
            {
                return NotFound(NOT_FOUNT_MESSAGE);
            }

            string json = System.IO.File.ReadAllText(TEST_DATA_PATH);
            List<Transaction>? transactions = JsonSerializer.Deserialize<List<Transaction>>(json, new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            });

            Transaction? transactionToDelete = transactions?.FirstOrDefault(t => t.Id == id);
            if (transactionToDelete == null)
            {
                return NotFound($"Could not find the transaction with given id: {id}");
            }

            transactions!.Remove(transactionToDelete);
            string updatedJson = JsonSerializer.Serialize(transactions, new JsonSerializerOptions()
            {
                WriteIndented = true,
            });

            System.IO.File.WriteAllText(TEST_DATA_PATH, updatedJson);
            return NoContent();
        }

        [ProducesResponseType(typeof(ContentResult), 200)]
        [HttpGet]
        public IActionResult Get()
        {
            if (!System.IO.File.Exists(TEST_DATA_PATH))
            {
                return NotFound(NOT_FOUNT_MESSAGE);
            }

            string testData = System.IO.File.ReadAllText(TEST_DATA_PATH);
            if (testData != null && !string.IsNullOrEmpty(testData))
            {
                Transaction[]? deserialized = JsonSerializer.Deserialize<Transaction[]>(testData);
                return Ok(deserialized);
            }

            return NotFound();
        }

        [ProducesResponseType(typeof(ContentResult), 200)]
        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            if (!System.IO.File.Exists(TEST_DATA_PATH))
            {
                return NotFound(NOT_FOUNT_MESSAGE);
            }
            string json = System.IO.File.ReadAllText(TEST_DATA_PATH);
            var transactions = JsonSerializer.Deserialize<IEnumerable<Transaction>>(json, new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            });
            Transaction? transaction = transactions?.FirstOrDefault(t => t.Id == id);
            if (transaction == null)
            {
                return NotFound($"Could not find the transaction with given id: {id}");
            }
            return Ok(transaction);
        }

        [HttpPut]
        public IActionResult Put([FromBody] Transaction updatedTransaction)
        {
            if (!System.IO.File.Exists(TEST_DATA_PATH))
            {
                return NotFound(NOT_FOUNT_MESSAGE);
            }
            string json = System.IO.File.ReadAllText(TEST_DATA_PATH);
            var transactions = JsonSerializer.Deserialize<IEnumerable<Transaction>>(json, new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            });
            Transaction? existingTransaction = transactions?.FirstOrDefault(t => t.Id == updatedTransaction.Id);
            if (existingTransaction == null)
            {
                return NotFound($"Could not find the transaction with given id: {updatedTransaction.Id}");
            }

            existingTransaction.Date = updatedTransaction.Date;
            existingTransaction.Description = updatedTransaction.Description;
            existingTransaction.Amount = updatedTransaction.Amount;
            existingTransaction.Category = updatedTransaction.Category;
            existingTransaction.BankAccount = updatedTransaction.BankAccount;
            existingTransaction.OtherSideOfTransaction = updatedTransaction.OtherSideOfTransaction;
            var updatedJson = JsonSerializer.Serialize(transactions, new JsonSerializerOptions()
            {
                WriteIndented = true,
            });
            System.IO.File.WriteAllText(TEST_DATA_PATH, updatedJson);
            return NoContent();
        }

        [HttpPost]
        public IActionResult Post([FromBody] Transaction newTransaction)
        {
            if (!System.IO.File.Exists(TEST_DATA_PATH))
            {
                return NotFound(NOT_FOUNT_MESSAGE);
            }

            string json = System.IO.File.ReadAllText(TEST_DATA_PATH);
            var transactions = JsonSerializer.Deserialize<List<Transaction>>(json, new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<Transaction>();

            int nextId = transactions.Any() ? transactions.Max(t => t.Id) + 1 : 1;
            newTransaction.Id = nextId;
            transactions.Add(newTransaction);

            var updatedJson = JsonSerializer.Serialize(transactions, new JsonSerializerOptions()
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(TEST_DATA_PATH, updatedJson);
            return CreatedAtAction(nameof(Post), new { id = newTransaction.Id }, new { id = newTransaction.Id });
        }
    }
}