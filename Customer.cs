using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CustomerServerlessApi
{
    public static class Customer
    {
        private const string StorageFile = "customers.json";
        private static List<Customers> _customers = new();

        static Customer()
        {
            _customers = LoadCustomersFromFile();
        }

        private static List<Customers> LoadCustomersFromFile()
        {
            try
            {
                if (File.Exists(StorageFile))
                {
                    var json = File.ReadAllText(StorageFile);
                    return JsonConvert.DeserializeObject<List<Customers>>(json) ?? new List<Customers>();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading customers from file: {ex.Message}");
            }
            return new List<Customers>(); // Return empty list if file doesn't exist or deserialization fails
        }

        [FunctionName("Customer")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Customer function processed a request."); // Log request

            if (req.Method == HttpMethods.Get)
            {
                log.LogInformation("Returning all customers.");
                return new OkObjectResult(_customers);
            }
            else if (req.Method == HttpMethods.Post)
            {
                string requestBody;
                try
                {
                    requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                }
                catch (Exception ex)
                {
                    log.LogError($"Error reading request body: {ex.Message}");
                    return new BadRequestObjectResult("Error reading request body.");
                }

                List<Customers> newCustomers;
                try
                {
                    newCustomers = JsonConvert.DeserializeObject<List<Customers>>(requestBody);
                }
                catch (JsonException ex)
                {
                    log.LogError($"Error deserializing request body: {ex.Message}");
                    return new BadRequestObjectResult("Invalid JSON format in request body.");
                }

                if (newCustomers == null || newCustomers.Count == 0)
                {
                    log.LogWarning("No customers provided in the request body.");
                    return new BadRequestObjectResult("No customers provided in the request body.");
                }

                try
                {
                    var ids = _customers.Select(c => c.Id).ToHashSet();
                    var validationErrors = new List<string>();

                    foreach (var cust in newCustomers)
                    {
                        if (!IsValidCustomer(cust, ids, validationErrors))
                        {
                            continue; // Skip to the next customer if validation fails
                        }

                        InsertCustomerSorted(cust);
                        ids.Add(cust.Id);
                    }

                    if (validationErrors.Any())
                    {
                        log.LogWarning($"Validation errors: {string.Join(", ", validationErrors)}");
                        return new BadRequestObjectResult(string.Join(", ", validationErrors));
                    }

                    SaveCustomersToFile();
                    log.LogInformation("Customers added successfully.");
                    return new OkObjectResult("Customers added successfully.");
                }
                catch (Exception ex)
                {
                    log.LogError($"Error processing customers: {ex.Message}");
                    return new StatusCodeResult(StatusCodes.Status500InternalServerError);
                }
            }

            return new OkResult();
        }

        private static bool IsValidCustomer(Customers cust, HashSet<int> existingIds, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(cust.FirstName) || string.IsNullOrWhiteSpace(cust.LastName))
            {
                errors.Add($"Customer ID {cust.Id}: First name and last name cannot be empty.");
                return false;
            }

            if (cust.Age <= 0)
            {
                errors.Add($"Customer ID {cust.Id}: Age must be a positive number.");
                return false;
            }

            if (cust.Age <= 18)
            {
                errors.Add($"Customer ID {cust.Id}: Age must be over 18.");
                return false;
            }

            if (cust.Id <= 0)
            {
                errors.Add($"Customer ID {cust.Id}: ID must be a positive number.");
                return false;
            }

            if (existingIds.Contains(cust.Id))
            {
                errors.Add($"Customer ID {cust.Id} already exists.");
                return false;
            }

            return true;
        }

        private static void InsertCustomerSorted(Customers cust)
        {
            int index = 0;
            while (index < _customers.Count)
            {
                var existing = _customers[index];
                int compare = string.Compare(cust.LastName, existing.LastName, StringComparison.OrdinalIgnoreCase);
                if (compare < 0 || (compare == 0 && string.Compare(cust.FirstName, existing.FirstName, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    break;
                }
                index++;
            }
            _customers.Insert(index, cust);
        }

        private static void SaveCustomersToFile()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_customers);
                File.WriteAllText(StorageFile, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error saving customers to file: {ex.Message}");
            }
        }
    }

    public class Customers
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public int Age { get; set; }
        public int Id { get; set; }
    }
}