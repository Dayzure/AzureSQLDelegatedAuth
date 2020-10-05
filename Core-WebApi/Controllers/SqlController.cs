using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace Core_WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SqlController : ControllerBase
    {
        private readonly ILogger<SqlController> _logger;
        private readonly IConfiguration _config;

        public SqlController(ILogger<SqlController> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        [HttpGet]
        [Authorize]
        public IEnumerable<TableModel> Get()
        {

            /**
             * SAMPLE
             * Get the original JWT Bearer from the Authorization header
             **/
            var authZhdr = Request.Headers.FirstOrDefault(h => h.Key.Equals("Authorization"));
            var token = authZhdr.Value.FirstOrDefault().Substring(7);

            /**
             * SAMPLE
             * User the original Token as user assertion
             * Get a new token for SQL using on-behalf-of flow
             * by providing Web API's client_id, client_secret and the user assertion 
             * WebAPI is granted Delegated permission on the SQL
             * The User must have account in the SQL Server, otherwise Authentication will fail
             * **/

            IConfidentialClientApplication clnt = ConfidentialClientApplicationBuilder
                .Create(_config.GetValue<string>("AzureAd:ClientId"))
                .WithClientSecret(_config.GetValue<string>("AzureAd:ClientSecret"))
                .WithAuthority(AadAuthorityAudience.AzureAdMyOrg)
                .WithTenantId(_config.GetValue<string>("AzureAd:TenantId"))
                .Build();
            UserAssertion ua = new UserAssertion(token);

            var res = clnt.AcquireTokenOnBehalfOf(new string[] { "https://sql.azuresynapse-dogfood.net/user_impersonation" }, ua)
                .ExecuteAsync().Result;

            _logger.LogInformation(res.AccessToken);

            var resultList = new List<TableModel>();

            try
            {
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

                builder.DataSource = "demo-test-adauth.database.windows.net";
                builder.InitialCatalog = "demoaadauth";


                using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
                {
                    /**
                     * Use the new access token - obtained using on-behalf-of flow
                     * with the SQL Database
                     * **/
                    connection.AccessToken = res.AccessToken;
                    connection.Open();

                    _logger.LogInformation("Query data example:");

                    using (SqlCommand command = new SqlCommand("SELECT * from t1", connection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                resultList.Add(
                                    new TableModel()
                                    {
                                        Id = reader.GetInt32(0),
                                        Value = reader.GetString(1)
                                    }
                                    );
                                _logger.LogInformation("read single record");
                            }
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                _logger.LogInformation("blah {0}...", e.Message);
            }
            return resultList;
        }
    }

    public class TableModel
    {
        public int Id { get; set; }
        public string Value { get; set; }
    }
}
